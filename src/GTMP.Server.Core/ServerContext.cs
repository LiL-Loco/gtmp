using GTMP.Server.Core.Configuration;
using GTMP.Server.Core.Runtime;
using GTMP.Server.Core.World;

namespace GTMP.Server.Core;

/// <summary>
/// The object graph handed to every subsystem and to the scripting layer. Keeping the
/// context explicit means a native script function never has to reach for a static.
/// </summary>
public sealed class ServerContext
{
    public ServerContext(ServerSettings settings, IServerLogger logger)
    {
        Settings = settings;
        Logger = logger;
    }

    public ServerSettings Settings { get; }

    public IServerLogger Logger { get; }

    public EntityRegistry Entities { get; } = new();

    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

    /// <summary>Current simulation tick. Zero before the tick loop has run.</summary>
    public long TickNumber { get; internal set; }

    public TimeSpan Uptime => DateTime.UtcNow - StartedAtUtc;

    public int PlayerCount => Entities.OfType<Shared.PlayerProperties>().Count();
}