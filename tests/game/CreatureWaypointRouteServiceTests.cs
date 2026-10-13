using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class CreatureWaypointRouteServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Position Start = new(0, 5, 0);

    private static WaypointRouteDefinition Loop() => new(77, 13,
    [
        new(12, 5, 0, 2000),
        new(24, 5, 0, 2000),
        new(36, 5, 0, 2000),
    ]);

    private static TimeSpan Leg(double distance) => TimeSpan.FromSeconds(distance / CreatureWaypointRouteService.Speed);

    [Fact]
    public void Advance_ProducesNothingTheFirstTimeATargetIsSeen()
    {
        var service = new CreatureWaypointRouteService();

        Assert.Null(service.Advance("Creature:1", Start, Loop(), Now));
    }

    [Fact]
    public void Advance_StartsTheFirstLegImmediatelyWithNoPause()
    {
        var service = new CreatureWaypointRouteService();
        var route = Loop();
        service.Advance("Creature:1", Start, route, Now);

        var start = service.Advance("Creature:1", Start, route, Now);

        Assert.NotNull(start);
        Assert.True(start!.IsTransition);
        Assert.True(start.Moving);
        Assert.Equal(12f, start.Target!.Value.X, precision: 3);
    }

    [Fact]
    public void Advance_WalksTheStopsInOrderRatherThanBackAndForth()
    {
        var service = new CreatureWaypointRouteService();
        var route = Loop();

        var targets = Targets(service, route, stops: 3);

        Assert.Equal([12f, 24f, 36f], targets);
    }

    [Fact]
    public void Advance_ReportsAnArrivalStandingExactlyOnTheStop()
    {
        var service = new CreatureWaypointRouteService();
        var route = Loop();
        service.Advance("Creature:1", Start, route, Now);
        service.Advance("Creature:1", Start, route, Now);

        var arrival = service.Advance("Creature:1", Start, route, Now + Leg(12))!;

        Assert.True(arrival.IsTransition);
        Assert.False(arrival.Moving);
        Assert.Null(arrival.Target);
        Assert.Equal(12f, arrival.Position.X, precision: 3);
    }

    [Fact]
    public void Advance_StandsForTheStopsOwnPauseBeforeLeavingIt()
    {
        var service = new CreatureWaypointRouteService();
        var route = Loop();
        service.Advance("Creature:1", Start, route, Now);
        service.Advance("Creature:1", Start, route, Now);
        var arrivedAt = Now + Leg(12);
        var arrival = service.Advance("Creature:1", Start, route, arrivedAt)!;

        var stillStanding = service.Advance("Creature:1", arrival.Position, route,
            arrivedAt + TimeSpan.FromMilliseconds(1999));
        var leaving = service.Advance("Creature:1", arrival.Position, route,
            arrivedAt + TimeSpan.FromMilliseconds(2000));

        Assert.Null(stillStanding);
        Assert.NotNull(leaving);
        Assert.Equal(24f, leaving!.Target!.Value.X, precision: 3);
    }

    [Fact]
    public void Advance_UsesEachStopsOwnPauseRatherThanOneRoutewideValue()
    {
        var route = new WaypointRouteDefinition(77, 13,
        [
            new(12, 5, 0, 500),
            new(24, 5, 0, 9000),
        ]);
        var service = new CreatureWaypointRouteService();
        service.Advance("Creature:1", Start, route, Now);
        service.Advance("Creature:1", Start, route, Now);
        var firstArrival = Now + Leg(12);
        service.Advance("Creature:1", Start, route, firstArrival);

        var afterShortPause = service.Advance("Creature:1", new Position(12, 5, 0), route,
            firstArrival + TimeSpan.FromMilliseconds(500));
        var secondArrival = firstArrival + TimeSpan.FromMilliseconds(500) + Leg(12);
        service.Advance("Creature:1", new Position(12, 5, 0), route, secondArrival);
        var duringLongPause = service.Advance("Creature:1", new Position(24, 5, 0), route,
            secondArrival + TimeSpan.FromMilliseconds(8999));

        Assert.NotNull(afterShortPause);
        Assert.Null(duringLongPause);
    }

    [Fact]
    public void Advance_LoopsBackToTheFirstStopAfterTheLast()
    {
        var service = new CreatureWaypointRouteService();
        var route = Loop();

        var targets = Targets(service, route, stops: 5);

        Assert.Equal([12f, 24f, 36f, 12f, 24f], targets);
    }

    [Fact]
    public void Advance_LoopsBackToTheNamedStopWhenTheRouteRejoinsItsMiddle()
    {
        var service = new CreatureWaypointRouteService();
        var route = Loop() with { LoopToStopIndex = 1 };

        var targets = Targets(service, route, stops: 5);

        Assert.Equal([12f, 24f, 36f, 24f, 36f], targets);
    }

    [Fact]
    public void Advance_StopsForGoodAtTheLastStopOfARouteThatDoesNotLoop()
    {
        var service = new CreatureWaypointRouteService();
        var route = Loop() with { LoopToStopIndex = null };

        var targets = Targets(service, route, stops: 5);

        Assert.Equal([12f, 24f, 36f], targets);
    }

    [Fact]
    public void Advance_ReportsAnInTransitPositionBetweenTheTwoTransitionTicks()
    {
        var service = new CreatureWaypointRouteService();
        var route = Loop();
        service.Advance("Creature:1", Start, route, Now);
        service.Advance("Creature:1", Start, route, Now);

        var midLeg = service.Advance("Creature:1", Start, route, Now + (Leg(12) / 2))!;

        Assert.False(midLeg.IsTransition);
        Assert.True(midLeg.Moving);
        Assert.Equal(6f, midLeg.Position.X, precision: 1);
    }

    [Fact]
    public void Advance_ProducesNothingForARouteWithNoStops()
    {
        var service = new CreatureWaypointRouteService();
        var empty = new WaypointRouteDefinition(77, 13, []);

        Assert.Null(service.Advance("Creature:1", Start, empty, Now));
        Assert.Null(service.Advance("Creature:1", Start, empty, Now));
    }

    [Fact]
    public void Clear_ForgetsRouteStateSoTheNextCallStartsFresh()
    {
        var service = new CreatureWaypointRouteService();
        var route = Loop();
        service.Advance("Creature:1", Start, route, Now);
        service.Advance("Creature:1", Start, route, Now);

        service.Clear("Creature:1");
        var afterClear = service.Advance("Creature:1", Start, route, Now);

        Assert.Null(afterClear);
    }

    [Fact]
    public void Advance_SamplesGroundHeightWhileMovingBetweenStops()
    {
        var service = new CreatureWaypointRouteService((x, _, nearY) => new GroundAnswer(x * 0.5f, true));
        var route = new WaypointRouteDefinition(77, 13,
        [
            new(10, 0, 0, 0),
            new(20, 0, 0, 0),
        ]);
        service.Advance("Creature:1", new Position(0, 0, 0), route, Now);
        service.Advance("Creature:1", new Position(0, 0, 0), route, Now);

        var midLeg = service.Advance("Creature:1", new Position(0, 0, 0), route, Now + Leg(10) / 2)!;

        Assert.Equal(5f, midLeg.Position.X, precision: 1);
        Assert.Equal(2.5f, midLeg.Position.Y, precision: 1);
    }

    [Fact]
    public void PositionAt_SamplesGroundHeightWhenResumedMidLeg()
    {
        var service = new CreatureWaypointRouteService((x, _, nearY) => new GroundAnswer(x, true));
        var route = new WaypointRouteDefinition(77, 13,
        [
            new(10, 5, 0, 0),
        ]);
        service.Advance("Creature:1", new Position(0, 0, 0), route, Now);
        service.Advance("Creature:1", new Position(0, 0, 0), route, Now);

        var midLeg = service.PositionAt("Creature:1", route, Now + Leg(10) / 2)!;

        Assert.Equal(5f, midLeg.Value.X, precision: 1);
        Assert.Equal(5f, midLeg.Value.Y, precision: 1);
    }

    [Fact]
    public void Advance_GroundsTheArrivalPositionSoPausesStandOnTerrain()
    {
        var service = new CreatureWaypointRouteService((_, _, _) => new GroundAnswer(8f, true));
        var route = new WaypointRouteDefinition(77, 13,
        [
            new(10, 0, 0, 500),
        ]);
        service.Advance("Creature:1", new Position(0, 0, 0), route, Now);
        service.Advance("Creature:1", new Position(0, 0, 0), route, Now);

        var arrival = service.Advance("Creature:1", new Position(0, 0, 0), route, Now + Leg(10))!;

        Assert.True(arrival.IsTransition);
        Assert.False(arrival.Moving);
        Assert.Equal(10f, arrival.Position.X, precision: 1);
        Assert.Equal(8f, arrival.Position.Y, precision: 1);

        var stillStanding = service.Advance("Creature:1", arrival.Position, route,
            Now + Leg(10) + TimeSpan.FromMilliseconds(250));
        Assert.Null(stillStanding);
    }

    [Fact]
    public void Advance_HoldsTerrainOverAStaleLowStop()
    {
        var service = new CreatureWaypointRouteService((_, _, nearY) =>
            nearY is >= 7.5f ? new GroundAnswer(8f, true) : null);
        var route = new WaypointRouteDefinition(77, 13,
        [
            new(10, 5, 0, 0),
            new(20, 5, 0, 0),
        ]);
        var position = new Position(0, 8, 0);
        service.Advance("Creature:1", position, route, Now);
        service.Advance("Creature:1", position, route, Now);

        var midLeg = service.Advance("Creature:1", position, route, Now + Leg(10) / 2)!;
        Assert.Equal(8f, midLeg.Position.Y, precision: 1);
        position = midLeg.Position;

        var arrival = service.Advance("Creature:1", position, route, Now + Leg(10))!;
        Assert.True(arrival.IsTransition);
        Assert.False(arrival.Moving);
        Assert.Equal(10f, arrival.Position.X, precision: 1);
        Assert.Equal(8f, arrival.Position.Y, precision: 1);

        var leaving = service.Advance("Creature:1", arrival.Position, route, Now + Leg(10))!;
        Assert.True(leaving.Moving);
        Assert.Equal(8f, leaving.Position.Y, precision: 1);
    }

    private static List<float> Targets(CreatureWaypointRouteService service, WaypointRouteDefinition route, int stops)
    {
        var targets = new List<float>();
        var at = Now;
        var position = Start;
        service.Advance("Creature:1", position, route, at);
        for (var i = 0; i < stops; i++)
        {
            var start = service.Advance("Creature:1", position, route, at);
            if (start is null) break;
            var destination = start.Target!.Value;
            targets.Add(destination.X);

            var distance = Math.Sqrt(
                ((destination.X - position.X) * (destination.X - position.X))
                + ((destination.Z - position.Z) * (destination.Z - position.Z)));
            at += Leg(distance);
            var arrival = service.Advance("Creature:1", position, route, at)!;
            position = arrival.Position;

            at += TimeSpan.FromMilliseconds(2000);
        }

        return targets;
    }
}
