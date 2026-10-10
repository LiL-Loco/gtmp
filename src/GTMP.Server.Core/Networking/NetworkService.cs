using System.Collections.Concurrent;
using System.Net;
using GTMP.Shared;
using GTMP.Shared.Protocol;

namespace GTMP.Server.Core.Networking;

/// <summary>
/// Owns the socket, the session table and the packet dispatch.
/// </summary>
/// <remarks>
/// The receive thread must not touch the entity registry, because the tick thread
/// mutates it. Incoming packets are therefore queued and drained at a fixed point in
/// the tick, which keeps all world mutations on one thread.
/// </remarks>
public sealed class NetworkService : IDisposable
{
    private readonly ServerContext _context;
    private readonly UdpServer _udpServer;
    private readonly ConcurrentDictionary<long, ClientSession> _sessions = new();
    private readonly ConcurrentQueue<InboundPacket> _inbound = new();

    private long _nextSessionId;
    private bool _acceptingConnections = true;

    public NetworkService(ServerContext context)
    {
        _context = context;
        _udpServer = new UdpServer(OnDatagram, (message, exception) => _context.Logger.Error(message, exception));
    }

    /// <summary>
    /// Called after the handshake succeeded, before the full entity sync is sent.
    /// </summary>
    public event Action<ClientSession>? PlayerConnected;

    public event Action<ClientSession>? PlayerDisconnected;

    public int SessionCount => _sessions.Count;

    public int ConnectedPlayerCount => _sessions.Values.Count(s => s.IsHandshakeComplete);

    public long DroppedPackets => _udpServer.DroppedPackets;

    public void Start() => _udpServer.Start(_context.Settings.Port);

    public bool AcceptingConnections
    {
        get => _acceptingConnections;
        set => _acceptingConnections = value;
    }

    public void Send(ClientSession session, PacketChannel channel, PacketType type, ReadOnlySpan<byte> payload) =>
        _udpServer.Send(session.EndPoint, channel, type, payload);

    /// <summary>Sends to every session that finished the handshake.</summary>
    public void Broadcast(PacketChannel channel, PacketType type, ReadOnlySpan<byte> payload)
    {
        foreach (var session in HandshakeCompletedSessions())
            Send(session, channel, type, payload);
    }

    public IEnumerable<ClientSession> HandshakeCompletedSessions() =>
        _sessions.Values.Where(s => s.IsHandshakeComplete && !s.IsDisconnected);

    /// <summary>
    /// Processes everything that arrived since the last call. Must run on the tick
    /// thread, never on the receive thread.
    /// </summary>
    public void PumpInbound()
    {
        while (_inbound.TryDequeue(out var packet))
        {
            try
            {
                Dispatch(packet);
            }
            catch (Exception exception)
            {
                _context.Logger.Error($"Failed to process packet from {packet.EndPoint}.", exception);
            }
        }

        ReapTimedOutSessions();
    }

    private void Dispatch(InboundPacket packet)
    {
        if (!_sessions.TryGetValue(packet.SessionId, out var session) || session.IsDisconnected)
            return;

        session.Touch();

        switch (packet.Type)
        {
            case PacketType.ClientHello:
                HandleClientHello(session, packet.Payload);
                break;

            case PacketType.EntityUpdate:
                HandleEntityUpdate(session, packet.Payload);
                break;

            case PacketType.ChatMessage:
                HandleChat(session, packet.Payload);
                break;

            default:
                _context.Logger.Warn($"Unhandled packet type {packet.Type} from {session}.");
                break;
        }
    }

    private void HandleClientHello(ClientSession session, ReadOnlyMemory<byte> payload)
    {
        if (session.IsHandshakeComplete) return;

        var reader = new PacketReader(payload.Span);

        var clientProtocol = reader.ReadByte();
        var name = reader.ReadString() ?? string.Empty;
        var clientVersion = reader.ReadString() ?? string.Empty;

        if (clientProtocol != PacketHeader.ProtocolVersion)
        {
            Reject(session, RejectReason.ProtocolMismatch);
            return;
        }

        if (!string.IsNullOrEmpty(_context.Settings.Password))
        {
            var password = reader.ReadString() ?? string.Empty;

            if (!string.Equals(password, _context.Settings.Password, StringComparison.Ordinal))
            {
                Reject(session, RejectReason.WrongPassword);
                return;
            }
        }

        if (!_acceptingConnections)
        {
            Reject(session, RejectReason.ServerNotAcceptingConnections);
            return;
        }

        if (_context.PlayerCount >= _context.Settings.MaxPlayers)
        {
            Reject(session, RejectReason.ServerFull);
            return;
        }

        session.Name = name;
        session.IsHandshakeComplete = true;

        var player = new PlayerProperties { Name = name };
        session.PlayerHandle = _context.Entities.Create(player);

        var writer = new PacketWriter();
        writer.WriteString(_context.Settings.ServerName);
        writer.WriteInt32(_context.Settings.TicksPerSecond);
        writer.WriteInt32(_context.Settings.MaxPlayers);
        writer.WriteUInt16(session.PlayerHandle.Id);

        Send(session, PacketChannel.Handshake, PacketType.ServerHelloAccepted, writer.Span);

        _context.Logger.Info($"{session} connected (client version {clientVersion}).");

        NotifyPlayerConnected(session);
    }

    private void Reject(ClientSession session, RejectReason reason)
    {
        var writer = new PacketWriter();
        writer.WriteByte((byte)reason);
        writer.WriteString(_context.Settings.ServerName);

        Send(session, PacketChannel.Handshake, PacketType.ServerHelloRejected, writer.Span);
        _context.Logger.Info($"{session} rejected: {reason}.");

        session.IsDisconnected = true;
        _sessions.TryRemove(session.SessionId, out _);
    }

    private void HandleEntityUpdate(ClientSession session, ReadOnlyMemory<byte> payload)
    {
        if (!session.IsHandshakeComplete) return;

        var reader = new PacketReader(payload.Span);

        var handle = EntityMessages.ReadHandle(ref reader);

        // A client may only ever write to its own player entity. Accepting arbitrary
        // handles here would let anyone teleport any other player.
        if (handle != session.PlayerHandle)
        {
            _context.Logger.Warn($"{session} tried to update {handle}, which it does not own.");
            return;
        }

        if (_context.Entities.Get<PlayerProperties>(handle) is not { } player) return;

        var fields = (EntityField)reader.ReadUInt16();
        EntityUpdateApplier.Apply(ref reader, player, fields);
    }

    private void HandleChat(ClientSession session, ReadOnlyMemory<byte> payload)
    {
        if (!session.IsHandshakeComplete) return;

        var reader = new PacketReader(payload.Span);
        var text = reader.ReadString() ?? string.Empty;

        BroadcastChat(session.PlayerHandle, session.Name, text);
    }

    public void BroadcastChat(NetHandle sender, string senderName, string text)
    {
        var writer = new PacketWriter();
        writer.WriteUInt16(sender.Id);
        writer.WriteString(senderName, EntityMessages.MaxNameByteCount);
        writer.WriteString(text, 240);

        Broadcast(PacketChannel.Event, PacketType.ChatMessage, writer.Span);
    }

    /// <summary>
    /// Hook the host uses to push the initial entity snapshot. Kept separate so the
    /// replication service can subscribe without the network knowing about it.
    /// </summary>
    internal void SetFullSyncHandler(Action<ClientSession> handler) => _fullSyncHandler = handler;

    private Action<ClientSession>? _fullSyncHandler;

    private void NotifyPlayerConnected(ClientSession session)
    {
        _fullSyncHandler?.Invoke(session);

        var writer = new PacketWriter();
        writer.WriteUInt16(session.PlayerHandle.Id);
        writer.WriteString(session.Name, EntityMessages.MaxNameByteCount);

        Broadcast(PacketChannel.Event, PacketType.PlayerConnected, writer.Span);

        PlayerConnected?.Invoke(session);
    }

    public void Disconnect(ClientSession session)
    {
        if (session.IsDisconnected) return;

        session.IsDisconnected = true;
        _sessions.TryRemove(session.SessionId, out _);

        if (session.IsHandshakeComplete)
        {
            var writer = new PacketWriter();
            writer.WriteUInt16(session.PlayerHandle.Id);
            Broadcast(PacketChannel.Event, PacketType.PlayerDisconnected, writer.Span);

            _context.Entities.Delete(session.PlayerHandle);
            _context.Logger.Info($"{session} disconnected.");
        }

        PlayerDisconnected?.Invoke(session);
    }

    /// <summary>
    /// Drops sessions that stopped sending. Without this a vanished player would stay
    /// in the world forever, because UDP has no disconnect packet.
    /// </summary>
    private void ReapTimedOutSessions()
    {
        foreach (var session in _sessions.Values.ToList())
        {
            if (!session.IsTimedOut) continue;

            _context.Logger.Info($"Session {session} timed out.");
            Disconnect(session);
        }
    }

    private void OnDatagram(IPEndPoint endPoint, PacketChannel channel, PacketType type, ReadOnlyMemory<byte> payload)
    {
        var sessionId = ResolveSession(endPoint);
        _inbound.Enqueue(new InboundPacket(endPoint, sessionId, channel, type, payload));
    }

    private long ResolveSession(IPEndPoint endPoint)
    {
        foreach (var session in _sessions.Values)
        {
            if (session.EndPoint.Equals(endPoint)) return session.SessionId;
        }

        // Unknown peer: create a session so the handshake can be answered. It only
        // becomes a player once the handshake succeeds.
        var created = new ClientSession(endPoint, Interlocked.Increment(ref _nextSessionId));
        _sessions[created.SessionId] = created;
        return created.SessionId;
    }

    public void Dispose() => _udpServer.Dispose();

    private readonly record struct InboundPacket(
        IPEndPoint EndPoint,
        long SessionId,
        PacketChannel Channel,
        PacketType Type,
        ReadOnlyMemory<byte> Payload);
}