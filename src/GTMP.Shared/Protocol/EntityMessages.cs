using System.Numerics;
using GTMP.Shared;

namespace GTMP.Shared.Protocol;

/// <summary>
/// Which fields an <see cref="PacketType.EntityUpdate"/> actually carries.
/// </summary>
/// <remarks>
/// The server sets a dirty flag per field when something changes it and clears it on
/// the next successful send. That keeps a steady state player from re-sending 30
/// unchanged floats every tick, which is what makes a full snapshot protocol
/// affordable at 20 ticks per second.
/// </remarks>
[Flags]
public enum EntityField : ushort
{
    None = 0,
    Position = 1 << 0,
    Rotation = 1 << 1,
    Alpha = 1 << 2,
    Flags = 1 << 3,
    Model = 1 << 4,
    Health = 1 << 5,
    Armour = 1 << 6,
    Name = 1 << 7,
    Weapon = 1 << 8,
    Colour = 1 << 9,
    MovementState = 1 << 10,

    /// <summary>Everything, used for the initial full sync when a client joins.</summary>
    All = Position | Rotation | Alpha | Flags | Model | Health | Armour | Name | Weapon | Colour | MovementState
}

/// <summary>Serialisation of the entity messages.</summary>
public static class EntityMessages
{
    /// <summary>Hard cap on a player supplied name so a long name cannot exhaust a packet.</summary>
    public const int MaxNameByteCount = 48;

    /// <summary>
    /// A complete snapshot of one entity.
    /// </summary>
    /// <remarks>
    /// The model is written exactly once, by <see cref="WriteCommonState"/> under
    /// <see cref="EntityField.Model"/>. Writing it here as well produced two strings in
    /// the stream, and the reader then consumed a float as a string length.
    /// </remarks>
    public static void WriteCreate(PacketWriter writer, EntityProperties entity)
    {
        WriteHandle(writer, entity.Handle);
        WriteCommonState(writer, entity, EntityField.All);
    }

    public static void WriteUpdate(PacketWriter writer, EntityProperties entity, EntityField dirty)
    {
        WriteHandle(writer, entity.Handle);
        writer.WriteUInt16((ushort)dirty);
        WriteCommonState(writer, entity, dirty);
    }

    public static void WriteDelete(PacketWriter writer, NetHandle handle) => WriteHandle(writer, handle);

    /// <summary>
    /// Writes only the fields flagged in <paramref name="fields"/>. The reader must be
    /// called with the identical mask, which is why the mask travels inside the packet.
    /// </summary>
    public static void WriteCommonState(PacketWriter writer, EntityProperties entity, EntityField fields)
    {
        if (fields.HasFlag(EntityField.Position))
        {
            writer.WriteSingle(entity.Position.X);
            writer.WriteSingle(entity.Position.Y);
            writer.WriteSingle(entity.Position.Z);
        }

        if (fields.HasFlag(EntityField.Rotation))
        {
            writer.WriteSingle(entity.Rotation.X);
            writer.WriteSingle(entity.Rotation.Y);
            writer.WriteSingle(entity.Rotation.Z);
            writer.WriteSingle(entity.Rotation.W);
        }

        if (fields.HasFlag(EntityField.Alpha)) writer.WriteSingle(entity.Alpha);

        if (fields.HasFlag(EntityField.Flags)) writer.WriteByte((byte)entity.Flags);

        if (fields.HasFlag(EntityField.Model)) writer.WriteString(entity.Model);

        switch (entity)
        {
            case VehicleProperties vehicle:
                WriteVehicleState(writer, vehicle, fields);
                break;

            case PlayerProperties player:
                WritePlayerState(writer, player, fields);
                break;

            case PedProperties ped:
                WritePedState(writer, ped, fields);
                break;
        }
    }

    private static void WriteVehicleState(PacketWriter writer, VehicleProperties vehicle, EntityField fields)
    {
        if (fields.HasFlag(EntityField.Health)) writer.WriteInt32(vehicle.Health);
        if (fields.HasFlag(EntityField.Colour))
        {
            writer.WriteByte(vehicle.Colour1);
            writer.WriteByte(vehicle.Colour2);
            writer.WriteByte(vehicle.Colour3);
            writer.WriteByte(vehicle.Colour4);
        }

        if (fields.HasFlag(EntityField.MovementState))
        {
            // Lights is a plain enum, not a flags enum, so it has to be widened
            // before it can share a byte with the boolean vehicle states.
            var state = (int)vehicle.Lights
                        | (vehicle.SirenActive ? 1 << 8 : 0)
                        | (vehicle.HasDriver ? 1 << 9 : 0);

            writer.WriteUInt32((uint)state);
        }
    }

    private static void WritePlayerState(PacketWriter writer, PlayerProperties player, EntityField fields)
    {
        if (fields.HasFlag(EntityField.Health)) writer.WriteInt32(player.Health);
        if (fields.HasFlag(EntityField.Armour)) writer.WriteInt32(player.Armour);
        if (fields.HasFlag(EntityField.Name)) writer.WriteString(player.Name, MaxNameByteCount);
        if (fields.HasFlag(EntityField.Weapon)) writer.WriteUInt32(player.WeaponHash);
    }

    private static void WritePedState(PacketWriter writer, PedProperties ped, EntityField fields)
    {
        if (fields.HasFlag(EntityField.Health)) writer.WriteInt32(ped.Health);
        if (fields.HasFlag(EntityField.Armour)) writer.WriteInt32(ped.Armour);
        if (fields.HasFlag(EntityField.Weapon)) writer.WriteUInt32(ped.WeaponHash);
    }

    private static void WriteHandle(PacketWriter writer, NetHandle handle)
    {
        writer.WriteUInt16(handle.Id);
        writer.WriteByte((byte)handle.EntityType);
        writer.WriteBool(handle.IsPlayer);
    }

    public static NetHandle ReadHandle(ref PacketReader reader)
    {
        var id = reader.ReadUInt16();
        var entityType = (EntityType)reader.ReadByte();
        var isPlayer = reader.ReadBool();

        return NetHandle.Create(id, entityType, isPlayer);
    }

    /// <summary>
    /// Decodes a packet written by <see cref="WriteCreate"/> into a fresh entity.
    /// </summary>
    /// <remarks>
    /// Encoding and decoding live side by side on purpose. When they were separate
    /// implementations, a duplicated field on the write side went unnoticed until an
    /// end to end test failed, because the low level tests never decoded the body.
    /// </remarks>
    public static EntityProperties ReadCreate(ref PacketReader reader)
    {
        var handle = ReadHandle(ref reader);

        var entity = CreatePropertiesFor(handle);

        // The freshly created properties start with a null handle. Without this line
        // every decoded entity collapses onto handle id 0 and the last one wins.
        entity.Handle = handle;

        EntityUpdateApplier.Apply(ref reader, entity, EntityField.All);

        return entity;
    }

    private static EntityProperties CreatePropertiesFor(NetHandle handle) => handle.EntityType switch
    {
        EntityType.Player => new PlayerProperties(),
        EntityType.Vehicle => new VehicleProperties(),
        EntityType.Ped => new PedProperties(),
        _ => new EntityProperties()
    };
}

/// <summary>Applies a decoded update onto a stored entity, guided by the transmitted mask.</summary>
public static class EntityUpdateApplier
{
    public static void Apply(ref PacketReader reader, EntityProperties target, EntityField fields)
    {
        if (fields.HasFlag(EntityField.Position))
            target.Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        if (fields.HasFlag(EntityField.Rotation))
            target.Rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        if (fields.HasFlag(EntityField.Alpha)) target.Alpha = reader.ReadSingle();

        if (fields.HasFlag(EntityField.Flags)) target.Flags = (EntityFlag)reader.ReadByte();

        if (fields.HasFlag(EntityField.Model)) target.Model = reader.ReadString() ?? string.Empty;

        switch (target)
        {
            case VehicleProperties vehicle:
                if (fields.HasFlag(EntityField.Health)) vehicle.Health = reader.ReadInt32();
                if (fields.HasFlag(EntityField.Colour))
                {
                    vehicle.Colour1 = reader.ReadByte();
                    vehicle.Colour2 = reader.ReadByte();
                    vehicle.Colour3 = reader.ReadByte();
                    vehicle.Colour4 = reader.ReadByte();
                }

                if (fields.HasFlag(EntityField.MovementState))
                {
                    var state = reader.ReadUInt32();
                    vehicle.Lights = (Lights)(state & 0xFF);
                    vehicle.SirenActive = (state & (1 << 8)) != 0;
                    vehicle.HasDriver = (state & (1 << 9)) != 0;
                }

                break;

            case PlayerProperties player:
                if (fields.HasFlag(EntityField.Health)) player.Health = reader.ReadInt32();
                if (fields.HasFlag(EntityField.Armour)) player.Armour = reader.ReadInt32();
                if (fields.HasFlag(EntityField.Name)) player.Name = reader.ReadString() ?? string.Empty;
                if (fields.HasFlag(EntityField.Weapon)) player.WeaponHash = reader.ReadUInt32();
                break;

            case PedProperties ped:
                if (fields.HasFlag(EntityField.Health)) ped.Health = reader.ReadInt32();
                if (fields.HasFlag(EntityField.Armour)) ped.Armour = reader.ReadInt32();
                if (fields.HasFlag(EntityField.Weapon)) ped.WeaponHash = reader.ReadUInt32();
                break;
        }
    }
}