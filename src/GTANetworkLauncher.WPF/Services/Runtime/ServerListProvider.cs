using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using GrandTheftMultiplayer.Launcher.Helpers;
using GrandTheftMultiplayer.Launcher.Models.VerifiedServers;
using GrandTheftMultiplayer.Launcher.Services.Interfaces;
using ServerModel = GrandTheftMultiplayer.Launcher.Models.ServerApi.Server;

namespace GrandTheftMultiplayer.Launcher.Services.Runtime
{
    public class ServerListProvider : IServerListProvider
    {
        private const int CacheTimeInMinutes = 5;

        private readonly ISettingsProvider _settingsProvider;
        private readonly IServerListIpsProvider _serverListIpsProvider;
        private readonly IServerQueryingService _serverQueryingService;
        private readonly ICollection<ServerModel> _cachedServerList = new HashSet<ServerModel>();
        private readonly TaskQueue _getServersTaskQueue = new TaskQueue();

        private DateTime? _cacheTime;

        public event EventDelegates.FavoritedServersUpdatedHandler FavoritedServersUpdated;

        public ServerListProvider(ISettingsProvider settingsProvider, IServerListIpsProvider serverListIpsProvider, IServerQueryingService serverQueryingService)
        {
            _settingsProvider = settingsProvider;
            _serverListIpsProvider = serverListIpsProvider;
            _serverQueryingService = serverQueryingService;
        }

        private static async Task<ICollection<string>> GetVerifiedServerIps()
        {
            var verifiedServerIps = (await HttpDataHelper.GetDataFromUrl<VerifiedServersResponse>(Constants.VerfiedServersUrl))?.List;

            return verifiedServerIps ?? new string[0];
        }

        private ICollection<string> GetFavoriteServerIps()
        {
            var favoriteServerIps = _settingsProvider.GetCurrentSettings()?.FavoriteServers;

            return favoriteServerIps ?? new List<string>();
        }

        private ICollection<string> GetRecentServerIps()
        {
            var recentServerIps = _settingsProvider.GetCurrentSettings()?.RecentServers;

            return recentServerIps ?? new List<string>();
        }

        public Task<IEnumerable<ServerModel>> GetServers(bool refreshCache = false)
        {
            return _getServersTaskQueue.Enqueue(() => InternalGetServersAsync(refreshCache));
        }

        private async Task<IEnumerable<ServerModel>> InternalGetServersAsync(bool refreshCache)
        {
            if (!refreshCache && _cachedServerList != null && _cacheTime.HasValue &&
                DateTime.Now.Subtract(_cacheTime.Value).TotalMinutes <= CacheTimeInMinutes)
                return _cachedServerList;

            var verifiedServerIps = await GetVerifiedServerIps();
            var favoriteServerIps = GetFavoriteServerIps();
            var recentServerIps = GetRecentServerIps();

            _cacheTime = DateTime.Now;

            foreach (var server in _cachedServerList)
            {
                server.UpdateInfo();
            }

            var servers = await _serverListIpsProvider.GetServerListIps();
            if (servers == null)
            {
                _cachedServerList.Clear();
                return _cachedServerList;
            }

            var deletedServers = new List<ServerModel>(_cachedServerList);
            foreach (var server in servers)
            {
                var serverAddress = server.IpPortAddress;

                // Compared by address string instead of GetHashCode: a hash collision
                // would otherwise merge two servers, or flag an unrelated one as verified.
                var currentServer = _cachedServerList.FirstOrDefault(s =>
                    string.Equals(s.UniqueAddress, serverAddress, StringComparison.OrdinalIgnoreCase));

                if (currentServer != null)
                {
                    deletedServers.Remove(currentServer);
                    continue;
                }

                var newServer = new ServerModel(serverAddress, _serverQueryingService)
                {
                    IsVerified = ContainsAddress(verifiedServerIps, serverAddress),
                    IsFavorited = ContainsAddress(favoriteServerIps, serverAddress),
                    IsRecent = ContainsAddress(recentServerIps, serverAddress),
                    Name = server.ServerName,
                    MaxPlayers = server.MaxPlayers,
                    IsPasswordProtected = server.Passworded,
                    PlayerCount = server.CurrentPlayers,
                    GameMode = server.GameMode
                };

                newServer.PropertyChanged += ServerPropertyUpdated;
                _cachedServerList.Add(newServer);

                newServer.UpdateInfo();
            }

            deletedServers.ForEach(server =>
            {
                _cachedServerList.Remove(server);
                server.PropertyChanged -= ServerPropertyUpdated;
            });

            return _cachedServerList;
        }

        private static bool ContainsAddress(IEnumerable<string> addresses, string address)
        {
            return addresses.Any(a => string.Equals(a, address, StringComparison.OrdinalIgnoreCase));
        }

        private void ServerPropertyUpdated(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ServerModel.IsFavorited)) return;

            FavoritedServersUpdated?.Invoke();
        }
    }
}
