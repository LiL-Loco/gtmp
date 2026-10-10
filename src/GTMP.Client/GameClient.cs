using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using GTMP.Shared;
using GTMP.Shared.Protocol;

namespace GTMP.Client;

/// <summary>Outcome of a connection attempt.</summary>
public enum ConnectResult
{
    Accepted,
    Rejected,
    ProtocolMismatch,
    WrongPassword,
    ServerFull,
    ServerClosed,
    Timeout,
    NetworkError
}

/// <summary>
/// Reference client that speaks the server protocol.
/// </summary>
/// <remarks>
/// Deliberately headless: it exists to prove the protocol and to make the server
/// observable without the game. Everything it does - handshake, entity tracking,
/// movement, chat - is exactly what a real client would do, so the wire format stays
/// honest.
/// </remarks>
public sealed class GameClient : IDisposable
{
    private const int MaxDatagramSize = 2048;

    private readonly Socket _socket;
    private readonly IPEndPoint _server;
    private readonly ConcurrentQueue<ReceivedPacket> _inbound = new();
    private readonly ConcurrentDictionary<NetHandle, EntityProperties> _entities = new();
    private readonly ConcurrentDictionary<ushort, string> _playersById = new();

    private Task? _receiveTask;
    private volatile bool _disposed;

    public GameClient(string host, int port, string name, Action<string, Exception>? onError = null)
    {
        Name = name;
        _onError = onError ?? ((_, _) => { });

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        // Binding explicitly matters: an unbound socket lets ReceiveFrom pick an
        // address on some platforms and throw on others, and the receive loop would
        // then die silently before the handshake answer ever arrives.
        _socket.Bind(new IPEndPoint(IPAddress.Any, 0));

        _server = new IPEndPoint(Dns.GetHostAddresses(host)
            .First(a => a.AddressFamily == AddressFamily.InterNetwork), port);

        // The receive loop has to run before the handshake is sent. The server
        // answers immediately, and a datagram that is never read simply stays in the
        // socket buffer until the process dies.
        StartReceiving();
    }

    private readonly Action<string, Exception> _onError;

    public string Name { get; }

    public bool IsConnected { get; private set; }

    public ushort PlayerHandleId { get; private set; }

    public string ServerName { get; private set; } = string.Empty;

    public int TicksPerSecond { get; private set; }

    public int MaxPlayers { get; private set; }

    /// <summary>Entities currently known, keyed by their full handle.</summary>
    public IReadOnlyCollection<EntityProperties> Entities => _entities.Values.ToArray();

    public IReadOnlyDictionary<ushort, string> Players => _playersById;

    public IReadOnlyCollection<string> ChatLog => _chatLog.ToArray();

    private readonly ConcurrentQueue<string> _chatLog = new();

    public ConnectResult Connect(string password = "", int timeoutMs = 5000)
    {
        if (!TryResolveAddress(out _))
        {
            return ConnectResult.NetworkError;
        }

        try
        {
            SendHello(password);
        }
        catch (SocketException)
        {
            return ConnectResult.NetworkError;
        }

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (_inbound.TryDequeue(out var packet))
            {
                if (packet.Type == PacketType.ServerHelloAccepted)
                {
                    ReadHello(packet.Payload);
                    IsConnected = true;
                    return ConnectResult.Accepted;
                }

                if (packet.Type == PacketType.ServerHelloRejected)
                {
                    var reader = new PacketReader(packet.Payload);
                    var reason = (RejectReason)reader.ReadByte();
                    return reason switch
                    {
                        RejectReason.ProtocolMismatch => ConnectResult.ProtocolMismatch,
                        RejectReason.WrongPassword => ConnectResult.WrongPassword,
                        RejectReason.ServerFull => ConnectResult.ServerFull,
                        _ => ConnectResult.Rejected
                    };
                }
            }

            Thread.Sleep(10);
        }

        return ConnectResult.Timeout;
    }

    /// <summary>Applies a local position change and tells the server about it.</summary>
    public void Move(Vector3 position)
    {
        if (!IsConnected) return;

        var writer = new PacketWriter();
        writer.WriteUInt16(PlayerHandleId);
        writer.WriteByte((byte)EntityType.Player);
        writer.WriteBool(true);
        writer.WriteUInt16((ushort)EntityField.Position);
        writer.WriteSingle(position.X);
        writer.WriteSingle(position.Y);
        writer.WriteSingle(position.Z);

        Send(PacketChannel.EntitySync, PacketType.EntityUpdate, writer.Span);
    }

    public void SendChat(string text)
    {
        if (!IsConnected) return;

        var writer = new PacketWriter();
        writer.WriteString(text, 240);

        Send(PacketChannel.Event, PacketType.ChatMessage, writer.Span);
    }

    /// <summary>
    /// Number of packets handed to the world state so far. Diagnostics only, but it is
    /// the difference between "nothing arrived" and "nothing applied".
    /// </summary>
    public long PacketsProcessed { get; private set; }

    /// <summary>Counts processed packets per type. Diagnostics for integration tests.</summary>
    public ConcurrentDictionary<string, int> PacketCounts { get; } = new();

    /// <summary>
    /// Drains everything that arrived since the last call and applies it. Returns how
    /// many packets were processed so a caller can idle efficiently.
    /// </summary>
    public int Pump()
    {
        var processed = 0;

        while (_inbound.TryDequeue(out var packet))
        {
            try
            {
                Apply(packet);
                processed++;
                PacketsProcessed++;
                PacketCounts.AddOrUpdate(packet.Type.ToString(), 1, (_, n) => n + 1);
            }
            catch (Exception exception)
            {
                // One malformed packet must not kill the receive side; the rest of the
                // stream is still usable and the server will resend on the next update.
                _onError($"Failed to apply {packet.Type}.", exception);
            }
        }

        return processed;
    }

    private void Apply(ReceivedPacket packet)
    {
        switch (packet.Type)
        {
            case PacketType.EntityCreate:
                ApplyCreate(packet.Payload);
                break;

            case PacketType.EntityUpdate:
                ApplyUpdate(packet.Payload);
                break;

            case PacketType.EntityDelete:
                ApplyDelete(packet.Payload);
                break;

            case PacketType.ChatMessage:
                ApplyChat(packet.Payload);
                break;

            case PacketType.PlayerConnected:
                ApplyPlayerConnected(packet.Payload);
                break;

            case PacketType.PlayerDisconnected:
                ApplyPlayerDisconnected(packet.Payload);
                break;
        }
    }

    private void ApplyCreate(ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);

        // Decoding lives next to the encoder in EntityMessages so the two cannot drift.
        var entity = EntityMessages.ReadCreate(ref reader);
        var handle = entity.Handle;

        _entities[handle] = entity;

        if (handle.IsPlayer) _playersById[handle.Id] = entity is PlayerProperties p ? p.Name : string.Empty;
    }

    private void ApplyUpdate(ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);

        var handle = EntityMessages.ReadHandle(ref reader);
        var fields = (EntityField)reader.ReadUInt16();

        if (!_entities.TryGetValue(handle, out var existing))
        {
            // An update for something we never received a create for. The server only
            // sends those after a join, so the only safe reaction is to ignore it
            // rather than invent state.
            return;
        }

        EntityUpdateApplier.Apply(ref reader, existing, fields);
    }

    private void ApplyDelete(ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);
        var handle = EntityMessages.ReadHandle(ref reader);

        _entities.TryRemove(handle, out _);
        _playersById.TryRemove(handle.Id, out _);
    }

    private void ApplyChat(ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);

        var senderId = reader.ReadUInt16();
        var senderName = reader.ReadString() ?? string.Empty;
        var text = reader.ReadString() ?? string.Empty;

        _chatLog.Enqueue($"<{senderName}> {text}");
    }

    private void ApplyPlayerConnected(ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);

        var id = reader.ReadUInt16();
        var name = reader.ReadString() ?? string.Empty;

        _playersById[id] = name;
        _chatLog.Enqueue($"* {name} joined");
    }

    private void ApplyPlayerDisconnected(ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);
        var id = reader.ReadUInt16();

        if (_playersById.TryRemove(id, out var name)) _chatLog.Enqueue($"* {name} left");
    }


    private void ReadHello(ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);

        ServerName = reader.ReadString() ?? string.Empty;
        TicksPerSecond = reader.ReadInt32();
        MaxPlayers = reader.ReadInt32();
        PlayerHandleId = reader.ReadUInt16();
    }

    private void SendHello(string password)
    {
        var writer = new PacketWriter();
        writer.WriteByte(PacketHeader.ProtocolVersion);
        writer.WriteString(Name, EntityMessages.MaxNameByteCount);
        writer.WriteString("0.1-reference-client");
        writer.WriteString(password);

        Send(PacketChannel.Handshake, PacketType.ClientHello, writer.Span);
    }

    private bool TryResolveAddress(out IPAddress? address)
    {
        address = null;

        if (IPAddress.TryParse(_server.Address.ToString(), out var parsed))
        {
            address = parsed;
            return true;
        }

        var resolved = Dns.GetHostAddresses(_server.Address.ToString())
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

        address = resolved;
        return resolved is not null;
    }

    private void StartReceiving() => _receiveTask = Task.Run(ReceiveLoop);

    private void Send(PacketChannel channel, PacketType type, ReadOnlySpan<byte> payload)
    {
        if (_disposed) return;

        var datagram = new byte[PacketHeader.Size + payload.Length];
        PacketHeader.Write(datagram, channel, type, payload.Length);
        payload.CopyTo(datagram.AsSpan(PacketHeader.Size));

        _socket.SendTo(datagram, 0, datagram.Length, SocketFlags.None, _server);
    }

    private void ReceiveLoop()
    {
        var buffer = new byte[MaxDatagramSize];

        while (!_disposed)
        {
            try
            {
                EndPoint any = new IPEndPoint(IPAddress.Any, 0);
                var received = _socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref any);

                if (!PacketHeader.TryRead(buffer.AsSpan(0, received), out _, out var type, out var payloadLength))
                    continue;

                var payload = new byte[payloadLength];
                Array.Copy(buffer, PacketHeader.Size, payload, 0, payloadLength);

                _inbound.Enqueue(new ReceivedPacket(type, payload));
            }
            catch (SocketException exception)
            {
                _onError("Client receive loop stopped.", exception);
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;

        try
        {
            _receiveTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // The receive task ends with a socket exception on shutdown; expected.
        }

        _socket.Dispose();
    }

    private readonly record struct ReceivedPacket(PacketType Type, byte[] Payload);
}