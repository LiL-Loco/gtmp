using GTMP.Server.Core.Runtime;
using GTMP.Server.Core.World;
using GTMP.Shared;
using GTMP.Shared.Protocol;

namespace GTMP.Server.Core.Networking;

/// <summary>
/// Pushes authoritative entity state to connected clients.
/// </summary>
/// <remarks>
/// Runs on the tick thread. Everything it sends is derived from the snapshot taken in
/// the previous tick, so a change that happens twice between two ticks costs exactly
/// one packet - which is the entire point of the delta mask.
/// </remarks>
public sealed class EntityReplicationService : IServerTick
{
    private readonly ServerContext _context;
    private readonly NetworkService _network;
    private readonly Dictionary<NetHandle, ReplicationSnapshot> _snapshots = new();

    public EntityReplicationService(ServerContext context, NetworkService network)
    {
        _context = context;
        _network = network;
    }

    public int UpdatesSent { get; private set; }

    public int DeletesSent { get; private set; }

    public int FullSyncsSent { get; private set; }

    public void Tick(long tickNumber) => PushDeltas();

    /// <summary>
    /// Sends every entity to a freshly connected client and remembers what it knows,
    /// so the delta stream continues from there instead of resending the world.
    /// </summary>
    public void SendFullSyncTo(ClientSession session)
    {
        foreach (var entity in _context.Entities.All())
        {
            var writer = new PacketWriter();
            EntityMessages.WriteCreate(writer, entity);

            _network.Send(session, PacketChannel.EntitySync, PacketType.EntityCreate, writer.Span);

            _snapshots[entity.Handle] = ReplicationSnapshot.From(entity);
            session.KnownEntities.Add(entity.Handle);
        }

        FullSyncsSent++;
    }

    private void PushDeltas()
    {
        var liveHandles = new HashSet<NetHandle>();

        foreach (var entity in _context.Entities.All())
        {
            liveHandles.Add(entity.Handle);

            _snapshots.TryGetValue(entity.Handle, out var snapshot);

            var dirty = snapshot == default
                ? EntityField.All
                : snapshot.DiffAgainst(entity);

            if (dirty == EntityField.None) continue;

            var writer = new PacketWriter();
            EntityMessages.WriteUpdate(writer, entity, dirty);

            Broadcast(PacketType.EntityUpdate, writer.Span);
            _snapshots[entity.Handle] = ReplicationSnapshot.From(entity);

            UpdatesSent++;
        }

        SendDeletes(liveHandles);
        PruneSnapshots(liveHandles);
    }

    private void SendDeletes(HashSet<NetHandle> liveHandles)
    {
        foreach (var handle in _snapshots.Keys.ToList())
        {
            if (liveHandles.Contains(handle)) continue;

            var writer = new PacketWriter();
            EntityMessages.WriteDelete(writer, handle);

            Broadcast(PacketType.EntityDelete, writer.Span);
            DeletesSent++;
        }
    }

    /// <summary>
    /// Forgets snapshots of entities that no longer exist. Without this the delta map
    /// would grow for the lifetime of the server.
    /// </summary>
    private void PruneSnapshots(HashSet<NetHandle> liveHandles)
    {
        foreach (var handle in _snapshots.Keys.ToList())
        {
            if (!liveHandles.Contains(handle)) _snapshots.Remove(handle);
        }
    }

    private void Broadcast(PacketType type, ReadOnlySpan<byte> payload)
    {
        foreach (var session in _network.HandshakeCompletedSessions())
            _network.Send(session, PacketChannel.EntitySync, type, payload);
    }
}