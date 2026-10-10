using System;

namespace GTMP.Shared;

/// <summary>
/// Identifies an entity across the whole system. Every networked object - players,
/// vehicles, peds, props, blips - carries one, and scripts use it as the primary key.
/// </summary>
/// <remarks>
/// The numeric value is split into an id and an entity type so that handles of
/// different types can never collide while sharing one id space per type.
/// </remarks>
public readonly struct NetHandle : IEquatable<NetHandle>
{
    /// <summary>Per-type running id. Valid range is 1..65535, 0 means "no entity".</summary>
    public ushort Id { get; }

    public EntityType EntityType { get; }

    /// <summary>
    /// True for player handles. Players are addressable from scripts both as a player
    /// handle and as an entity handle, which is why the flag is tracked separately.
    /// </summary>
    public bool IsPlayer { get; }

    private NetHandle(ushort id, EntityType entityType, bool isPlayer)
    {
        Id = id;
        EntityType = entityType;
        IsPlayer = isPlayer;
    }

    public static NetHandle Create(ushort id, EntityType entityType, bool isPlayer = false)
    {
        if (id == 0) throw new ArgumentOutOfRangeException(nameof(id), "Entity id must not be zero.");

        return new NetHandle(id, entityType, isPlayer);
    }

    public static NetHandle CreatePlayer(ushort id) => Create(id, EntityType.Player, isPlayer: true);

    public bool IsNull => Id == 0;

    public bool Equals(NetHandle other) => Id == other.Id && EntityType == other.EntityType && IsPlayer == other.IsPlayer;

    public override bool Equals(object? obj) => obj is NetHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, EntityType, IsPlayer);

    public override string ToString() => IsNull
        ? "NetHandle(null)"
        : $"NetHandle({EntityType}#{Id}{(IsPlayer ? ", player)" : ")")}";

    public static bool operator ==(NetHandle left, NetHandle right) => left.Equals(right);

    public static bool operator !=(NetHandle left, NetHandle right) => !left.Equals(right);
}