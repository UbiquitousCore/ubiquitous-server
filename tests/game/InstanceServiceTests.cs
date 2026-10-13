using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class InstanceServiceTests
{
    [Fact]
    public void Enter_CreatesALiveInstanceAndReturnsItsWorldState()
    {
        var service = new InstanceService();
        var result = service.Enter("a1", 13, "a1");

        Assert.Equal(InstanceEntryCode.Ok, result.Code);
        Assert.NotNull(result.InstanceKey);
        Assert.NotNull(result.World);
        Assert.True(service.IsLive(result.InstanceKey!));
    }

    [Fact]
    public void Enter_DifferentOwnersOnTheSameMap_GetDifferentInstances()
    {
        var service = new InstanceService();
        var first = service.Enter("a2", 13, "a2");
        var second = service.Enter("b2", 13, "b2");

        Assert.NotEqual(first.InstanceKey, second.InstanceKey);
        Assert.NotSame(first.World, second.World);
    }

    [Fact]
    public void Enter_TheSameOwnerTwice_ReusesTheSameLiveInstance()
    {
        var service = new InstanceService();
        var first = service.Enter("a3", 13, "a3");
        var second = service.Enter("a3", 13, "a3");

        Assert.Equal(first.InstanceKey, second.InstanceKey);
        Assert.Same(first.World, second.World);
    }

    [Fact]
    public void Enter_TwoMembersOfTheSameOwner_ShareTheSameInstance_LikeAPartyEnteringTogether()
    {
        var service = new InstanceService();
        var leader = service.Enter("leader4", 13, "leader4");
        var follower = service.Enter("follower4", 13, "leader4");

        Assert.Equal(leader.InstanceKey, follower.InstanceKey);
        Assert.Same(leader.World, follower.World);
    }

    [Fact]
    public void Enter_TheSameOwnerOnADifferentMap_GetsADifferentInstance()
    {
        var service = new InstanceService();
        var mapA = service.Enter("a5", 13, "a5");
        var mapB = service.Enter("a5", 14, "a5");

        Assert.NotEqual(mapA.InstanceKey, mapB.InstanceKey);
    }

    [Fact]
    public void Leave_TearsTheInstanceDownOnceTheLastMemberIsGoneAndItsGraceRunsOut()
    {
        var service = new InstanceService();
        var now = DateTimeOffset.UnixEpoch;
        var result = service.Enter("a6", 13, "a6", now: now);
        service.Enter("b6", 13, "a6", now: now);

        service.Leave("a6", result.InstanceKey!, now);
        Assert.True(service.IsLive(result.InstanceKey!, now));

        service.Leave("b6", result.InstanceKey!, now);
        Assert.True(service.IsLive(result.InstanceKey!, now + InstanceService.EmptyGrace - TimeSpan.FromSeconds(1)));
        Assert.False(service.IsLive(result.InstanceKey!, now + InstanceService.EmptyGrace));
    }

    [Fact]
    public void Enter_WithinTheGrace_ReturnsTheSameCopyAndCancelsItsTeardown()
    {
        var service = new InstanceService();
        var now = DateTimeOffset.UnixEpoch;
        var first = service.Enter("a6b", 13, "a6b", now: now);
        service.Leave("a6b", first.InstanceKey!, now);

        var back = service.Enter("a6b", 13, "a6b", now: now + TimeSpan.FromMinutes(5));

        Assert.Same(first.World, back.World);
        Assert.Same(first.Shared, back.Shared);
        Assert.True(service.IsLive(first.InstanceKey!, now + InstanceService.EmptyGrace * 3));
    }

    [Fact]
    public void Leave_ForAnUnknownKeyOrMember_IsHarmless()
    {
        var service = new InstanceService();
        service.Leave("nobody7", "no-such-instance");
        Assert.False(service.IsLive("no-such-instance"));
    }

    [Fact]
    public void Enter_AfterTeardown_MintsABrandNewInstance()
    {
        var service = new InstanceService();
        var now = DateTimeOffset.UnixEpoch;
        var first = service.Enter("a8", 13, "a8", now: now);
        service.Leave("a8", first.InstanceKey!, now);
        var later = now + InstanceService.EmptyGrace;
        Assert.False(service.IsLive(first.InstanceKey!, later));

        var second = service.Enter("a8", 13, "a8", now: later);
        Assert.NotSame(first.World, second.World);
    }

    [Fact]
    public void ResetOwnedBy_EndsAnEmptyCopyButRefusesAnOccupiedOne()
    {
        var service = new InstanceService();
        var mine = service.Enter("a9", 13, "a9");

        Assert.False(service.ResetOwnedBy("a9"));
        Assert.True(service.IsLive(mine.InstanceKey!));

        service.Leave("a9", mine.InstanceKey!);
        Assert.True(service.ResetOwnedBy("a9"));
        Assert.False(service.IsLive(mine.InstanceKey!));

        var again = service.Enter("a9", 13, "a9");
        Assert.NotSame(mine.World, again.World);
    }

    [Fact]
    public void ResetOwnedBy_LeavesAnotherOwnersCopyAlone()
    {
        var service = new InstanceService();
        var mine = service.Enter("a10", 13, "a10");
        var theirs = service.Enter("b10", 13, "b10");
        service.Leave("a10", mine.InstanceKey!);
        service.Leave("b10", theirs.InstanceKey!);

        Assert.True(service.ResetOwnedBy("a10"));

        Assert.False(service.IsLive(mine.InstanceKey!));
        Assert.True(service.IsLive(theirs.InstanceKey!));
    }

    [Fact]
    public void AnOwnerStartsInNormalMode()
    {
        var service = new InstanceService();

        Assert.Equal(DungeonMode.Normal, service.ChosenMode("m1"));
        Assert.Equal(DungeonMode.Normal, service.Enter("m1", 14, "m1").Mode);
    }

    [Fact]
    public void ChoosingLegend_MakesTheOwnersNextCopyALegendOne()
    {
        var service = new InstanceService();

        Assert.Equal(DungeonModeChange.Changed, service.ChooseMode("m2", DungeonMode.Legend));
        Assert.Equal(DungeonMode.Legend, service.Enter("m2", 14, "m2").Mode);
        Assert.Equal(DungeonMode.Legend, service.Enter("p2", 14, "m2").Mode);
    }

    [Fact]
    public void ChoosingTheModeAlreadyChosen_ChangesNothing()
    {
        var service = new InstanceService();
        var copy = service.Enter("m3", 14, "m3");
        service.Leave("m3", copy.InstanceKey!);

        Assert.Equal(DungeonModeChange.Unchanged, service.ChooseMode("m3", DungeonMode.Normal));
        Assert.True(service.IsLive(copy.InstanceKey!));
    }

    [Fact]
    public void ChangingTheMode_EndsTheOwnersEmptyCopies()
    {
        var service = new InstanceService();
        var copy = service.Enter("m4", 14, "m4");
        service.Leave("m4", copy.InstanceKey!);

        Assert.Equal(DungeonModeChange.Changed, service.ChooseMode("m4", DungeonMode.Legend));
        Assert.False(service.IsLive(copy.InstanceKey!));
    }

    [Fact]
    public void ChangingTheModeWhileAnyoneIsInside_IsRefusedAndKeepsTheOldMode()
    {
        var service = new InstanceService();
        var copy = service.Enter("m5", 14, "m5");

        Assert.Equal(DungeonModeChange.Refused, service.ChooseMode("m5", DungeonMode.Legend));
        Assert.Equal(DungeonMode.Normal, service.ChosenMode("m5"));
        Assert.True(service.IsLive(copy.InstanceKey!));
    }

    [Fact]
    public void ACopyKeepsTheModeItWasOpenedIn()
    {
        var service = new InstanceService();
        var first = service.Enter("m6", 14, "m6");
        service.Leave("m6", first.InstanceKey!);
        service.ChooseMode("m6", DungeonMode.Legend);

        var next = service.Enter("m6", 14, "m6");

        Assert.Equal(DungeonMode.Normal, first.Mode);
        Assert.Equal(DungeonMode.Legend, next.Mode);
    }
}
