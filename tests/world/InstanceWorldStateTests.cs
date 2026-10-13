using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.World.Tests;

public sealed class InstanceWorldStateTests
{
    private static readonly CreatureSpawnDefinition[] CreatureSpawns =
    [
        new(1, 13, 9001, 0, 0, 0, 0),
        new(2, 13, 9002, 500, 500, 0, 0),
        new(3, 14, 9003, 0, 0, 0, 0),
    ];

    private static readonly StaticObjectSpawnDefinition[] StaticSpawns =
    [
        new(1, 13, 8001, 0, 0, 0, 0),
        new(2, 14, 8002, 0, 0, 0, 0),
    ];

    [Fact]
    public void RespawnCycle_KillScheduleDueConfirm_ReusesTheSameWireId()
    {
        var instance = new InstanceWorldState(13);
        var now = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        var population = instance.EnsurePopulated(CreatureSpawns, StaticSpawns);
        var first = population.Creatures.Single(c => c.SpawnId == 1);

        Assert.Empty(instance.DueRespawns(now + TimeSpan.FromSeconds(30)));
        instance.Kill(1, now);
        instance.ScheduleRespawn(1, TimeSpan.FromSeconds(5), now);
        Assert.Empty(instance.DueRespawns(now + TimeSpan.FromSeconds(4)));
        var due = instance.DueRespawns(now + TimeSpan.FromSeconds(6));
        Assert.Equal([1u], due);
        Assert.True(instance.TryGetHomeSnapshot(1, out var snapshot));
        Assert.Equal(first.Id, snapshot.Id);
        Assert.Equal(new Position(0, 0, 0), snapshot.Position);
        instance.ConfirmRespawned(1);
        Assert.Empty(instance.DueRespawns(now + TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void EnsurePopulated_SpawnsEveryCandidateForItsOwnMapOnly()
    {
        var instance = new InstanceWorldState(13);

        var population = instance.EnsurePopulated(CreatureSpawns, StaticSpawns);

        Assert.Equal(2, population.Creatures.Count);
        Assert.Contains(population.Creatures, c => c.Entry == 9001);
        Assert.Contains(population.Creatures, c => c.Entry == 9002);
        Assert.DoesNotContain(population.Creatures, c => c.Entry == 9003);
        var staticObject = Assert.Single(population.StaticObjects);
        Assert.Equal(8001u, staticObject.Entry);
    }

    [Fact]
    public void EnsurePopulated_SpawnsEvenAFarAwaySpawn_UnlikeTheOpenWorldDistanceScan()
    {
        var instance = new InstanceWorldState(13);
        var population = instance.EnsurePopulated(CreatureSpawns, StaticSpawns);

        var farAway = population.Creatures.Single(c => c.Entry == 9002);
        Assert.Equal(500f, farAway.Position.X);
    }

    [Fact]
    public void EnsurePopulated_CalledAgain_ReturnsTheSameEntityIds()
    {
        var instance = new InstanceWorldState(13);
        var first = instance.EnsurePopulated(CreatureSpawns, StaticSpawns);
        var second = instance.EnsurePopulated(CreatureSpawns, StaticSpawns);

        Assert.Equal(
            first.Creatures.Select(c => c.Id).OrderBy(id => id.ToString()),
            second.Creatures.Select(c => c.Id).OrderBy(id => id.ToString()));
    }

    [Fact]
    public void EnsurePopulated_CoversBothCreaturesAndStaticObjects()
    {
        var population = new InstanceWorldState(13).EnsurePopulated(CreatureSpawns, StaticSpawns);

        Assert.Equal(3, population.Creatures.Count + population.StaticObjects.Count);
    }
}
