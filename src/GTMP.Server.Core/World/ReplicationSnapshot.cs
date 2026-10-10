using System.Numerics;
using GTMP.Server.Core.Networking;
using GTMP.Shared;
using GTMP.Shared.Protocol;

namespace GTMP.Server.Core.World;

/// <summary>
/// Last state that was replicated for an entity, used to derive the delta mask for
/// the next update.
/// </summary>
public readonly record struct ReplicationSnapshot(
    Vector3 Position,
    Quaternion Rotation,
    float Alpha,
    EntityFlag Flags,
    string Model,
    int Health,
    int Armour,
    string Name,
    uint Weapon,
    byte Colour1,
    byte Colour2,
    byte Colour3,
    byte Colour4,
    byte MovementState)
{
    public static ReplicationSnapshot From(EntityProperties entity) => entity switch
    {
        VehicleProperties vehicle => new ReplicationSnapshot(
            entity.Position, entity.Rotation, entity.Alpha, entity.Flags, entity.Model,
            vehicle.Health, 0, string.Empty, 0,
            vehicle.Colour1, vehicle.Colour2, vehicle.Colour3, vehicle.Colour4,
            (byte)((int)vehicle.Lights | (vehicle.SirenActive ? 1 << 8 : 0) | (vehicle.HasDriver ? 1 << 9 : 0))),

        PlayerProperties player => new ReplicationSnapshot(
            entity.Position, entity.Rotation, entity.Alpha, entity.Flags, entity.Model,
            player.Health, player.Armour, player.Name, player.WeaponHash,
            0, 0, 0, 0, 0),

        PedProperties ped => new ReplicationSnapshot(
            entity.Position, entity.Rotation, entity.Alpha, entity.Flags, entity.Model,
            ped.Health, ped.Armour, string.Empty, ped.WeaponHash,
            0, 0, 0, 0, 0),

        _ => new ReplicationSnapshot(
            entity.Position, entity.Rotation, entity.Alpha, entity.Flags, entity.Model,
            0, 0, string.Empty, 0, 0, 0, 0, 0, 0)
    };

    /// <summary>
    /// Compares against the current state and returns only the fields that differ.
    /// Position and rotation are compared with a tolerance because clients report
    /// float noise every frame; without it nothing but garbage would ever be deltaed.
    /// </summary>
    public EntityField DiffAgainst(EntityProperties entity)
    {
        var fields = EntityField.None;

        if (!PositionApproxEqual(Position, entity.Position)) fields |= EntityField.Position;
        if (!RotationApproxEqual(Rotation, entity.Rotation)) fields |= EntityField.Rotation;
        if (Math.Abs(Alpha - entity.Alpha) > AlphaEpsilon) fields |= EntityField.Alpha;
        if (Flags != entity.Flags) fields |= EntityField.Flags;
        if (!string.Equals(Model, entity.Model, StringComparison.Ordinal)) fields |= EntityField.Model;

        switch (entity)
        {
            case VehicleProperties vehicle:
                if (Health != vehicle.Health) fields |= EntityField.Health;
                if (Colour1 != vehicle.Colour1 || Colour2 != vehicle.Colour2 ||
                    Colour3 != vehicle.Colour3 || Colour4 != vehicle.Colour4) fields |= EntityField.Colour;
                if (MovementState != CurrentMovementState(vehicle)) fields |= EntityField.MovementState;
                break;

            case PlayerProperties player:
                if (Health != player.Health) fields |= EntityField.Health;
                if (Armour != player.Armour) fields |= EntityField.Armour;
                if (!string.Equals(Name, player.Name, StringComparison.Ordinal)) fields |= EntityField.Name;
                if (Weapon != player.WeaponHash) fields |= EntityField.Weapon;
                break;

            case PedProperties ped:
                if (Health != ped.Health) fields |= EntityField.Health;
                if (Armour != ped.Armour) fields |= EntityField.Armour;
                if (Weapon != ped.WeaponHash) fields |= EntityField.Weapon;
                break;
        }

        return fields;
    }

    private const float PositionEpsilon = 0.05f;
    private const float RotationEpsilon = 0.001f;
    private const float AlphaEpsilon = 0.001f;

    private static byte CurrentMovementState(VehicleProperties vehicle) =>
        (byte)((int)vehicle.Lights | (vehicle.SirenActive ? 1 << 8 : 0) | (vehicle.HasDriver ? 1 << 9 : 0));

    private static bool PositionApproxEqual(Vector3 a, Vector3 b) =>
        Math.Abs(a.X - b.X) <= PositionEpsilon &&
        Math.Abs(a.Y - b.Y) <= PositionEpsilon &&
        Math.Abs(a.Z - b.Z) <= PositionEpsilon;

    private static bool RotationApproxEqual(Quaternion a, Quaternion b) =>
        Math.Abs(a.X - b.X) <= RotationEpsilon &&
        Math.Abs(a.Y - b.Y) <= RotationEpsilon &&
        Math.Abs(a.Z - b.Z) <= RotationEpsilon &&
        Math.Abs(a.W - b.W) <= RotationEpsilon;
}