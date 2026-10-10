using System.Numerics;
using GTMP.Server.Core.Configuration;
using GTMP.Shared;
using GTMP.Server.Core.Runtime;
using Xunit;

namespace GTMP.Server.Tests;

public class NetHandleTests
{
    [Fact]
    public void DefaultHandleIsNull()
    {
        Assert.True(default(NetHandle).IsNull);
    }

    [Fact]
    public void Create_RejectsZeroId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NetHandle.Create(0, EntityType.Vehicle));
    }

    [Fact]
    public void EqualityDistinguishesTypeAndPlayerFlag()
    {
        var vehicle = NetHandle.Create(1, EntityType.Vehicle);
        var ped = NetHandle.Create(1, EntityType.Ped);
        var player = NetHandle.CreatePlayer(1);

        Assert.NotEqual(vehicle, ped);
        Assert.NotEqual(player, NetHandle.Create(1, EntityType.Player));
        Assert.Equal(vehicle, NetHandle.Create(1, EntityType.Vehicle));
    }

    [Fact]
    public void HashCodeMatchesForEqualHandles()
    {
        Assert.Equal(
            NetHandle.Create(7, EntityType.Vehicle).GetHashCode(),
            NetHandle.Create(7, EntityType.Vehicle).GetHashCode());
    }
}

public class TickLoopTests
{
    [Fact]
    public void TickIsCalledWithIncreasingNumbers()
    {
        var seen = new List<long>();
        var loop = new TickLoop
        {
            Settings = new ServerSettings { TickRate = 1 },
            ShouldContinue = () => seen.Count < 5
        };

        loop.Register(new DelegateTick(seen.Add));
        loop.Run();

        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, seen);
    }

    [Fact]
    public void AFailingTickDoesNotStopTheOthers()
    {
        var counter = 0;
        var loop = new TickLoop
        {
            Settings = new ServerSettings { TickRate = 1 },
            ShouldContinue = () => counter < 3
        };

        loop.Register(new ThrowingTick());
        loop.Register(new DelegateTick(_ => counter++));

        loop.Run();

        Assert.Equal(3, counter);
    }

    private sealed class DelegateTick(Action<long> action) : IServerTick
    {
        public void Tick(long tickNumber) => action(tickNumber);
    }

    private sealed class ThrowingTick : IServerTick
    {
        public void Tick(long tickNumber) => throw new InvalidOperationException("boom");
    }
}