namespace GTMP.Shared;

public class PedProperties : EntityProperties
{
    public uint ModelHash { get; set; }

    public int Health { get; set; } = 100;

    public int Armour { get; set; }

    public uint WeaponHash { get; set; }

    /// <summary>Handle of the vehicle the ped is sitting in, or null.</summary>
    public NetHandle VehicleHandle { get; set; }

    /// <summary>Handle of the player currently driving this ped, or null.</summary>
    public NetHandle OwnerHandle { get; set; }

    /// <summary>Free-form tag used by game modes to classify peds.</summary>
    public string Type { get; set; } = string.Empty;

    public override void ApplyUpdate(EntityProperties update)
    {
        base.ApplyUpdate(update);

        if (update is not PedProperties pedUpdate) return;

        ModelHash = pedUpdate.ModelHash;
        Health = pedUpdate.Health;
        Armour = pedUpdate.Armour;
        WeaponHash = pedUpdate.WeaponHash;
        VehicleHandle = pedUpdate.VehicleHandle;
        OwnerHandle = pedUpdate.OwnerHandle;
        Type = pedUpdate.Type;
    }
}