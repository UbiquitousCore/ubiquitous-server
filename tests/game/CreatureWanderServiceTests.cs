using Ubiquitous.Common;

namespace Ubiquitous.Game.Tests;

public sealed class CreatureWanderServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Position Home = new(0, 5, 0);
    private const double WanderRadius = 10;

    [Fact]
    public void Advance_StandsStillThroughItsOwnFirstPause()
    {
        var rolls = new Queue<double>([0.5]);
        var wander = new CreatureWanderService(rollUnit: () => rolls.Count > 0 ? rolls.Dequeue() : 0.75);
        var firstPause = CreatureWanderService.PauseDuration * 0.5;

        var first = wander.Advance("Creature:1", Home, Home, WanderRadius, Now);
        var stillPaused = wander.Advance("Creature:1", Home, Home, WanderRadius,
            Now + firstPause - TimeSpan.FromMilliseconds(1));
        var setOff = wander.Advance("Creature:1", Home, Home, WanderRadius, Now + firstPause);

        Assert.Null(first);
        Assert.Null(stillPaused);
        Assert.True(setOff!.Moving);
    }

    [Fact]
    public void Advance_TwoCreaturesSeenOnOneTickSetOffApart()
    {
        var rolls = new Queue<double>([0.1, 0.8]);
        var wander = new CreatureWanderService(rollUnit: () => rolls.Count > 0 ? rolls.Dequeue() : 0.75);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now);
        wander.Advance("Creature:2", Home, Home, WanderRadius, Now);

        var early = Now + (CreatureWanderService.PauseDuration * 0.1);

        Assert.True(wander.Advance("Creature:1", Home, Home, WanderRadius, early)!.Moving);
        Assert.Null(wander.Advance("Creature:2", Home, Home, WanderRadius, early));
    }

    [Fact]
    public void Advance_StandsBetweenTheShortestAndLongestPauseBetweenLegs()
    {
        var rolls = new Queue<double>([0.0, 1.0, 1.0, 0.25]);
        var wander = new CreatureWanderService(rollUnit: () => rolls.Count > 0 ? rolls.Dequeue() : 0.75);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now);
        var arrived = Now + TimeSpan.FromSeconds(60);
        var arrival = wander.Advance("Creature:1", Home, Home, WanderRadius, arrived)!;
        var pause = CreatureWanderService.ShortestPause
            + ((CreatureWanderService.PauseDuration - CreatureWanderService.ShortestPause) * 0.25);

        Assert.False(arrival.Moving);
        Assert.Null(wander.Advance("Creature:1", arrival.Position, Home, WanderRadius,
            arrived + pause - TimeSpan.FromMilliseconds(1)));
        Assert.True(wander.Advance("Creature:1", arrival.Position, Home, WanderRadius, arrived + pause)!.Moving);
    }

    [Fact]
    public void Advance_StartsALegOncePauseElapses()
    {
        var wander = new CreatureWanderService(rollUnit: () => 0.75);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now);

        var start = wander.Advance("Creature:1", Home, Home, WanderRadius,
            Now + CreatureWanderService.PauseDuration);

        Assert.NotNull(start);
        Assert.True(start!.IsTransition);
        Assert.True(start.Moving);
        Assert.NotNull(start.Target);
        Assert.Equal(Home, start.Position);
        Assert.Equal(Home.X, start.Target!.Value.X, precision: 3);
        Assert.Equal(Home.Y, start.Target!.Value.Y);
        Assert.Equal(Home.Z - (float)(Math.Sqrt(0.75) * WanderRadius), start.Target!.Value.Z, precision: 3);
    }

    [Fact]
    public void Advance_PicksAnOffsetProportionalToRollUnit()
    {
        var wander = new CreatureWanderService(rollUnit: () => 1.0);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now);

        var start = wander.Advance("Creature:1", Home, Home, WanderRadius, Now + CreatureWanderService.PauseDuration);

        Assert.Equal(Home.X + (float)WanderRadius, start!.Target!.Value.X, precision: 3);
        Assert.Equal(Home.Z, start.Target!.Value.Z, precision: 3);
    }

    [Fact]
    public void Advance_NeverPicksADestinationFartherThanMoveRangeFromHome()
    {
        var rolls = new Queue<double>([1.0, 0.125, 1.0]);
        var wander = new CreatureWanderService(rollUnit: () => rolls.Count > 0 ? rolls.Dequeue() : 0.5);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now);

        var start = wander.Advance("Creature:1", Home, Home, WanderRadius, Now + CreatureWanderService.PauseDuration);

        Assert.NotNull(start!.Target);
        var dx = start.Target!.Value.X - Home.X;
        var dz = start.Target!.Value.Z - Home.Z;
        Assert.Equal(WanderRadius, Math.Sqrt((dx * dx) + (dz * dz)), precision: 3);
    }

    [Fact]
    public void Advance_InterpolatesTowardTheDestinationMidLeg_ThenArrivesAndPauses()
    {
        var wander = new CreatureWanderService(rollUnit: () => 1.0);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now);
        var start = wander.Advance("Creature:1", Home, Home, WanderRadius, Now + CreatureWanderService.PauseDuration)!;
        var destination = start.Target!.Value;
        var legStart = Now + CreatureWanderService.PauseDuration;
        var distance = Math.Sqrt(Home.DistanceSquaredTo(destination));
        var totalSeconds = Math.Max(1.0, distance / CreatureWanderService.Speed);

        var midway = wander.Advance("Creature:1", Home, Home, WanderRadius,
            legStart + TimeSpan.FromSeconds(totalSeconds / 2));

        Assert.NotNull(midway);
        Assert.False(midway!.IsTransition);
        Assert.True(midway.Moving);
        var expectedX = (float)(Home.X + (destination.X - Home.X) * 0.5);
        Assert.Equal(expectedX, midway.Position.X, precision: 2);

        var arrival = wander.Advance("Creature:1", midway.Position, Home, WanderRadius,
            legStart + TimeSpan.FromSeconds(totalSeconds));

        Assert.NotNull(arrival);
        Assert.True(arrival!.IsTransition);
        Assert.False(arrival.Moving);
        Assert.Null(arrival.Target);
        Assert.Equal(destination.X, arrival.Position.X, precision: 3);
        Assert.Equal(destination.Z, arrival.Position.Z, precision: 3);
    }

    [Fact]
    public void Advance_PausesAgainAfterArrivingBeforeStartingTheNextLeg()
    {
        var wander = new CreatureWanderService(rollUnit: () => 1.0);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now);
        var start = wander.Advance("Creature:1", Home, Home, WanderRadius, Now + CreatureWanderService.PauseDuration)!;
        var destination = start.Target!.Value;
        var legStart = Now + CreatureWanderService.PauseDuration;
        var distance = Math.Sqrt(Home.DistanceSquaredTo(destination));
        var arrivesAt = legStart + TimeSpan.FromSeconds(Math.Max(1.0, distance / CreatureWanderService.Speed));
        var arrival = wander.Advance("Creature:1", Home, Home, WanderRadius, arrivesAt)!;

        var immediatelyAfterArrival = wander.Advance(
            "Creature:1", arrival.Position, Home, WanderRadius, arrivesAt + TimeSpan.FromMilliseconds(1));

        Assert.Null(immediatelyAfterArrival);
    }

    [Fact]
    public void Advance_NeverProducesAnUpdateForATargetItHasNotBeenAskedAboutYet()
    {
        var wander = new CreatureWanderService(rollUnit: () => 0.5);

        var result = wander.Advance("Creature:unseen", Home, Home, WanderRadius, Now);

        Assert.Null(result);
    }

    [Fact]
    public void Clear_ForgetsWanderStateSoTheNextCallStartsAFreshPause()
    {
        var wander = new CreatureWanderService(rollUnit: () => 1.0);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now + CreatureWanderService.PauseDuration);

        wander.Clear("Creature:1");
        var afterClear = wander.Advance("Creature:1", Home, Home, WanderRadius, Now + CreatureWanderService.PauseDuration);

        Assert.Null(afterClear);
    }

    [Fact]
    public void Advance_ScalesTheDestinationOffsetWithMoveRange()
    {
        var wander = new CreatureWanderService(rollUnit: () => 1.0);
        wander.Advance("Creature:1", Home, Home, wanderRadius: 3, Now);

        var start = wander.Advance("Creature:1", Home, Home, wanderRadius: 3, Now + CreatureWanderService.PauseDuration);

        Assert.Equal(Home.X + 3f, start!.Target!.Value.X, precision: 3);
    }

    private static GroundAnswer? RisingGround(float x, float z, float? nearY) =>
        new(x, KnowsFloors: false);

    [Fact]
    public void Advance_PutsTheDestinationOnTheGroundRatherThanAtTheSpawnHeight()
    {
        var wander = new CreatureWanderService(rollUnit: () => 1.0, groundHeight: RisingGround);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now);

        var start = wander.Advance("Creature:1", Home, Home, WanderRadius, Now + CreatureWanderService.PauseDuration);

        Assert.Equal(Home.X + (float)WanderRadius, start!.Target!.Value.X, precision: 3);
        Assert.Equal(10f, start.Target!.Value.Y, precision: 3);
    }

    [Fact]
    public void Advance_ClimbsWithTheGroundPartWayThroughALeg()
    {
        var wander = new CreatureWanderService(rollUnit: () => 1.0, groundHeight: RisingGround);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now);
        var start = Now + CreatureWanderService.PauseDuration;
        wander.Advance("Creature:1", Home, Home, WanderRadius, start);

        var midLeg = wander.Advance("Creature:1", Home, Home, WanderRadius, start + TimeSpan.FromMilliseconds(500));

        Assert.NotNull(midLeg);
        Assert.False(midLeg!.IsTransition);
        Assert.True(midLeg.Position.X > Home.X);
        Assert.Equal(midLeg.Position.X, midLeg.Position.Y, precision: 3);
        Assert.NotEqual(Home.Y, midLeg.Position.Y);
    }

    [Fact]
    public void Advance_ArrivesStandingOnTheGroundAtTheDestination()
    {
        var wander = new CreatureWanderService(rollUnit: () => 1.0, groundHeight: RisingGround);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now);
        var start = Now + CreatureWanderService.PauseDuration;
        wander.Advance("Creature:1", Home, Home, WanderRadius, start);

        var arrival = wander.Advance("Creature:1", Home, Home, WanderRadius, start + TimeSpan.FromSeconds(60));

        Assert.NotNull(arrival);
        Assert.True(arrival!.IsTransition);
        Assert.False(arrival.Moving);
        Assert.Equal(10f, arrival.Position.Y, precision: 3);
    }

    [Fact]
    public void Advance_KeepsTheSpawnHeightWhereThereIsNoTerrainToSample()
    {
        var wander = new CreatureWanderService(rollUnit: () => 1.0, groundHeight: (_, _, _) => null);
        wander.Advance("Creature:1", Home, Home, WanderRadius, Now);
        var start = Now + CreatureWanderService.PauseDuration;
        wander.Advance("Creature:1", Home, Home, WanderRadius, start);

        var midLeg = wander.Advance("Creature:1", Home, Home, WanderRadius, start + TimeSpan.FromMilliseconds(500));

        Assert.Equal(Home.Y, midLeg!.Position.Y);
    }

    [Fact]
    public void Advance_TimesALegByItsGroundDistanceNotItsClimb()
    {
        var start = Now + CreatureWanderService.PauseDuration;
        var flat = new CreatureWanderService(rollUnit: () => 1.0, groundHeight: (_, _, _) => new GroundAnswer(5f, KnowsFloors: false));
        var steep = new CreatureWanderService(rollUnit: () => 1.0, groundHeight: RisingGround);
        flat.Advance("Creature:1", Home, Home, WanderRadius, Now);
        steep.Advance("Creature:1", Home, Home, WanderRadius, Now);
        flat.Advance("Creature:1", Home, Home, WanderRadius, start);
        steep.Advance("Creature:1", Home, Home, WanderRadius, start);

        var at = start + TimeSpan.FromSeconds(30);

        Assert.False(flat.Advance("Creature:1", Home, Home, WanderRadius, at)!.Moving);
        Assert.False(steep.Advance("Creature:1", Home, Home, WanderRadius, at)!.Moving);
    }
}
