using System.Net;
using System.Net.Sockets;
using System.Numerics;
using GTMP.Client;
using GTMP.Server.Core;
using GTMP.Server.Core.Configuration;
using GTMP.Server.Core.Networking;
using GTMP.Server.Core.World;
using GTMP.Shared;
using GTMP.Shared.Protocol;
using Xunit;

namespace GTMP.Server.Tests;

/// <summary>
/// Drives the shipped <see cref="GameClient"/> against a real server instance.
/// This is the end to end contract: if these pass, a client and a server of this
/// version can actually talk to each other.
/// </summary>
public sealed class ClientIntegrationTests
{
    private const int Timeout = 5000;

    [Fact]
    public void ClientConnectsAndReceivesTheServerIdentity()
    {
        using var harness = ServerHarness.Start();
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        using var client = new GameClient("127.0.0.1", harness.Port, "Alice", (m, e) => errors.Add($"{m} {e?.Message}"));

        var result = client.Connect(timeoutMs: Timeout);

        Assert.True(result == ConnectResult.Accepted,
            $"result={result} | client errors: {string.Join(" / ", errors)} | " +
            $"server log: {string.Join(" / ", harness.Log.Lines)}");

        Assert.Equal(harness.Settings.ServerName, client.ServerName);
        Assert.True(client.PlayerHandleId > 0);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public void WrongPasswordIsRejectedWithAReadableReason()
    {
        using var harness = ServerHarness.Start(password: "secret");

        using var client = new GameClient("127.0.0.1", harness.Port, "Mallory");

        Assert.Equal(ConnectResult.WrongPassword, client.Connect(password: "wrong", timeoutMs: Timeout));
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void ProtocolMismatchIsRejected()
    {
        using var harness = ServerHarness.Start();

        // A client speaking a future protocol version must be told so, not dropped.
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram,
            System.Net.Sockets.ProtocolType.Udp);

        socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        socket.ReceiveTimeout = Timeout;

        var writer = new PacketWriter();
        writer.WriteByte((byte)(PacketHeader.ProtocolVersion + 1));
        writer.WriteString("Future", EntityMessages.MaxNameByteCount);
        writer.WriteString("9.9");
        writer.WriteString(string.Empty);

        Send(socket, harness.Port, PacketChannel.Handshake, PacketType.ClientHello, writer.Span);

        EndPoint any = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
        var buffer = new byte[2048];
        var received = socket.ReceiveFrom(buffer, 0, buffer.Length, System.Net.Sockets.SocketFlags.None, ref any);

        Assert.True(PacketHeader.TryRead(buffer.AsSpan(0, received), out var channel, out var type, out var length));
        Assert.Equal(PacketChannel.Handshake, channel);
        Assert.Equal(PacketType.ServerHelloRejected, type);

        var reader = new PacketReader(buffer.AsSpan(PacketHeader.Size, length));
        Assert.Equal((byte)RejectReason.ProtocolMismatch, reader.ReadByte());
    }

    [Fact]
    public void ClientSeesEntitiesThatExistedBeforeItJoined()
    {
        using var harness = ServerHarness.Start();

        harness.Context.Entities.Create(new VehicleProperties { Model = "adder" });
        harness.Context.Entities.Create(new PedProperties { Model = "s_m_y_boob_01" });

        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var client = new GameClient("127.0.0.1", harness.Port, "Bob", (m, e) => errors.Add($"{m}: {e?.Message}"));
        Assert.Equal(ConnectResult.Accepted, client.Connect(timeoutMs: Timeout));

        Assert.True(WaitUntil(client, () =>
                client.Entities.Any(e => e.Model == "adder") &&
                client.Entities.Any(e => e.Model == "s_m_y_boob_01")),
            $"entities seen: [{string.Join(", ", client.Entities.Select(e => e.Model))}] " +
            $"processed={client.PacketsProcessed} types=[{string.Join(",", client.PacketCounts.Select(kv => kv.Key + "=" + kv.Value))}] errors=[{string.Join(" | ", errors)}]");

        // Plus the client's own player entity.
        Assert.Equal(3, client.Entities.Count);
    }

    [Fact]
    public void ClientMovementIsReplicatedByTheServer()
    {
        using var harness = ServerHarness.Start();

        using var client = new GameClient("127.0.0.1", harness.Port, "Mover");
        Assert.Equal(ConnectResult.Accepted, client.Connect(timeoutMs: Timeout));

        var target = new Vector3(88.5f, 12.25f, -3.75f);
        client.Move(target);

        var handle = NetHandle.CreatePlayer(client.PlayerHandleId);

        Assert.True(WaitFor(() =>
        {
            var player = harness.Context.Entities.Get<PlayerProperties>(handle);
            return player is not null && Vector3.Distance(player.Position, target) < 0.1f;
        }), $"server never applied the move. server log: {string.Join(" / ", harness.Log.Lines)}");

        var applied = harness.Context.Entities.Get<PlayerProperties>(handle)!;
        Assert.Equal(target.X, applied.Position.X, 2);
        Assert.Equal(target.Y, applied.Position.Y, 2);
        Assert.Equal(target.Z, applied.Position.Z, 2);
    }

    [Fact]
    public void ChatIsRelayedToOtherClients()
    {
        using var harness = ServerHarness.Start();

        using var listener = new GameClient("127.0.0.1", harness.Port, "Listener");
        Assert.Equal(ConnectResult.Accepted, listener.Connect(timeoutMs: Timeout));

        using var talker = new GameClient("127.0.0.1", harness.Port, "Talker");
        Assert.Equal(ConnectResult.Accepted, talker.Connect(timeoutMs: Timeout));

        Assert.True(WaitUntil(listener, () => listener.Players.Count >= 2),
            $"listener saw {listener.Players.Count} players");

        talker.SendChat("hello world");

        Assert.True(WaitUntil(listener, () => listener.ChatLog.Any(l => l.Contains("hello world", StringComparison.Ordinal))),
            $"chat log: {string.Join(" / ", listener.ChatLog)}");
    }

    [Fact]
    public void ClientSeesOtherPlayersJoinAndLeave()
    {
        // A short server side timeout, because a vanished peer has to be reaped and
        // UDP offers no way to tell the server about it.
        using var harness = ServerHarness.Start(sessionTimeoutSeconds: 2);

        using var watcher = new GameClient("127.0.0.1", harness.Port, "Watcher");
        Assert.Equal(ConnectResult.Accepted, watcher.Connect(timeoutMs: Timeout));

        var joiner = new GameClient("127.0.0.1", harness.Port, "Joiner");
        Assert.Equal(ConnectResult.Accepted, joiner.Connect(timeoutMs: Timeout));

        Assert.True(WaitUntil(watcher, () => watcher.Players.Count >= 2),
            $"watcher saw {watcher.Players.Count} players");

        // The watcher has to keep talking, otherwise the server reaps it as well.
        joiner.Dispose();

        Assert.True(WaitUntil(watcher, () =>
        {
            watcher.Move(new Vector3(1, 0, 0));
            return watcher.Players.Count == 1;
        }, Timeout), $"watcher ended with {watcher.Players.Count} players");
    }

    [Fact]
    public void ClientIgnoresAnUpdateForAnEntityItNeverReceived()
    {
        using var harness = ServerHarness.Start();

        using var client = new GameClient("127.0.0.1", harness.Port, "Confused");
        Assert.Equal(ConnectResult.Accepted, client.Connect(timeoutMs: Timeout));

        var writer = new PacketWriter();
        writer.WriteUInt16(999);
        writer.WriteByte((byte)EntityType.Vehicle);
        writer.WriteBool(false);
        writer.WriteUInt16((ushort)EntityField.Position);
        writer.WriteSingle(1f);
        writer.WriteSingle(2f);
        writer.WriteSingle(3f);

        SendFromThrowawaySocket(harness.Port, PacketChannel.EntitySync, PacketType.EntityUpdate, writer.Span);

        Thread.Sleep(200);
        client.Pump();

        Assert.DoesNotContain(client.Entities, e => e.Handle.Id == 999);
    }

    private static void Send(Socket socket, int port, PacketChannel channel, PacketType type, ReadOnlySpan<byte> payload)
    {
        var datagram = new byte[PacketHeader.Size + payload.Length];
        PacketHeader.Write(datagram, channel, type, payload.Length);
        payload.CopyTo(datagram.AsSpan(PacketHeader.Size));

        socket.SendTo(datagram, 0, datagram.Length, SocketFlags.None,
            new IPEndPoint(IPAddress.Loopback, port));
    }

    private static void SendFromThrowawaySocket(int port, PacketChannel channel, PacketType type, ReadOnlySpan<byte> payload)
    {
        // A separate socket, because the server keys sessions by endpoint and a crafted
        // datagram from the client's own socket would corrupt that session.
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        Send(socket, port, channel, type, payload);
    }

    private static bool WaitUntil(GameClient client, Func<bool> condition, int timeoutMs = Timeout)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            client.Pump();

            if (condition()) return true;

            Thread.Sleep(10);
        }

        client.Pump();
        return condition();
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs = Timeout)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;

            Thread.Sleep(10);
        }

        return condition();
    }
}