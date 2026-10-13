using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class PatrolRoutesTests
{
    private static CreatureSpawnDefinition Spawn(uint id, float x = 100, float y = 5, float z = -40) =>
        new(id, 13, 9001, x, y, z, 0, PatrolRouteId: 4000 + id, WanderRadius: 4);

    private static IEnumerable<CreatureSpawnDefinition> ManySpawns() =>
        Enumerable.Range(1, 200).Select(id => Spawn((uint)id));

    [Fact]
    public void Generate_GivesTheSameRouteForTheSameSpawnEveryTime()
    {
        var first = PatrolRoutes.Generate(Spawn(42));
        var second = PatrolRoutes.Generate(Spawn(42));

        Assert.Equal(first.Stops, second.Stops);
        Assert.Equal(first.LoopToStopIndex, second.LoopToStopIndex);
    }

    [Fact]
    public void Generate_GivesDifferentSpawnsDifferentRoutes()
    {
        var distinct = ManySpawns()
            .Select(spawn => string.Join(";", PatrolRoutes.Generate(spawn).Stops.Skip(1).Select(stop => $"{stop.X:F2},{stop.Z:F2}")))
            .Distinct()
            .Count();

        Assert.Equal(200, distinct);
    }

    [Fact]
    public void Generate_StartsAtTheSpawnPointAndLoopsBackToIt()
    {
        foreach (var spawn in ManySpawns())
        {
            var route = PatrolRoutes.Generate(spawn);

            Assert.Equal(spawn.PatrolRouteId, route.Id);
            Assert.Equal(spawn.MapId, route.MapId);
            Assert.Equal(new WaypointRouteStop(spawn.X, spawn.Y, spawn.Z, route.Stops[0].PauseMilliseconds), route.Stops[0]);
            Assert.Equal(0, route.LoopToStopIndex);
            Assert.InRange(route.Stops.Count, 2, PatrolRoutes.MaxOuterStops + 1);
        }
    }

    [Fact]
    public void Generate_KeepsEveryStopWithinTheRadiusButOffTheSpawnPoint()
    {
        foreach (var spawn in ManySpawns())
        {
            foreach (var stop in PatrolRoutes.Generate(spawn).Stops.Skip(1))
            {
                var distance = Math.Sqrt(Math.Pow(stop.X - spawn.X, 2) + Math.Pow(stop.Z - spawn.Z, 2));
                Assert.InRange(distance, PatrolRoutes.MinStopDistance - 0.01, PatrolRoutes.Radius + 0.01);
            }
        }
    }

    [Fact]
    public void Generate_StandsForWholeHalfSecondsBetweenTwoAndSixSeconds()
    {
        foreach (var stop in ManySpawns().SelectMany(spawn => PatrolRoutes.Generate(spawn).Stops))
        {
            Assert.InRange(stop.PauseMilliseconds, 2000, 6000);
            Assert.Equal(0, stop.PauseMilliseconds % 500);
        }
    }

    [Fact]
    public void Generate_OnlyUsesLegsTheGroundAllows_IncludingTheWalkHome()
    {
        static bool SameSideOfWall(Position from, Position to) => from.X >= 100 == to.X >= 100;

        foreach (var spawn in ManySpawns().Select(spawn => spawn with { X = 101 }))
        {
            var route = PatrolRoutes.Generate(spawn, SameSideOfWall);
            var stops = route.Stops.Select(stop => new Position(stop.X, stop.Y, stop.Z)).ToList();

            for (var index = 1; index < stops.Count; index++)
            {
                Assert.True(SameSideOfWall(stops[index - 1], stops[index]));
            }

            Assert.True(SameSideOfWall(stops[^1], stops[0]));
        }
    }

    [Fact]
    public void Generate_StandsASpawnBoxedInOnEverySideAtHome()
    {
        var route = PatrolRoutes.Generate(Spawn(7), canWalkBetween: (_, _) => false);

        var only = Assert.Single(route.Stops);
        Assert.Equal((100f, -40f), (only.X, only.Z));
        Assert.Null(route.LoopToStopIndex);
    }

    [Fact]
    public void Generate_PutsEachStopOnTheGround()
    {
        var route = PatrolRoutes.Generate(
            Spawn(9), groundHeight: (x, z, _) => new GroundAnswer(x / 10f, KnowsFloors: true));

        foreach (var stop in route.Stops.Skip(1))
        {
            Assert.Equal(stop.X / 10f, stop.Y, precision: 4);
        }
    }
}
