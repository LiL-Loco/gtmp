using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using GTMP.Server.Core;
using GTMP.Server.Core.Configuration;
using GTMP.Server.Core.Networking;
using GTMP.Server.Core.Runtime;
using GTMP.Shared;
using GTMP.Shared.Protocol;

namespace GTMP.Server.Tests;

/// <summary>
/// Boots a real server on a free UDP port and drives its tick loop on demand, so the
/// tests never have to sleep and wait for real time.
/// </summary>
internal sealed class ServerHarness : IDisposable
{
    private readonly NetworkService _network;
    private readonly EntityReplicationService _replication;
    private readonly TickLoop _tickLoop;

    private ServerHarness(
        ServerSettings settings,
        ServerContext context,
        NetworkService network,
        EntityReplicationService replication,
        TickLoop tickLoop)
    {
        Settings = settings;
        Context = context;
        _network = network;
        _replication = replication;
        _tickLoop = tickLoop;
    }

    public ServerSettings Settings { get; }

    public ServerContext Context { get; }

    public NetworkService Network => _network;

    public int Port => Settings.Port;

    /// <summary>
    /// Finds a free UDP port. There is a small race between releasing it here and
    /// binding it in the server, which is acceptable for a loopback test run.
    /// </summary>
    public static ServerHarness Start(string password = "")
    {
        var settings = new ServerSettings
        {
            Port = FindFreeUdpPort(),
            Password = password,
            TickRate = 20,
            MaxPlayers = 8,
            ServerName = "Test Server"
        };

        var context = new ServerContext(settings, new InMemoryLogger());
        var network = new NetworkService(context);
        var replication = new EntityReplicationService(context, network);
        network.SetFullSyncHandler(replication.SendFullSyncTo);

        var tickLoop = new TickLoop
        {
            Settings = settings,
            ShouldContinue = () => true
        };

        tickLoop.Register(new Pump(network));
        tickLoop.Register(replication);

        network.Start();

        return new ServerHarness(settings, context, network, replication, tickLoop);
    }

    /// <summary>Advances the server by <paramref name="count"/> ticks.</summary>
    public (int UpdatesSent, int DeletesSent) RunTicks(int count)
    {
        var updatesBefore = _replication.UpdatesSent;
        var deletesBefore = _replication.DeletesSent;

        for (var i = 0; i < count; i++) _tickLoop.StepOnce();

        return (_replication.UpdatesSent - updatesBefore, _replication.DeletesSent - deletesBefore);
    }

    /// <summary>Advances the server by a single tick, for use while a test is waiting.</summary>
    public void PumpOnce() => _tickLoop.StepOnce();

    public void Dispose() => _network.Dispose();

    private static int FindFreeUdpPort()
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint!).Port;
    }

    private sealed class Pump(NetworkService network) : IServerTick
    {
        public void Tick(long tickNumber) => network.PumpInbound();
    }
}

/// <summary>A bare UDP client that speaks the protocol directly.</summary>
internal sealed class TestClient : IDisposable
{
    private readonly Socket _socket;
    private readonly IPEndPoint _server;
    private readonly Action _pumpServer;
    private readonly ConcurrentQueue<(PacketType Type, byte[] Payload)> _received = new();

    /// <param name="pumpServer">
    /// Advances the server while this client waits. The server only processes packets
    /// on a tick, so without this a client would block until its own timeout.
    /// </param>
    public TestClient(int serverPort, Action pumpServer)
    {
        _pumpServer = pumpServer;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        _socket.ReceiveTimeout = 200;
        _server = new IPEndPoint(IPAddress.Loopback, serverPort);

        _ = Task.Run(ReceiveLoop);
    }

    public ushort PlayerHandleId { get; private set; }

    public void SendHello(string name, string password = "", byte protocolVersion = PacketHeader.ProtocolVersion)
    {
        var writer = new PacketWriter();
        writer.WriteByte(protocolVersion);
        writer.WriteString(name, EntityMessages.MaxNameByteCount);
        writer.WriteString("0.1-test-client");
        writer.WriteString(password);

        Send(PacketChannel.Handshake, PacketType.ClientHello, writer.Span);
    }

    public void SendEntityUpdate(ushort handleId, EntityField fields, Vector3 position)
    {
        var writer = new PacketWriter();
        writer.WriteUInt16(handleId);
        writer.WriteByte((byte)EntityType.Player);
        writer.WriteBool(true);
        writer.WriteUInt16((ushort)fields);

        if (fields.HasFlag(EntityField.Position))
        {
            writer.WriteSingle(position.X);
            writer.WriteSingle(position.Y);
            writer.WriteSingle(position.Z);
        }

        Send(PacketChannel.EntitySync, PacketType.EntityUpdate, writer.Span);
    }

    public void SendRaw(byte[] datagram)
    {
        try
        {
            _socket.SendTo(datagram, 0, datagram.Length, SocketFlags.None, _server);
        }
        catch (SocketException)
        {
            // An empty datagram is rejected by the OS; that is a valid test input.
        }
    }

    /// <summary>Waits for a handshake answer and records the assigned player handle.</summary>
    public (PacketType Type, byte[] Payload) ReceiveHandshake(int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (_received.TryDequeue(out var message))
            {
                if (message.Type == PacketType.ServerHelloAccepted)
                {
                    var reader = new PacketReader(message.Payload);
                    reader.ReadString();
                    reader.ReadInt32();
                    reader.ReadInt32();
                    PlayerHandleId = reader.ReadUInt16();
                }

                return message;
            }

            _pumpServer();
            Thread.Sleep(5);
        }

        throw new TimeoutException("No handshake response received.");
    }

    public byte[] ReceiveEntityPacket(PacketType expected, int timeoutMs = 5000)
    {
        var payloads = ReceiveEntityPackets(expected, 1, timeoutMs);
        return payloads[0];
    }

    public List<byte[]> ReceiveEntityPackets(PacketType expected, int count, int timeoutMs = 5000)
    {
        var collected = new List<byte[]>();
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (collected.Count < count && DateTime.UtcNow < deadline)
        {
            if (_received.TryDequeue(out var message) && message.Type == expected)
                collected.Add(message.Payload);

            _pumpServer();
            Thread.Sleep(5);
        }

        if (collected.Count < count)
            throw new TimeoutException($"Expected {count}x {expected}, got {collected.Count}.");

        return collected;
    }

    private void Send(PacketChannel channel, PacketType type, ReadOnlySpan<byte> payload)
    {
        var datagram = new byte[PacketHeader.Size + payload.Length];
        PacketHeader.Write(datagram, channel, type, payload.Length);
        payload.CopyTo(datagram.AsSpan(PacketHeader.Size));

        _socket.SendTo(datagram, 0, datagram.Length, SocketFlags.None, _server);
    }

    private void ReceiveLoop()
    {
        var buffer = new byte[2048];

        while (true)
        {
            try
            {
                EndPoint any = new IPEndPoint(IPAddress.Any, 0);
                var received = _socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref any);

                if (!PacketHeader.TryRead(buffer.AsSpan(0, received), out _, out var type, out var payloadLength))
                    continue;

                var payload = new byte[payloadLength];
                Array.Copy(buffer, PacketHeader.Size, payload, 0, payloadLength);

                _received.Enqueue((type, payload));
            }
            catch (SocketException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public void Dispose() => _socket.Dispose();
}