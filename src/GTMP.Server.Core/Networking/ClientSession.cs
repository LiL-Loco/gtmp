using System.Net;
using System.Net.Sockets;
using GTMP.Shared.Protocol;

namespace GTMP.Server.Core.Networking;

/// <summary>
/// One connected or connecting client.
/// </summary>
public sealed class ClientSession
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    public ClientSession(IPEndPoint endPoint, long sessionId)
    {
        EndPoint = endPoint;
        SessionId = sessionId;
        CreatedAt = DateTime.UtcNow;
    }

    public IPEndPoint EndPoint { get; }

    /// <summary>Stable identity for this socket; a reconnect gets a new id.</summary>
    public long SessionId { get; }

    public string Name { get; set; } = string.Empty;

    public Shared.NetHandle PlayerHandle { get; set; }

    public bool IsHandshakeComplete { get; set; }

    public bool IsDisconnected { get; set; }

    public DateTime CreatedAt { get; }

    public DateTime LastSeenAt { get; private set; } = DateTime.UtcNow;

    /// <summary>Entities already pushed to this client, so replication can skip resending them.</summary>
    public HashSet<Shared.NetHandle> KnownEntities { get; } = new();

    public void Touch() => LastSeenAt = DateTime.UtcNow;

    public bool IsTimedOut => DateTime.UtcNow - LastSeenAt > HandshakeTimeout;

    public override string ToString() => $"#{SessionId} {(IsHandshakeComplete ? Name : "<handshaking>")} @ {EndPoint}";
}