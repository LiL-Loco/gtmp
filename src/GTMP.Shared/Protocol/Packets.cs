namespace GTMP.Shared.Protocol;

/// <summary>
/// Logical separation of packet streams. Everything on one channel is delivered in
/// order, but a channel carrying chat must never be blocked behind a position sync.
/// </summary>
public enum PacketChannel : byte
{
    /// <summary>Connection handshake and version negotiation.</summary>
    Handshake = 0,

    /// <summary>Authoritative entity state. Loss is tolerated, the next update supersedes it.</summary>
    EntitySync = 1,

    /// <summary>Reliable, ordered gameplay events. Nothing here may be dropped.</summary>
    Event = 2,

    /// <summary>Script driven calls in either direction.</summary>
    Scripting = 3
}

/// <summary>Message identifiers, scoped to their <see cref="PacketChannel"/>.</summary>
public enum PacketType : byte
{
    None = 0,

    // Handshake
    ClientHello = 1,
    ServerHelloAccepted = 2,
    ServerHelloRejected = 3,

    // EntitySync
    EntityCreate = 10,
    EntityUpdate = 11,
    EntityDelete = 12,

    // Event
    ChatMessage = 20,
    PlayerConnected = 21,
    PlayerDisconnected = 22,

    // Scripting
    ScriptTick = 30
}

/// <summary>Why the server refused a connection.</summary>
public enum RejectReason : byte
{
    None = 0,
    ProtocolMismatch = 1,
    WrongPassword = 2,
    ServerFull = 3,
    ServerNotAcceptingConnections = 4
}