using Ubiquitous.Common;
using Ubiquitous.World;

namespace Ubiquitous.Game.Tests;

public sealed class VisibleCreatureSetTests
{
    private static WorldEntitySnapshot Creature(uint id, uint spawnId, float x = 0) =>
        new(EntityId.From(ObjectType.Creature, id), 9001, new Position(x, 0, 0), 0, spawnId);

    [Fact]
    public void ForgettingACreatureDropsEveryWayOfFindingIt()
    {
        var sight = new VisibleCreatureSet();
        sight.Add(Creature(1, 7));

        sight.Remove("Creature:1");

        Assert.False(sight.Contains("Creature:1"));
        Assert.Empty(sight.SpawnIds);
        Assert.Empty(sight.WireIdsBySpawnId);
        Assert.Empty(sight.Targets);
        Assert.Empty(sight.Ids);
    }

    [Fact]
    public void ForgettingAnOldBodyKeepsTheSpawnsNewOne()
    {
        var sight = new VisibleCreatureSet();
        sight.Add(Creature(1, 7));
        sight.Add(Creature(2, 7));

        sight.Remove("Creature:1");

        Assert.Equal("Creature:2", sight.WireIdsBySpawnId[7]);
    }

    [Fact]
    public void AWalkOverTheIdsIsUndisturbedByForgettingAndSeesTheChangeNextTime()
    {
        var sight = new VisibleCreatureSet();
        sight.Add(Creature(1, 7));
        sight.Add(Creature(2, 8));

        var walked = new List<string>();
        foreach (var id in sight.Ids)
        {
            walked.Add(id);
            sight.Remove(id);
        }

        Assert.Equal(["Creature:1", "Creature:2"], walked);
        Assert.Empty(sight.Ids);
    }

    [Fact]
    public void MovingKeepsTheTargetingPositionCurrentButAddsNothing()
    {
        var sight = new VisibleCreatureSet();
        sight.Add(Creature(1, 7));

        sight.Move("Creature:1", new Position(5, 0, 0));
        sight.Move("Creature:9", new Position(5, 0, 0));

        Assert.Equal(5, Assert.Single(sight.Targets).X);
    }
}
