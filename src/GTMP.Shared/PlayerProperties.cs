using System.Numerics;

namespace GTMP.Shared;

public class PlayerProperties : EntityProperties
{
    /// <summary>Account name as reported by the client during connection.</summary>
    public string Name { get; set; } = string.Empty;

    public int Ping { get; set; }

    public int Money { get; set; }

    public int WantedLevel { get; set; }

    public int Health { get; set; } = 100;

    public int Armour { get; set; }

    public uint WeaponHash { get; set; }

    /// <summary>Handle of the vehicle the player is currently driving, or null.</summary>
    public NetHandle VehicleHandle { get; set; }

    /// <summary>True while the player is dead and waiting to respawn.</summary>
    public bool IsDead { get; set; }

    /// <summary>True while the player is in spectator mode.</summary>
    public bool IsSpectating { get; set; }

    /// <summary>Current animation index, 0 when no animation is playing.</summary>
    public int Animation { get; set; }

    public Vector3 AimTarget { get; set; }

    public override void ApplyUpdate(EntityProperties update)
    {
        base.ApplyUpdate(update);

        if (update is not PlayerProperties playerUpdate) return;

        Name = playerUpdate.Name;
        Ping = playerUpdate.Ping;
        Money = playerUpdate.Money;
        WantedLevel = playerUpdate.WantedLevel;
        Health = playerUpdate.Health;
        Armour = playerUpdate.Armour;
        WeaponHash = playerUpdate.WeaponHash;
        VehicleHandle = playerUpdate.VehicleHandle;
        IsDead = playerUpdate.IsDead;
        IsSpectating = playerUpdate.IsSpectating;
        Animation = playerUpdate.Animation;
        AimTarget = playerUpdate.AimTarget;
    }
}