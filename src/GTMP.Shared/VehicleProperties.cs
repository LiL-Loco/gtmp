namespace GTMP.Shared;

public class VehicleProperties : EntityProperties
{
    /// <summary>Hash of the vehicle model, used by scripts instead of the model name.</summary>
    public uint ModelHash { get; set; }

    public int Health { get; set; } = 1000;

    public int MaxHealth { get; set; } = 1000;

    public byte Colour1 { get; set; }

    public byte Colour2 { get; set; }

    public byte Colour3 { get; set; }

    public byte Colour4 { get; set; }

    public Lights Lights { get; set; } = Lights.NormalLights;

    /// <summary>True while the vehicle is occupied by a driver.</summary>
    public bool HasDriver { get; set; }

    /// <summary>True when the vehicle is an emergency vehicle with an active siren.</summary>
    public bool SirenActive { get; set; }

    /// <summary>Locked vehicles reject entry until unlocked.</summary>
    public bool IsLocked => Flags.HasFlag(EntityFlag.VehicleLocked);

    public override void ApplyUpdate(EntityProperties update)
    {
        base.ApplyUpdate(update);

        if (update is not VehicleProperties vehicleUpdate) return;

        ModelHash = vehicleUpdate.ModelHash;
        Health = vehicleUpdate.Health;
        MaxHealth = vehicleUpdate.MaxHealth;
        Colour1 = vehicleUpdate.Colour1;
        Colour2 = vehicleUpdate.Colour2;
        Colour3 = vehicleUpdate.Colour3;
        Colour4 = vehicleUpdate.Colour4;
        Lights = vehicleUpdate.Lights;
        HasDriver = vehicleUpdate.HasDriver;
        SirenActive = vehicleUpdate.SirenActive;
    }
}