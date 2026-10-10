using System.Numerics;

namespace GTMP.Shared;

/// <summary>
/// Everything the server keeps about an entity. Per-type subclasses add their own
/// fields; everything shared lives here so replication can treat all entity types
/// uniformly.
/// </summary>
public class EntityProperties
{
    public NetHandle Handle { get; set; }

    /// <summary>Model name, e.g. "adder" or "v_ind_weed_bag".</summary>
    public string Model { get; set; } = string.Empty;

    public Vector3 Position { get; set; }

    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    /// <summary>Opacity 0.0 - 1.0. 1.0 means fully solid.</summary>
    public float Alpha { get; set; } = 1.0f;

    public EntityFlag Flags { get; set; } = EntityFlag.None;

    public EntityProperties()
    {
    }

    public EntityProperties(NetHandle handle) => Handle = handle;

    /// <summary>
    /// Copies everything a replicator may overwrite on an existing entity. Used when
    /// an update packet arrives for a handle the server already knows.
    /// </summary>
    public virtual void ApplyUpdate(EntityProperties update)
    {
        Model = update.Model;
        Position = update.Position;
        Rotation = update.Rotation;
        Alpha = update.Alpha;
        Flags = update.Flags;
    }

    public virtual EntityProperties Clone() => (EntityProperties)MemberwiseClone();
}