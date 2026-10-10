using System.Numerics;
using GTMP.Scripting.Hosting;
using GTMP.Shared;

namespace GTMP.Server.Core.Scripting;

/// <summary>
/// The server API as the scripting layer sees it.
/// </summary>
/// <remarks>
/// Every native takes untyped arguments, so each one validates and converts its own
/// parameters and returns <c>null</c> instead of throwing. A game mode that passes a
/// string where a handle belongs should get a sensible default, not a stack trace.
/// </remarks>
public sealed class ServerNatives
{
    private readonly ServerContext _context;
    private readonly Networking.NetworkService _network;
    private readonly NativeFunctionRegistry _registry = new();

    public ServerNatives(ServerContext context, Networking.NetworkService network)
    {
        _context = context;
        _network = network;
        Register();
    }

    public NativeFunctionRegistry Registry => _registry;

    private void Register()
    {
        // --- server information -------------------------------------------------
        _registry.Register("GetServerName", 0, _ => _context.Settings.ServerName);
        _registry.Register("GetPlayerCount", 0, _ => (double)_context.PlayerCount);
        _registry.Register("GetMaxPlayers", 0, _ => (double)_context.Settings.MaxPlayers);
        _registry.Register("GetTickCount", 0, _ => (double)_context.TickNumber);
        _registry.Register("GetUptime", 0, _ => _context.Uptime.TotalSeconds);

        // --- players ------------------------------------------------------------
        _registry.Register("GetPlayerName", 1, args =>
            GetPlayer(args[0]) is { } player ? player.Name : string.Empty);

        _registry.Register("GetPlayerId", 1, args => (double)ToHandleId(args[0]));

        _registry.Register("GetPlayerMoney", 1, args =>
            GetPlayer(args[0]) is { } player ? (double)player.Money : 0.0);

        _registry.Register("SetPlayerMoney", 2, args =>
        {
            if (GetPlayer(args[0]) is not { } player) return false;

            player.Money = ToInt(args[1]);
            return true;
        });

        _registry.Register("GivePlayerMoney", 2, args =>
        {
            if (GetPlayer(args[0]) is not { } player) return false;

            player.Money += ToInt(args[1]);
            return true;
        });

        _registry.Register("GetPlayerHealth", 1, args =>
            GetPlayer(args[0]) is { } player ? (double)player.Health : 0.0);

        _registry.Register("SetPlayerHealth", 2, args =>
        {
            if (GetPlayer(args[0]) is not { } player) return false;

            player.Health = ToInt(args[1]);
            return true;
        });

        // --- entities -----------------------------------------------------------
        _registry.Register("CreateVehicle", 4, args =>
        {
            var handle = _context.Entities.Create(new VehicleProperties
            {
                Model = ToString(args[0]),
                Position = new Vector3((float)ToDouble(args[1]), (float)ToDouble(args[2]), (float)ToDouble(args[3]))
            });

            return (double)handle.Id;
        });

        _registry.Register("GetEntityPosition", 1, args =>
        {
            if (ToEntity(args[0]) is not { } entity) return null;

            return new object?[] { entity.Position.X, entity.Position.Y, entity.Position.Z };
        });

        _registry.Register("SetEntityPosition", 4, args =>
        {
            if (ToEntity(args[0]) is not { } entity) return false;

            entity.Position = new Vector3((float)ToDouble(args[1]), (float)ToDouble(args[2]), (float)ToDouble(args[3]));
            return true;
        });

        _registry.Register("GetEntityModel", 1, args =>
            ToEntity(args[0])?.Model ?? string.Empty);

        _registry.Register("DeleteEntity", 1, args =>
        {
            if (ToEntity(args[0]) is not { } entity) return false;

            return _context.Entities.Delete(entity.Handle);
        });

        // --- world --------------------------------------------------------------
        _registry.Register("BroadcastChat", 2, args =>
        {
            var text = ToString(args[1]);

            _network.BroadcastChat(default, string.Empty, text);
            _context.Logger.Info($"[chat] {text}");
            return true;
        });

        _registry.Register("SendPlayerMessage", 2, args =>
        {
            var session = _network.HandshakeCompletedSessions()
                .FirstOrDefault(s => s.PlayerHandle.Id == ToHandleId(args[0]));

            if (session is null) return false;

            _network.SendChatTo(session, session.PlayerHandle, session.Name, ToString(args[1]));
            return true;
        });

        _registry.Register("Log", 1, args =>
        {
            _context.Logger.Info($"[script] {ToString(args[0])}");
            return true;
        });
    }

    private PlayerProperties? GetPlayer(object? argument)
    {
        var id = ToHandleId(argument);
        return id == 0 ? null : _context.Entities.Get<PlayerProperties>(NetHandle.CreatePlayer((ushort)id));
    }

    private EntityProperties? ToEntity(object? argument)
    {
        var id = ToHandleId(argument);
        if (id == 0) return null;

        foreach (var entity in _context.Entities.All())
        {
            if (entity.Handle.Id == id) return entity;
        }

        return null;
    }

    private static ushort ToHandleId(object? value) => value switch
    {
        double d when d is > 0 and <= ushort.MaxValue => (ushort)d,
        int i when i is > 0 and <= ushort.MaxValue => (ushort)i,
        _ => 0
    };

    private static int ToInt(object? value) => ToDouble(value) is var d ? (int)d : 0;

    private static double ToDouble(object? value) => value switch
    {
        double d => d,
        int i => i,
        bool b => b ? 1 : 0,
        _ => 0
    };

    private static string ToString(object? value) => value switch
    {
        string s => s,
        null => string.Empty,
        _ => value.ToString() ?? string.Empty
    };
}