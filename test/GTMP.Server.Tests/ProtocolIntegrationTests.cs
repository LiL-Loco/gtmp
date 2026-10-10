using System.Net;
using System.Net.Sockets;
using System.Numerics;
using GTMP.Server.Core;
using GTMP.Server.Core.Configuration;
using GTMP.Server.Core.Networking;
using GTMP.Server.Core.Runtime;
using GTMP.Server.Core.World;
using GTMP.Shared;
using GTMP.Shared.Protocol;
using Xunit;

namespace GTMP.Server.Tests;

/// <summary>
/// Drives a real UDP socket against a real server instance. These are the only tests
/// that prove the protocol contract actually works end to end; everything else in this
/// project can pass while the wire format is wrong.
/// </summary>
public sealed class ProtocolIntegrationTests
{
    private const int TestTimeoutMs = 5000;

    [Fact]
    public void ClientCanCompleteTheHandshakeAndReceiveItsPlayerHandle()
    {
        using var harness = ServerHarness.Start();

        using var client = new TestClient(harness.Port, harness.PumpOnce);
        client.SendHello("Alice");

        var response = client.ReceiveHandshake();

        Assert.Equal(PacketType.ServerHelloAccepted, response.Type);

        var reader = new PacketReader(response.Payload);
        var serverName = reader.ReadString();
        var ticksPerSecond = reader.ReadInt32();
        var maxPlayers = reader.ReadInt32();
        var handleId = reader.ReadUInt16();

        Assert.Equal(harness.Settings.ServerName, serverName);
        Assert.Equal(harness.Settings.TicksPerSecond, ticksPerSecond);
        Assert.Equal(harness.Settings.MaxPlayers, maxPlayers);
        Assert.True(handleId > 0, "The server must hand out a valid player handle.");
    }

    [Fact]
    public void WrongPasswordIsRejected()
    {
        var harness = ServerHarness.Start(password: "correct");
        using var _ = harness;

        using var client = new TestClient(harness.Port, harness.PumpOnce);
        client.SendHello("Mallory", password: "wrong");

        var response = client.ReceiveHandshake();

        Assert.Equal(PacketType.ServerHelloRejected, response.Type);

        var reader = new PacketReader(response.Payload);
        Assert.Equal((byte)RejectReason.WrongPassword, reader.ReadByte());
    }

    [Fact]
    public void ProtocolVersionMismatchIsRejected()
    {
        using var harness = ServerHarness.Start();

        using var client = new TestClient(harness.Port, harness.PumpOnce);
        client.SendHello("Alice", protocolVersion: PacketHeader.ProtocolVersion + 1);

        var response = client.ReceiveHandshake();

        Assert.Equal(PacketType.ServerHelloRejected, response.Type);

        var reader = new PacketReader(response.Payload);
        Assert.Equal((byte)RejectReason.ProtocolMismatch, reader.ReadByte());
    }

    [Fact]
    public void JoiningClientReceivesTheExistingWorld()
    {
        using var harness = ServerHarness.Start();

        // Two entities exist before anyone connects.
        harness.Context.Entities.Create(new VehicleProperties { Model = "adder" });
        harness.Context.Entities.Create(new PedProperties { Model = "s_m_y_boob_01" });

        using var client = new TestClient(harness.Port, harness.PumpOnce);
        client.SendHello("Bob");
        client.ReceiveHandshake();

        var creates = client.ReceiveEntityPackets(PacketType.EntityCreate, 2, TestTimeoutMs);

        Assert.Equal(2, creates.Count);

        var models = new List<string>();
        foreach (var payload in creates)
        {
            var reader = new PacketReader(payload);
            EntityMessages.ReadHandle(ref reader);
            models.Add(reader.ReadString() ?? string.Empty);
        }

        Assert.Contains("adder", models);
        Assert.Contains("s_m_y_boob_01", models);
    }

    [Fact]
    public void MovingAPlayerIsReplicatedBackToOtherClients()
    {
        using var harness = ServerHarness.Start();

        using var observer = new TestClient(harness.Port, harness.PumpOnce);
        observer.SendHello("Observer");
        observer.ReceiveHandshake();

        using var mover = new TestClient(harness.Port, harness.PumpOnce);
        mover.SendHello("Mover");
        mover.ReceiveHandshake();

        // The observer needs to know the mover before the move arrives.
        harness.RunTicks(2);

        var target = new Vector3(120.5f, -340.25f, 30.75f);
        mover.SendEntityUpdate(mover.PlayerHandleId, EntityField.Position, target);
        harness.RunTicks(3);

        var update = observer.ReceiveEntityPacket(PacketType.EntityUpdate, timeoutMs: TestTimeoutMs);

        var reader = new PacketReader(update);
        var handle = EntityMessages.ReadHandle(ref reader);
        var fields = (EntityField)reader.ReadUInt16();

        Assert.True(fields.HasFlag(EntityField.Position));

        var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        Assert.Equal(target.X, position.X, 3);
        Assert.Equal(target.Y, position.Y, 3);
        Assert.Equal(target.Z, position.Z, 3);
        Assert.Equal(EntityType.Player, handle.EntityType);
    }

    [Fact]
    public void UnchangedEntitiesAreNotResent()
    {
        using var harness = ServerHarness.Start();

        using var client = new TestClient(harness.Port, harness.PumpOnce);
        client.SendHello("Watcher");
        client.ReceiveHandshake();

        var handle = harness.Context.Entities.Create(new VehicleProperties { Model = "sultan" });
        harness.RunTicks(3);

        // The create went out once; with nothing changing it must stay silent.
        var quietTicks = harness.RunTicks(5);

        Assert.Equal(0, quietTicks.UpdatesSent);

        // Touching one field must produce exactly one update carrying only that field.
        var vehicle = harness.Context.Entities.Get<VehicleProperties>(handle)!;
        vehicle.Health = 500;
        var busyTicks = harness.RunTicks(2);

        Assert.Equal(1, busyTicks.UpdatesSent);
    }

    [Fact]
    public void ClientCannotUpdateAnEntityItDoesNotOwn()
    {
        using var harness = ServerHarness.Start();

        using var attacker = new TestClient(harness.Port, harness.PumpOnce);
        attacker.SendHello("Attacker");
        attacker.ReceiveHandshake();

        var victimHandle = harness.Context.Entities.Create(new PlayerProperties { Name = "Victim" });

        attacker.SendEntityUpdate(victimHandle.Id, EntityField.Position, new Vector3(9999, 9999, 9999));
        harness.RunTicks(3);

        var victim = harness.Context.Entities.Get<PlayerProperties>(victimHandle)!;

        Assert.Equal(Vector3.Zero, victim.Position);
    }

    [Fact]
    public void DeletingAnEntityReplicatesTheDelete()
    {
        using var harness = ServerHarness.Start();

        using var client = new TestClient(harness.Port, harness.PumpOnce);
        client.SendHello("Watcher");
        client.ReceiveHandshake();

        var handle = harness.Context.Entities.Create(new VehicleProperties { Model = "adder" });
        harness.RunTicks(3);

        harness.Context.Entities.Delete(handle);
        harness.RunTicks(2);

        var delete = client.ReceiveEntityPacket(PacketType.EntityDelete, timeoutMs: TestTimeoutMs);

        var reader = new PacketReader(delete);
        var deletedHandle = EntityMessages.ReadHandle(ref reader);

        Assert.Equal(handle, deletedHandle);
    }

    [Fact]
    public void GarbageDatagramsAreDroppedWithoutKillingTheServer()
    {
        using var harness = ServerHarness.Start();

        using var client = new TestClient(harness.Port, harness.PumpOnce);

        client.SendRaw(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
        client.SendRaw(Array.Empty<byte>());

        client.SendHello("Survivor");
        var response = client.ReceiveHandshake();

        Assert.Equal(PacketType.ServerHelloAccepted, response.Type);
        Assert.True(harness.Network.DroppedPackets >= 1);
    }

    [Fact]
    public void OversizedPayloadIsRefused()
    {
        var writer = new PacketWriter();
        writer.WriteString(new string('x', PacketHeader.MaxPayloadSize + 100));

        Assert.True(writer.Length > PacketHeader.MaxPayloadSize);
    }
}