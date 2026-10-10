using GTMP.Shared;

namespace GTMP.Server.Core.World;

/// <summary>
/// Authoritative store of every entity the server knows about.
/// </summary>
/// <remarks>
/// Handles are allocated per entity type from a monotonically increasing counter, and
/// the counter is never rewound - a recycled id would let a late update packet from a
/// deleted entity land on a freshly created one.
/// </remarks>
public sealed class EntityRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<NetHandle, EntityProperties> _entities = new();
    private readonly Dictionary<EntityType, ushort> _nextIdByType = new();

    public int Count
    {
        get
        {
            lock (_gate) return _entities.Count;
        }
    }

    public NetHandle Create(EntityProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        lock (_gate)
        {
            var handle = AllocateHandle(properties.GetType());

            // The caller may have supplied a placeholder handle; the registry owns it.
            var previousHandle = properties.Handle;
            properties.Handle = handle;

            if (!previousHandle.IsNull)
                _entities.Remove(previousHandle);

            _entities[handle] = properties;

            return handle;
        }
    }

    public bool Update(EntityProperties update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (_gate)
        {
            if (!_entities.TryGetValue(update.Handle, out var existing)) return false;

            existing.ApplyUpdate(update);
            return true;
        }
    }

    public bool Delete(NetHandle handle)
    {
        lock (_gate) return _entities.Remove(handle);
    }

    public T? Get<T>(NetHandle handle) where T : EntityProperties
    {
        lock (_gate)
        {
            return _entities.TryGetValue(handle, out var entity) ? entity as T : null;
        }
    }

    public EntityProperties? Get(NetHandle handle)
    {
        lock (_gate)
        {
            return _entities.TryGetValue(handle, out var entity) ? entity : null;
        }
    }

    public IEnumerable<T> OfType<T>() where T : EntityProperties
    {
        lock (_gate)
        {
            return _entities.Values.OfType<T>().ToList();
        }
    }

    public IEnumerable<EntityProperties> All()
    {
        lock (_gate)
        {
            return _entities.Values.ToList();
        }
    }

    private NetHandle AllocateHandle(Type propertiesType)
    {
        // PlayerProperties maps onto EntityType.Player, everything else onto its own type.
        var entityType = ResolveEntityType(propertiesType);

        if (!_nextIdByType.TryGetValue(entityType, out var nextId) || nextId == 0)
            nextId = 1;

        if (nextId == ushort.MaxValue)
            throw new InvalidOperationException($"No more {entityType} handles available.");

        _nextIdByType[entityType] = (ushort)(nextId + 1);

        return NetHandle.Create(nextId, entityType, entityType == EntityType.Player);
    }

    private static EntityType ResolveEntityType(Type propertiesType)
    {
        if (propertiesType == typeof(PlayerProperties)) return EntityType.Player;
        if (propertiesType == typeof(VehicleProperties)) return EntityType.Vehicle;
        if (propertiesType == typeof(PedProperties)) return EntityType.Ped;

        return EntityType.Prop;
    }
}