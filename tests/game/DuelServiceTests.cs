using Ubiquitous.Common;

namespace Ubiquitous.Game.Tests;

public sealed class DuelServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Position Origin = new(0, 0, 0);
    private static readonly Position Nearby = new(5, 0, 0);
    private static readonly Position FarAway = new(500, 0, 0);

    [Fact]
    public void Request_QueuesANotificationForTheTarget()
    {
        var duels = new DuelService();

        var outcome = duels.Request("player:a", Origin, "player:b", Now);

        Assert.Equal(DuelService.RequestOutcome.Ok, outcome);
        var notification = Assert.Single(duels.Drain("player:b"));
        var requested = Assert.IsType<DuelChallenged>(notification);
        Assert.Equal("player:a", requested.OpponentId);
    }

    [Fact]
    public void Request_RefusesDuellingYourself()
    {
        var duels = new DuelService();

        Assert.Equal(DuelService.RequestOutcome.SelfDuel, duels.Request("player:a", Origin, "player:a", Now));
    }

    [Fact]
    public void Request_RefusesWhenEitherSideIsAlreadyDuelling()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);
        duels.Accept("player:b", Origin, Now);

        Assert.Equal(DuelService.RequestOutcome.AlreadyDuelling,
            duels.Request("player:a", Origin, "player:c", Now));
        Assert.Equal(DuelService.RequestOutcome.AlreadyDuelling,
            duels.Request("player:c", Origin, "player:b", Now));
    }

    [Fact]
    public void Accept_WithNoPendingRequestIsRefused()
    {
        var duels = new DuelService();

        var result = duels.Accept("player:b", Origin, Now);

        Assert.Equal(DuelService.AcceptOutcome.NoPendingRequest, result.Outcome);
    }

    [Fact]
    public void Accept_TooFarFromTheRequesterIsRefused()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);

        var result = duels.Accept("player:b", FarAway, Now);

        Assert.Equal(DuelService.AcceptOutcome.OutOfRange, result.Outcome);
        Assert.Null(duels.OpponentOf("player:a"));
        Assert.Null(duels.OpponentOf("player:b"));
    }

    [Fact]
    public void Accept_InRangeStartsTheDuelForBothSides()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);

        var result = duels.Accept("player:b", Nearby, Now);

        Assert.Equal(DuelService.AcceptOutcome.Ok, result.Outcome);
        Assert.Equal("player:a", result.OpponentId);
        Assert.True(duels.IsActiveOpponent("player:a", "player:b"));
        Assert.True(duels.IsActiveOpponent("player:b", "player:a"));
        var started = Assert.IsType<DuelStarted>(Assert.Single(duels.Drain("player:a")));
        Assert.Equal("player:b", started.OpponentId);
    }

    [Fact]
    public void Accept_AfterExpiryIsRefusedAndNotifiesTheRequesterOfADecline()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);

        var result = duels.Accept("player:b", Nearby, Now + DuelService.RequestExpiry + TimeSpan.FromSeconds(1));

        Assert.Equal(DuelService.AcceptOutcome.Expired, result.Outcome);
        var declined = Assert.IsType<DuelDeclined>(Assert.Single(duels.Drain("player:a")));
        Assert.Equal("player:b", declined.OpponentId);
    }

    [Fact]
    public void Decline_NotifiesTheRequesterAndClearsThePendingRequest()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);

        var requesterId = duels.Decline("player:b");

        Assert.Equal("player:a", requesterId);
        var declined = Assert.IsType<DuelDeclined>(Assert.Single(duels.Drain("player:a")));
        Assert.Equal("player:b", declined.OpponentId);
        Assert.Equal(DuelService.AcceptOutcome.NoPendingRequest, duels.Accept("player:b", Origin, Now).Outcome);
    }

    [Fact]
    public void Decline_WithNothingPendingReturnsNull()
    {
        var duels = new DuelService();

        Assert.Null(duels.Decline("player:b"));
    }

    [Fact]
    public void IsActiveOpponent_IsFalseForAnyoneNotActuallyDuelling()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);
        duels.Accept("player:b", Nearby, Now);

        Assert.False(duels.IsActiveOpponent("player:a", "player:c"));
        Assert.False(duels.IsActiveOpponent("player:c", "player:a"));
        Assert.False(duels.IsActiveOpponent("player:c", "player:d"));
    }

    [Fact]
    public void End_RemovesBothSidesAndNotifiesTheOpponentWithTheFlippedReason()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);
        duels.Accept("player:b", Nearby, Now);
        duels.Drain("player:a");
        duels.Drain("player:b");

        var opponentId = duels.End("player:a", DuelEndReason.Lost);

        Assert.Equal("player:b", opponentId);
        Assert.Null(duels.OpponentOf("player:a"));
        Assert.Null(duels.OpponentOf("player:b"));
        var over = Assert.IsType<DuelOver>(Assert.Single(duels.Drain("player:b")));
        Assert.Equal("player:a", over.OpponentId);
        Assert.Equal(DuelEndReason.Won, over.Reason);
    }

    [Fact]
    public void End_ForfeitFlipsToOpponentForfeitedForTheOtherSide()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);
        duels.Accept("player:b", Nearby, Now);
        duels.Drain("player:a");

        duels.End("player:b", DuelEndReason.Forfeited);

        var over = Assert.IsType<DuelOver>(Assert.Single(duels.Drain("player:a")));
        Assert.Equal(DuelEndReason.OpponentForfeited, over.Reason);
    }

    [Fact]
    public void End_LosingTheRaceAgainstTheOpponentIsANoOp()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);
        duels.Accept("player:b", Nearby, Now);

        Assert.Equal("player:b", duels.End("player:a", DuelEndReason.Lost));
        Assert.Null(duels.End("player:b", DuelEndReason.Won));
    }

    [Fact]
    public void End_WithNoActiveDuelReturnsNull()
    {
        var duels = new DuelService();

        Assert.Null(duels.End("player:a", DuelEndReason.Forfeited));
    }

    [Fact]
    public void Drain_NeverReturnsTheSameNotificationTwice()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);

        Assert.Single(duels.Drain("player:b"));
        Assert.Empty(duels.Drain("player:b"));
    }

    [Fact]
    public void ANewRequestReplacesWhateverWasPreviouslyPendingForTheSameTarget()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);
        duels.Request("player:c", Origin, "player:b", Now);
        duels.Drain("player:b");

        var result = duels.Accept("player:b", Origin, Now);

        Assert.Equal("player:c", result.OpponentId);
    }

    [Fact]
    public void Leave_EndsTheDuelAsAForfeit_SoNeitherSideIsStuckDuelling()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);
        duels.Accept("player:b", Nearby, Now);
        duels.Drain("player:a");

        duels.Leave("player:b");

        Assert.Null(duels.OpponentOf("player:a"));
        var over = Assert.IsType<DuelOver>(Assert.Single(duels.Drain("player:a")));
        Assert.Equal(DuelEndReason.OpponentForfeited, over.Reason);
        Assert.Equal(DuelService.RequestOutcome.Ok, duels.Request("player:a", Origin, "player:c", Now));
    }

    [Fact]
    public void Leave_LapsesChallengesToAndFromThePlayerWhoLeft()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);
        duels.Request("player:c", Origin, "player:a", Now);

        duels.Leave("player:a");

        Assert.Equal(DuelService.AcceptOutcome.NoPendingRequest, duels.Accept("player:b", Nearby, Now).Outcome);
        Assert.Empty(duels.Drain("player:a"));
    }

    [Fact]
    public void AnAcceptedDuel_BeginsOnlyOnceItsCountdownHasRunOut()
    {
        var duels = new DuelService();
        duels.Request("player:a", Origin, "player:b", Now);
        duels.Accept("player:b", Nearby, Now);

        Assert.Equal(Now + DuelService.Countdown, duels.StartsAt("player:a"));
        Assert.False(duels.IsActiveOpponent("player:a", "player:b", Now + TimeSpan.FromSeconds(4)));
        Assert.True(duels.IsActiveOpponent("player:a", "player:b", Now + DuelService.Countdown));
    }
}
