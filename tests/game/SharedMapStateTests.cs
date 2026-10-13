using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class SharedMapStateTests
{
    private static CreatureSpawnDefinition Spawn(uint id, float x = 2, float z = 4) =>
        new(id, 13, 9001, x, 2, z, 0, IdleBehaviour: 1, WanderRadius: 10);

    [Fact]
    public void ScansMintStableIdsInSpawnOrder()
    {
        var state = new SharedMapState(13);
        var spawns = new[] { Spawn(7), Spawn(3) };

        var first = state.VisibleCreatures(new Position(0, 2, 0), 58f, spawns);
        var second = state.VisibleCreatures(new Position(0, 2, 0), 58f, spawns);

        Assert.Equal(["Creature:1", "Creature:2"],
            first.Select(e => e.Id.ToString()));
        Assert.Equal(first.Select(e => e.Id.ToString()), second.Select(e => e.Id.ToString()));
        Assert.Equal([3u, 7u], first.Select(e => e.SpawnId));
    }

    [Fact]
    public void AScanReadsTheSpawnListItIsGivenNotTheOneBefore()
    {
        var state = new SharedMapState(13);
        var player = new Position(0, 2, 0);
        var offMap = new CreatureSpawnDefinition(9, 14, 9001, 2, 2, 4, 0);

        Assert.Equal([3u], state.VisibleCreatures(player, 58f, [Spawn(3), offMap]).Select(e => e.SpawnId));
        Assert.Equal([5u, 7u], state.VisibleCreatures(player, 58f, [Spawn(7), Spawn(5)]).Select(e => e.SpawnId));
    }

    [Fact]
    public void DeadSpawnsStayHiddenUntilTheirRespawnCollects()
    {
        var state = new SharedMapState(13);
        var spawns = new[] { Spawn(7) };
        var player = new Position(0, 2, 0);
        var now = DateTimeOffset.UtcNow;

        state.VisibleCreatures(player, 58f, spawns);
        state.Kill(7, now);
        state.ScheduleRespawn(7, TimeSpan.FromSeconds(30), now);

        Assert.Empty(state.VisibleCreatures(player, 58f, spawns));
        Assert.False(state.IsAlive("Creature:1"));

        Assert.Empty(state.CollectDueRespawns(now + TimeSpan.FromSeconds(29)));
        Assert.Empty(state.VisibleCreatures(player, 58f, spawns));

        Assert.Equal([7u], state.CollectDueRespawns(now + TimeSpan.FromSeconds(31)));
        var respawned = Assert.Single(state.VisibleCreatures(player, 58f, spawns));
        Assert.Equal("Creature:2", respawned.Id.ToString());
        Assert.True(state.IsAlive("Creature:2"));
    }

    [Fact]
    public void WanderDestinationIsPickedOnceForEveryObserver()
    {
        var state = new SharedMapState(13, rollUnit: () => 0.9);
        var spawns = new[] { Spawn(7) };
        var home = new Position(2, 2, 4);
        var now = DateTimeOffset.UtcNow;
        state.VisibleCreatures(new Position(0, 2, 0), 58f, spawns);

        Assert.Null(state.AdvanceWander("Creature:1", home, home, 10, now));
        var pause = now + CreatureWanderService.PauseDuration;
        var first = state.AdvanceWander("Creature:1", home, home, 10, pause);
        var second = state.AdvanceWander("Creature:1", home, home, 10, pause);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Target, second!.Target);
    }

    // A wanderer is drawn moving between updates; a fight or a death must find it where it is drawn.
    [Fact]
    public void AWanderersPositionNowIsPartwayAlongItsLegAndASettleStopsItThere()
    {
        var state = new SharedMapState(13, rollUnit: () => 0.9);
        var home = new Position(2, 2, 4);
        var now = DateTimeOffset.UtcNow;
        state.VisibleCreatures(new Position(0, 2, 0), 58f, new[] { Spawn(7) });
        state.AdvanceWander("Creature:1", home, home, 10, now);
        var start = now + CreatureWanderService.PauseDuration;
        var leg = state.AdvanceWander("Creature:1", home, home, 10, start)!;
        state.SetPosition("Creature:1", leg.Position);
        var target = leg.Target!.Value;
        var legSeconds = Math.Max(1.0, Math.Sqrt(Math.Pow(target.X - home.X, 2) + Math.Pow(target.Z - home.Z, 2)) / CreatureWanderService.Speed);
        var halfway = start + TimeSpan.FromSeconds(legSeconds / 2);

        var midway = state.PositionNow("Creature:1", halfway)!.Value;
        var settled = state.Settle("Creature:1", halfway)!.Value;
        var later = state.PositionNow("Creature:1", halfway + TimeSpan.FromSeconds(30))!.Value;

        Assert.Equal((home.X + target.X) / 2, midway.X, 2);
        Assert.Equal((home.Z + target.Z) / 2, midway.Z, 2);
        Assert.Equal(midway.X, settled.X, 3);
        Assert.Equal(settled.X, later.X, 3);
        Assert.Equal(settled.Z, later.Z, 3);
    }

    [Fact]
    public void RespawnScheduleKeepsTheFirstWriterWhenAbsent()
    {
        var state = new SharedMapState(13);
        var now = DateTimeOffset.UtcNow;
        state.Kill(7, now);

        state.ScheduleRespawnIfAbsent(7, TimeSpan.FromSeconds(30), TimeSpan.Zero, now);
        state.ScheduleRespawnIfAbsent(7, TimeSpan.FromSeconds(90), TimeSpan.Zero, now);
        Assert.Equal([7u], state.CollectDueRespawns(now + TimeSpan.FromSeconds(31)));

        state.Kill(7, now);
        state.ScheduleRespawnIfAbsent(7, TimeSpan.FromSeconds(30), TimeSpan.Zero, now);
        state.ScheduleRespawn(7, TimeSpan.FromSeconds(90), now);
        Assert.Equal([7u], state.CollectDueRespawns(now + TimeSpan.FromSeconds(31)));
    }

    [Fact]
    public void ReturnLegRunsHomeAtRunPaceThenArrives()
    {
        var state = new SharedMapState(13);
        var home = new Position(0, 2, 0);
        var now = DateTimeOffset.UtcNow;
        state.Adopt("Creature:1", 1, new Position(30, 2, 0));
        state.MarkReturning("Creature:1", home);

        state.TickWorld(now);
        var start = Assert.Single(state.MovementsSince(0, _ => true, out var cursor));
        Assert.True(start.Moving);
        Assert.Equal(home, start.Target);

        state.TickWorld(now + TimeSpan.FromSeconds(2));
        Assert.Empty(state.MovementsSince(cursor, _ => true, out _));
        Assert.True(state.IsReturning("Creature:1"));

        state.TickWorld(now + TimeSpan.FromSeconds(6));
        var arrival = Assert.Single(state.MovementsSince(cursor, _ => true, out _));
        Assert.False(arrival.Moving);
        Assert.False(state.IsReturning("Creature:1"));
    }

    [Fact]
    public void ReturnLegSkipsBodiesAlreadyHome()
    {
        var state = new SharedMapState(13);
        var home = new Position(0, 2, 0);
        state.Adopt("Creature:1", 1, new Position(0.2f, 2, 0));
        state.MarkReturning("Creature:1", home);

        state.TickWorld(DateTimeOffset.UtcNow);

        Assert.Empty(state.MovementsSince(0, _ => true, out _));
        Assert.False(state.IsReturning("Creature:1"));
    }

    [Fact]
    public void AReaderGetsOnlyTheMovesItWantsButItsCursorPassesThemAll()
    {
        var state = new SharedMapState(13);
        var home = new Position(0, 2, 0);
        state.Adopt("Creature:1", 1, new Position(30, 2, 0));
        state.Adopt("Creature:2", 2, new Position(-30, 2, 0));
        state.MarkReturning("Creature:1", home);
        state.MarkReturning("Creature:2", home);

        state.TickWorld(DateTimeOffset.UtcNow);
        var seen = state.MovementsSince(0, id => id == "Creature:2", out var cursor);

        Assert.Equal(["Creature:2"], seen.Select(m => m.WireId));
        Assert.Equal(2, cursor);
        Assert.Empty(state.MovementsSince(cursor, _ => true, out var unchanged));
        Assert.Equal(cursor, unchanged);
    }

    [Fact]
    public void EngagementIsSharedAndClearedByDeath()
    {
        var state = new SharedMapState(13);
        state.VisibleCreatures(new Position(0, 2, 0), 58f, new[] { Spawn(7) });

        state.MarkEngaged("Creature:1");
        Assert.True(state.IsEngaged("Creature:1"));

        state.MarkDisengaged("Creature:1");
        Assert.False(state.IsEngaged("Creature:1"));

        state.MarkEngaged("Creature:1");
        state.Kill(7, DateTimeOffset.UtcNow);
        Assert.False(state.IsEngaged("Creature:1"));
    }

    [Fact]
    public void DestinationOf_IsNothingForABodyStandingStill()
    {
        var state = new SharedMapState(13);
        state.Adopt("Creature:1", 1, new Position(0, 0, 0));

        Assert.Null(state.DestinationOf("Creature:1"));
        Assert.Null(state.DestinationOf("Creature:nobody"));
    }

    [Fact]
    public void DestinationOf_AWalkHomeAnswersOnceTheLegHasActuallyStarted()
    {
        var state = new SharedMapState(13);
        state.Adopt("Creature:2", 2, new Position(50, 0, 0));
        state.MarkReturning("Creature:2", new Position(0, 0, 0));

        Assert.Null(state.DestinationOf("Creature:2"));

        state.TickWorld(DateTimeOffset.UnixEpoch);

        Assert.Equal(new Position(0, 0, 0), state.DestinationOf("Creature:2"));
    }

    [Fact]
    public void TickWorld_WalksABodyHomeWithNobodyOnTheMap()
    {
        var state = new SharedMapState(13);
        var start = DateTimeOffset.UnixEpoch;
        state.Adopt("Creature:1", 1, new Position(60, 0, 0));
        state.MarkReturning("Creature:1", new Position(0, 0, 0));

        for (var i = 1; i <= 40; i++) state.TickWorld(start + TimeSpan.FromSeconds(i));

        Assert.True(state.TryGetPosition("Creature:1", out var where));
        Assert.Equal(new Position(0, 0, 0), where);
        Assert.False(state.IsReturning("Creature:1"));
    }

    [Fact]
    public void TickWorld_MovesABodyPartWayAndKeepsItWalking()
    {
        var state = new SharedMapState(13);
        var start = DateTimeOffset.UnixEpoch;
        state.Adopt("Creature:2", 2, new Position(1000, 0, 0));
        state.MarkReturning("Creature:2", new Position(0, 0, 0));

        state.TickWorld(start + TimeSpan.FromSeconds(1));
        state.TickWorld(start + TimeSpan.FromSeconds(5));

        Assert.True(state.TryGetPosition("Creature:2", out var where));
        Assert.True(where.X < 1000, "it should have made some ground");
        Assert.True(where.X > 0, "but nowhere near all of it");
        Assert.True(state.IsReturning("Creature:2"));
        Assert.Equal(new Position(0, 0, 0), state.DestinationOf("Creature:2"));
    }

    [Fact]
    public void TickWorld_LeavesABodyThatIsNotGoingAnywhereAlone()
    {
        var state = new SharedMapState(13);
        state.Adopt("Creature:3", 3, new Position(5, 0, 5));

        state.TickWorld(DateTimeOffset.UnixEpoch + TimeSpan.FromHours(1));

        Assert.True(state.TryGetPosition("Creature:3", out var where));
        Assert.Equal(new Position(5, 0, 5), where);
    }

    [Fact]
    public void AHuntIsClaimedOnceForItsHunterAndOnceForItsPrey()
    {
        var state = new SharedMapState(13);

        Assert.True(state.TryClaimHunt("Creature:1", "Creature:2"));
        Assert.False(state.TryClaimHunt("Creature:1", "Creature:3"));
        Assert.False(state.TryClaimHunt("Creature:4", "Creature:2"));
        Assert.False(state.TryClaimHunt("Creature:2", "Creature:5"));
        Assert.False(state.TryClaimHunt("Creature:6", "Creature:1"));
        Assert.True(state.IsInHunt("Creature:1"));
        Assert.True(state.IsInHunt("Creature:2"));
        Assert.False(state.IsInHunt("Creature:3"));
    }

    [Fact]
    public void ReleasingAHuntFreesBothBodies()
    {
        var state = new SharedMapState(13);
        state.TryClaimHunt("Creature:1", "Creature:2");

        state.ReleaseHunt("Creature:1");

        Assert.False(state.IsInHunt("Creature:1"));
        Assert.False(state.IsInHunt("Creature:2"));
        Assert.True(state.TryClaimHunt("Creature:3", "Creature:2"));
    }

    [Fact]
    public void ADeathEndsEveryHuntTheBodyWasIn()
    {
        var state = new SharedMapState(13);
        var prey = Assert.Single(state.VisibleCreatures(new Position(0, 2, 0), 58f, [Spawn(7)]));
        state.TryClaimHunt("Creature:9", prey.Id.ToString());

        state.Kill(7, DateTimeOffset.UnixEpoch);

        Assert.False(state.IsInHunt("Creature:9"));
    }

    [Fact]
    public void AFighterIsForgottenOnceTheFakeClockPassesTheExpiry()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var state = new SharedMapState(13, time: clock);
        state.AddFighter("Creature:1", "player:a");

        Assert.Equal(["player:a"], state.Fighters("Creature:1"));

        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.Empty(state.Fighters("Creature:1"));
    }
}
