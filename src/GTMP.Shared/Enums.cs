namespace GTMP.Shared;

/// <summary>
/// Kinds of entity the server can hold. Values are explicit because they end up in
/// the wire format and in saved data; appending new ones is safe, renumbering is not.
/// </summary>
public enum EntityType
{
    None = 0,
    Vehicle = 1,
    Prop = 2,
    Blip = 3,
    Marker = 4,
    Pickup = 5,
    Player = 6,
    TextLabel = 7,
    Ped = 8,
    Particle = 9,
    World = 10
}

/// <summary>Behaviour switches that can be toggled on an entity at runtime.</summary>
[System.Flags]
public enum EntityFlag
{
    None = 0,
    Collisionless = 1 << 0,
    EngineOff = 1 << 1,
    SpecialLight = 1 << 2,
    PlayerSpectating = 1 << 3,
    VehicleLocked = 1 << 4
}

/// <summary>Vehicle light states.</summary>
public enum Lights
{
    NormalLights = 0,
    Highbeams = 1
}

/// <summary>
/// Transient state flags reported every tick alongside ped position data. They are
/// deliberately kept out of <see cref="PedProperties"/> - they describe the current
/// frame, not the entity.
/// </summary>
[System.Flags]
public enum PedDataFlags
{
    None = 0,
    Jumping = 1 << 0,
    Shooting = 1 << 1,
    Aiming = 1 << 2,
    ParachuteOpen = 1 << 3,
    Ragdoll = 1 << 4,
    InMeleeCombat = 1 << 5,
    InFreefall = 1 << 6,
    IsInCover = 1 << 7,
    IsInLowerCover = 1 << 8,
    IsInCoverFacingLeft = 1 << 9,
    IsReloading = 1 << 10,
    HasAimData = 1 << 11,
    IsOnLadder = 1 << 12,
    IsVaulting = 1 << 13,
    EnteringVehicle = 1 << 14,
    ClosingVehicleDoor = 1 << 15,
    OnFire = 1 << 16,
    PlayerDead = 1 << 17
}

/// <summary>Transient state flags reported every tick alongside vehicle data.</summary>
[System.Flags]
public enum VehicleDataFlags
{
    None = 0,
    PressingHorn = 1 << 0,
    Shooting = 1 << 1,
    SirenActive = 1 << 2,
    VehicleDead = 1 << 3,
    Aiming = 1 << 4,
    Driver = 1 << 5,
    HasAimData = 1 << 6,
    BurnOut = 1 << 7,
    ExitingVehicle = 1 << 8,
    PlayerDead = 1 << 9
}

/// <summary>Life-cycle of a downloadable resource as seen by the client.</summary>
public enum ResourceType
{
    Unknown = 0,
    Folder = 1,
    Script = 2,
    Map = 3,
    Meta = 4,
    Archive = 5
}