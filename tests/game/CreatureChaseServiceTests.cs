using Ubiquitous.Common;

namespace Ubiquitous.Game.Tests;

public sealed class CreatureChaseServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private const double MeleeRange = 3.0;

    [Fact]
    public void Advance_DoesNothingWhenAlreadyWithinMeleeRange()
    {
        var chase = new CreatureChaseService();
        var creature = new Position(0, 0, 0);
        var target = new Position(2, 0, 0);

        var result = chase.Advance("Creature:1", creature, target, MeleeRange, Now);

        Assert.Null(result);
    }

    [Fact]
    public void Advance_AnnouncesTheChaseOnItsFirstTickWithoutMovingYet()
    {
        var chase = new CreatureChaseService();
        var creature = new Position(0, 0, 0);
        var target = new Position(20, 0, 0);

        var start = chase.Advance("Creature:1", creature, target, MeleeRange, Now);

        Assert.NotNull(start);
        Assert.True(start!.Moving);
        Assert.Equal(target, start.Target);
        Assert.Equal(creature, start.Position);
    }

    [Fact]
    public void Advance_StepsTowardTheTargetProportionalToElapsedTime()
    {
        var chase = new CreatureChaseService();
        var creature = new Position(0, 0, 0);
        var target = new Position(20, 0, 0);
        chase.Advance("Creature:1", creature, target, MeleeRange, Now);

        var oneSecondLater = chase.Advance("Creature:1", creature, target, MeleeRange, Now + TimeSpan.FromSeconds(1));

        Assert.NotNull(oneSecondLater);
        Assert.True(oneSecondLater!.Moving);
        Assert.Equal((float)CreatureChaseService.Speed, oneSecondLater.Position.X, precision: 3);
        Assert.Equal(0f, oneSecondLater.Position.Z, precision: 3);
    }

    [Fact]
    public void Advance_ASlowedChaserCoversItsShareOfTheGround()
    {
        var chase = new CreatureChaseService();
        var creature = new Position(0, 0, 0);
        var target = new Position(20, 0, 0);
        chase.Advance("Creature:1", creature, target, MeleeRange, Now);

        var slowed = chase.Advance("Creature:1", creature, target, MeleeRange, Now + TimeSpan.FromSeconds(1), pace: 0.4);

        Assert.Equal((float)(CreatureChaseService.Speed * 0.4), slowed!.Position.X, precision: 3);
    }

    [Fact]
    public void Advance_NeverOvershootsTheTargetInOneStep()
    {
        var chase = new CreatureChaseService();
        var creature = new Position(0, 0, 0);
        var target = new Position(3.1f, 0, 0);
        chase.Advance("Creature:1", creature, target, MeleeRange, Now);

        var afterOneSecond = chase.Advance("Creature:1", creature, target, MeleeRange, Now + TimeSpan.FromSeconds(1));

        Assert.NotNull(afterOneSecond);
        Assert.Equal(target.X, afterOneSecond!.Position.X, precision: 3);
    }

    [Fact]
    public void Advance_ReportsATransitionOnceBackWithinMeleeRange()
    {
        var chase = new CreatureChaseService();
        var creature = new Position(0, 0, 0);
        var target = new Position(3.1f, 0, 0);
        chase.Advance("Creature:1", creature, target, MeleeRange, Now);
        var afterOneSecond = chase.Advance("Creature:1", creature, target, MeleeRange, Now + TimeSpan.FromSeconds(1))!;

        var stop = chase.Advance("Creature:1", afterOneSecond.Position, target, MeleeRange, Now + TimeSpan.FromSeconds(1));

        Assert.NotNull(stop);
        Assert.False(stop!.Moving);
        Assert.Null(stop.Target);
    }

    [Fact]
    public void Advance_ProducesNoUpdateOnceAlreadyStoppedAndStillInRange()
    {
        var chase = new CreatureChaseService();
        var creature = new Position(0, 0, 0);
        var target = new Position(2, 0, 0);
        chase.Advance("Creature:1", creature, target, MeleeRange, Now);

        var still = chase.Advance("Creature:1", creature, target, MeleeRange, Now + TimeSpan.FromSeconds(1));

        Assert.Null(still);
    }

    [Fact]
    public void Advance_ReChasesCleanlyAfterAPreviousChaseEnded()
    {
        var chase = new CreatureChaseService();
        var creature = new Position(0, 0, 0);
        var farTarget = new Position(20, 0, 0);
        chase.Advance("Creature:1", creature, farTarget, MeleeRange, Now);
        var closeEnough = new Position(3.1f, 0, 0);
        chase.Advance("Creature:1", closeEnough, closeEnough, MeleeRange, Now + TimeSpan.FromSeconds(1));

        var restart = chase.Advance("Creature:1", closeEnough, farTarget, MeleeRange, Now + TimeSpan.FromSeconds(2));

        Assert.NotNull(restart);
        Assert.Equal(closeEnough, restart!.Position);
    }

    [Fact]
    public void Clear_ForgetsChaseStateSoTheNextTickStartsFresh()
    {
        var chase = new CreatureChaseService();
        var creature = new Position(0, 0, 0);
        var target = new Position(20, 0, 0);
        chase.Advance("Creature:1", creature, target, MeleeRange, Now);

        chase.Clear("Creature:1");
        var afterClear = chase.Advance("Creature:1", creature, target, MeleeRange, Now + TimeSpan.FromSeconds(1));

        Assert.NotNull(afterClear);
        Assert.Equal(creature, afterClear!.Position);
    }

    private static CreatureChaseService ChasingAlong(params Position[] corners) =>
        new((_, _) => corners);

    [Fact]
    public void Advance_WalksTheCornersOfTheWayRoundRatherThanStraightAtItsQuarry()
    {
        var chase = ChasingAlong(new Position(0, 0, 0), new Position(0, 0, 20), new Position(20, 0, 20));
        var quarry = new Position(20, 0, 20);
        chase.Advance("Creature:1", new Position(0, 0, 0), quarry, MeleeRange, Now);

        var step = chase.Advance("Creature:1", new Position(0, 0, 0), quarry, MeleeRange, Now + TimeSpan.FromSeconds(1));

        Assert.NotNull(step);
        Assert.Equal(0f, step!.Position.X, precision: 3);
        Assert.Equal((float)CreatureChaseService.Speed, step.Position.Z, precision: 3);
    }

    [Fact]
    public void Advance_RampsHeightSteadilyAlongALegRatherThanSteppingUpIt()
    {
        var chase = ChasingAlong(new Position(0, 0, 0), new Position(12, 6, 0));
        var quarry = new Position(12, 6, 0);
        chase.Advance("Creature:1", new Position(0, 0, 0), quarry, MeleeRange, Now);

        var step = chase.Advance("Creature:1", new Position(0, 0, 0), quarry, MeleeRange, Now + TimeSpan.FromSeconds(1));

        Assert.NotNull(step);
        Assert.Equal((float)CreatureChaseService.Speed, step!.Position.X, precision: 3);
        Assert.Equal((float)CreatureChaseService.Speed / 2f, step.Position.Y, precision: 3);
    }

    [Fact]
    public void Advance_StandsExactlyOnACornerItWalksCleanPast()
    {
        var chase = ChasingAlong(new Position(0, 0, 0), new Position(1, 3, 0), new Position(1, 3, 40));
        var quarry = new Position(1, 3, 40);
        chase.Advance("Creature:1", new Position(0, 0, 0), quarry, MeleeRange, Now);

        var step = chase.Advance("Creature:1", new Position(0, 0, 0), quarry, MeleeRange, Now + TimeSpan.FromSeconds(1));

        Assert.NotNull(step);
        Assert.Equal(1f, step!.Position.X, precision: 3);
        Assert.Equal(3f, step.Position.Y, precision: 3);
        Assert.Equal((float)CreatureChaseService.Speed - 1f, step.Position.Z, precision: 3);
    }

    [Fact]
    public void Advance_AnnouncesTheCornerItIsHeadingForRatherThanItsQuarry()
    {
        var chase = ChasingAlong(new Position(0, 0, 0), new Position(0, 0, 20), new Position(20, 0, 20));
        var quarry = new Position(20, 0, 20);

        var start = chase.Advance("Creature:1", new Position(0, 0, 0), quarry, MeleeRange, Now);

        Assert.Equal(new Position(0, 0, 20), start!.Target);
    }

    [Fact]
    public void Advance_WalksAStraightLineWhereThereIsNoWayRoundToBeHad()
    {
        var chase = new CreatureChaseService((_, _) => null);
        var quarry = new Position(20, 0, 0);
        chase.Advance("Creature:1", new Position(0, 0, 0), quarry, MeleeRange, Now);

        var step = chase.Advance("Creature:1", new Position(0, 0, 0), quarry, MeleeRange, Now + TimeSpan.FromSeconds(1));

        Assert.Equal((float)CreatureChaseService.Speed, step!.Position.X, precision: 3);
    }

    [Fact]
    public void Advance_WalksAStraightLineAtItsOwnHeightRatherThanItsQuarrys()
    {
        var chase = new CreatureChaseService((_, _) => null);
        var quarry = new Position(60, 25, 0);
        var creature = new Position(0, 1, 0);
        chase.Advance("Creature:1", creature, quarry, MeleeRange, Now);

        var step = chase.Advance("Creature:1", creature, quarry, MeleeRange, Now + TimeSpan.FromSeconds(1));

        Assert.NotNull(step);
        Assert.Equal(1f, step!.Position.Y, precision: 3);
        Assert.Equal(1f, step.Target!.Value.Y, precision: 3);
        Assert.Equal(quarry.X, step.Target!.Value.X, precision: 3);
    }

    [Fact]
    public void Advance_KeepsTheRouteItHasWhileItsQuarryStaysNearWhereItWasPlanned()
    {
        var planned = 0;
        var chase = new CreatureChaseService((_, _) =>
        {
            planned++;
            return [new Position(0, 0, 0), new Position(0, 0, 40)];
        });
        var quarry = new Position(0, 0, 40);

        var at = Now;
        var position = new Position(0, 0, 0);
        for (var tick = 0; tick < 4; tick++)
        {
            at += TimeSpan.FromSeconds(0.5);
            quarry = quarry with { X = quarry.X + 0.25f };
            position = chase.Advance("Creature:1", position, quarry, MeleeRange, at)!.Position;
        }

        Assert.Equal(1, planned);
    }

    [Fact]
    public void Advance_WorksOutAFreshRouteOnceItsQuarryHasMovedFarEnough()
    {
        var planned = 0;
        var chase = new CreatureChaseService((_, to) =>
        {
            planned++;
            return [new Position(0, 0, 0), to];
        });
        var start = new Position(0, 0, 0);
        chase.Advance("Creature:1", start, new Position(0, 0, 40), MeleeRange, Now);

        var moved = new Position(0, 0, 40 + (float)CreatureChaseService.RouteRedrawDistance + 1f);
        chase.Advance("Creature:1", start, moved, MeleeRange, Now + TimeSpan.FromSeconds(0.5));

        Assert.Equal(2, planned);
    }
}
