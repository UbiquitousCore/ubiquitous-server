using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public class CreatureFollowServiceTests
{
    private static readonly DateTimeOffset Now = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly CreatureSpawnDefinition Leader =
        new(100, 13, 9001, 0, 0, 0, 0, PatrolRouteId: 55);

    private static readonly CreatureSpawnDefinition Follower =
        new(101, 13, 9002, 2, 0, 0, 0, LeaderSpawnId: 100);

    [Fact]
    public void Advance_PlacesAFollowerIntoItsSpawnRowsOwnOffsetImmediately()
    {
        var follow = new CreatureFollowService();
        var leaderAt = new Position(10, 5, 10);

        var update = follow.Advance("creature:1", new Position(0, 0, 0), leaderAt, null, Follower, Leader, Now);

        Assert.NotNull(update);
        Assert.False(update!.Moving);
        Assert.Equal(12f, update.Position.X, precision: 3);
        Assert.Equal(10f, update.Position.Z, precision: 3);
    }

    [Fact]
    public void Advance_AimsAtTheLeadersDestinationPlusTheOffsetRatherThanAtTheLeader()
    {
        var follow = new CreatureFollowService();
        var leaderAt = new Position(0, 0, 0);
        follow.Advance("creature:1", new Position(2, 0, 0), leaderAt, null, Follower, Leader, Now);

        var update = follow.Advance(
            "creature:1", new Position(2, 0, 0), leaderAt, new Position(0, 0, -20),
            Follower, Leader, Now + TimeSpan.FromSeconds(0.5));

        Assert.NotNull(update);
        Assert.True(update!.Moving);
        Assert.Equal(2f, update.Target!.Value.X, precision: 3);
        Assert.Equal(-20f, update.Target!.Value.Z, precision: 3);
    }

    [Fact]
    public void Advance_ClosesGroundOnAMovingLeadersStationEveryTick()
    {
        var follow = new CreatureFollowService();
        var leaderAt = new Position(0, 0, 0);
        follow.Advance("creature:1", new Position(2, 0, 0), leaderAt, null, Follower, Leader, Now);

        var station = new Position(2, 0, -40);
        var at = new Position(2, 0, 0);
        var previousDistance = double.MaxValue;
        for (int tick = 1; tick <= 5; tick++)
        {
            var leaderNow = new Position(0, 0, tick * -2f);
            var update = follow.Advance(
                "creature:1", at, leaderNow, new Position(0, 0, -40),
                Follower, Leader, Now + TimeSpan.FromSeconds(0.5 * tick));
            Assert.NotNull(update);
            Assert.True(update!.Moving);
            at = update.Position;
            var distance = Math.Sqrt(at.DistanceSquaredTo(station));
            Assert.True(distance < previousDistance, $"tick {tick} did not close on the station");
            previousDistance = distance;
        }

        Assert.True(at.Z < 0, "the follower never left its spawn point");
    }

    [Fact]
    public void Advance_RotatesTheOffsetWithTheLeadersHeading()
    {
        var follow = new CreatureFollowService();
        var leaderAt = new Position(0, 0, 0);
        follow.Advance("creature:1", new Position(2, 0, 0), leaderAt, null, Follower, Leader, Now);

        var update = follow.Advance(
            "creature:1", new Position(2, 0, 0), leaderAt, new Position(20, 0, 0),
            Follower, Leader, Now + TimeSpan.FromSeconds(0.5));

        Assert.NotNull(update);
        Assert.Equal(20f, update!.Target!.Value.X, precision: 3);
        Assert.Equal(2f, update.Target!.Value.Z, precision: 3);
    }

    [Fact]
    public void Advance_IgnoresATurnSmallerThanTheThreshold()
    {
        var follow = new CreatureFollowService();
        var leaderAt = new Position(0, 0, 0);
        follow.Advance("creature:1", new Position(2, 0, 0), leaderAt, null, Follower, Leader, Now);
        var slightly = CreatureFollowService.TurnThreshold / 2;
        var ahead = new Position((float)(Math.Sin(slightly) * 20), 0, (float)(-Math.Cos(slightly) * 20));

        var update = follow.Advance(
            "creature:1", new Position(2, 0, 0), leaderAt, ahead,
            Follower, Leader, Now + TimeSpan.FromSeconds(0.5));

        Assert.Equal(ahead.X + 2f, update!.Target!.Value.X, precision: 3);
        Assert.Equal(ahead.Z, update.Target!.Value.Z, precision: 3);
    }

    [Fact]
    public void Advance_KeepsTheLeadersLastHeadingWhileItStandsAtAStop()
    {
        var follow = new CreatureFollowService();
        var leaderAt = new Position(0, 0, 0);
        follow.Advance("creature:1", new Position(2, 0, 0), leaderAt, null, Follower, Leader, Now);
        follow.Advance("creature:1", new Position(2, 0, 0), leaderAt, new Position(20, 0, 0),
            Follower, Leader, Now + TimeSpan.FromSeconds(0.5));

        var standing = new Position(20, 0, 0);
        var update = follow.Advance(
            "creature:1", new Position(5, 0, 2), standing, null,
            Follower, Leader, Now + TimeSpan.FromSeconds(1));

        Assert.NotNull(update);
        Assert.Equal(20f, update!.Target!.Value.X, precision: 3);
        Assert.Equal(2f, update.Target!.Value.Z, precision: 3);
    }

    [Fact]
    public void Advance_SamplesGroundHeightAtTheFollowersOffset()
    {
        var follow = new CreatureFollowService((x, _, _) => new GroundAnswer(x * 0.5f, true));
        var leaderAt = new Position(10, 5, 10);

        var update = follow.Advance("creature:1", new Position(0, 0, 0), leaderAt, null, Follower, Leader, Now);

        Assert.NotNull(update);
        Assert.Equal(12f, update!.Position.X, precision: 3);
        Assert.Equal(6f, update.Position.Y, precision: 3);
    }

    [Fact]
    public void Advance_SamplesGroundHeightWhileMoving()
    {
        var follow = new CreatureFollowService((x, _, _) => new GroundAnswer(x, true));
        var leaderAt = new Position(0, 0, 0);
        follow.Advance("creature:1", new Position(2, 0, 0), leaderAt, null, Follower, Leader, Now);

        var update = follow.Advance(
            "creature:1", new Position(2, 0, 0), leaderAt, new Position(10, 0, 0),
            Follower, Leader, Now + TimeSpan.FromSeconds(0.5));

        Assert.NotNull(update);
        Assert.True(update!.Moving);
        // Ground height sampled at the follower's X should equal its X value
        Assert.Equal(update.Position.X, update.Position.Y, precision: 1);
    }

    [Fact]
    public void Advance_PrefersLiveLeaderHeightOverAStaleDestination()
    {
        var follow = new CreatureFollowService((_, _, nearY) =>
            nearY is >= 7.5f ? new GroundAnswer(8f, true) : null);
        var leaderAt = new Position(0, 8, 0);
        follow.Advance("creature:1", new Position(2, 8, 0), leaderAt, null, Follower, Leader, Now);

        var update = follow.Advance(
            "creature:1", new Position(2, 8, 0), leaderAt, new Position(0, 5, -20),
            Follower, Leader, Now + TimeSpan.FromSeconds(0.5));

        Assert.NotNull(update);
        Assert.True(update!.Moving);
        Assert.Equal(2f, update.Position.X, precision: 1);
        Assert.Equal(8f, update.Position.Y, precision: 1);
        Assert.Equal(8f, update.Target!.Value.Y, precision: 1);
    }
}
