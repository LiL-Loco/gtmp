using GTMP.Server.Core.World;
using GTMP.Shared;
using Xunit;

namespace GTMP.Server.Tests;

public class EntityRegistryTests
{
    [Fact]
    public void Create_AssignsSequentialHandlesPerType()
    {
        var registry = new EntityRegistry();

        var first = registry.Create(new VehicleProperties());
        var second = registry.Create(new VehicleProperties());

        Assert.Equal(1, first.Id);
        Assert.Equal(2, second.Id);
        Assert.Equal(EntityType.Vehicle, first.EntityType);
    }

    [Fact]
    public void Create_PlayerHandleIsFlaggedAndTyped()
    {
        var registry = new EntityRegistry();

        var handle = registry.Create(new PlayerProperties());

        Assert.Equal(EntityType.Player, handle.EntityType);
        Assert.True(handle.IsPlayer);
    }

    [Fact]
    public void Create_CountsSeparatelyPerEntityType()
    {
        var registry = new EntityRegistry();

        var vehicle = registry.Create(new VehicleProperties());
        var ped = registry.Create(new PedProperties());

        Assert.Equal(1, vehicle.Id);
        Assert.Equal(1, ped.Id);
    }

    [Fact]
    public void Update_AppliesFieldsOntoStoredEntity()
    {
        var registry = new EntityRegistry();
        var handle = registry.Create(new VehicleProperties { Model = "adder", Health = 1000 });

        var applied = registry.Update(new VehicleProperties
        {
            Handle = handle,
            Model = "sultan",
            Health = 750
        });

        Assert.True(applied);

        var stored = registry.Get<VehicleProperties>(handle);
        Assert.Equal("sultan", stored!.Model);
        Assert.Equal(750, stored.Health);
    }

    [Fact]
    public void Update_UnknownHandleIsRejected()
    {
        var registry = new EntityRegistry();

        var applied = registry.Update(new VehicleProperties { Handle = NetHandle.Create(999, EntityType.Vehicle) });

        Assert.False(applied);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void Delete_RemovesEntityAndFreesNothing()
    {
        var registry = new EntityRegistry();
        var handle = registry.Create(new VehicleProperties());

        Assert.True(registry.Delete(handle));
        Assert.Null(registry.Get(handle));

        // Ids must not be rewound, otherwise a late packet from the deleted entity
        // could land on the one that reuses its id.
        Assert.Equal(2, registry.Create(new VehicleProperties()).Id);
    }

    [Fact]
    public void OfType_OnlyReturnsRequestedType()
    {
        var registry = new EntityRegistry();
        registry.Create(new VehicleProperties());
        registry.Create(new PedProperties());
        registry.Create(new PlayerProperties());

        Assert.Single(registry.OfType<VehicleProperties>());
        Assert.Single(registry.OfType<PedProperties>());
        Assert.Single(registry.OfType<PlayerProperties>());
    }
}