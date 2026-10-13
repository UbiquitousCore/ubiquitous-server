namespace Ubiquitous.Game.Tests;

public sealed class PartyServiceTests
{
    [Fact]
    public void Invite_ThenAccept_FormsANewPartyLedByTheInviter()
    {
        var party = new PartyService();

        var invite = party.Invite("a1", "Ayla", "b1", "Borin");
        Assert.Equal(PartyResultCode.Ok, invite.Code);

        var accept = party.Accept("b1");
        Assert.Equal(PartyResultCode.Ok, accept.Code);
        Assert.NotNull(accept.Roster);
        Assert.Equal(["a1"], accept.NotifyMemberIds);
        Assert.Equal(2, accept.Roster!.Members.Count);
        Assert.Contains(accept.Roster.Members, m => m.Id == "a1" && m.IsLeader);
        Assert.Contains(accept.Roster.Members, m => m.Id == "b1" && !m.IsLeader);
    }

    [Fact]
    public void TwoServices_DoNotSeeEachOthersParties()
    {
        var first = new PartyService();
        var second = new PartyService();
        first.Invite("a1", "Ayla", "b1", "Borin");
        first.Accept("b1");

        Assert.NotNull(first.RosterOf("a1"));
        Assert.Null(second.RosterOf("a1"));
        Assert.Equal(PartyResultCode.Ok, second.Invite("a1", "Ayla", "c1", "Cora").Code);
    }

    [Fact]
    public void Invite_CannotTargetSelf()
    {
        var party = new PartyService();
        Assert.Equal(PartyResultCode.SelfInvite, party.Invite("a2", "Ayla", "a2", "Ayla").Code);
    }

    [Fact]
    public void Invite_RefusedWhenTargetAlreadyInAParty()
    {
        var party = new PartyService();
        party.Invite("a3", "Ayla", "b3", "Borin");
        party.Accept("b3");

        var result = party.Invite("c3", "Cara", "b3", "Borin");
        Assert.Equal(PartyResultCode.TargetAlreadyInParty, result.Code);
    }

    [Fact]
    public void Invite_RefusedWhenTargetAlreadyHasAPendingInvite()
    {
        var party = new PartyService();
        party.Invite("a4", "Ayla", "b4", "Borin");

        var result = party.Invite("c4", "Cara", "b4", "Borin");
        Assert.Equal(PartyResultCode.AlreadyPending, result.Code);
    }

    [Fact]
    public void Invite_OnlyTheLeaderCanGrowAnExistingParty()
    {
        var party = new PartyService();
        party.Invite("a5", "Ayla", "b5", "Borin");
        party.Accept("b5");

        var result = party.Invite("b5", "Borin", "c5", "Cara");
        Assert.Equal(PartyResultCode.NotLeader, result.Code);
    }

    [Fact]
    public void Invite_RefusedOnceThePartyIsFull()
    {
        var party = new PartyService();
        party.Invite("lead6", "Leader", "m6-1", "M1");
        party.Accept("m6-1");
        for (var i = 2; i < PartyService.MaxPartySize; i++)
        {
            party.Invite("lead6", "Leader", $"m6-{i}", $"M{i}");
            party.Accept($"m6-{i}");
        }

        var result = party.Invite("lead6", "Leader", "overflow6", "Overflow");
        Assert.Equal(PartyResultCode.PartyFull, result.Code);
    }

    [Fact]
    public void Decline_RemovesThePendingInviteAndNamesTheInviter()
    {
        var party = new PartyService();
        party.Invite("a7", "Ayla", "b7", "Borin");

        var result = party.Decline("b7");
        Assert.Equal(PartyResultCode.Ok, result.Code);
        Assert.Equal("a7", result.InviterId);
        Assert.Equal("Ayla", result.InviterName);

        Assert.Equal(PartyResultCode.NoPendingInvite, party.Decline("b7").Code);
    }

    [Fact]
    public void Accept_WithNoPendingInvite_IsRefused()
    {
        var party = new PartyService();
        Assert.Equal(PartyResultCode.NoPendingInvite, party.Accept("nobody8").Code);
    }

    [Fact]
    public void Leave_ByAnOrdinaryMember_KeepsThePartyAndTheLeader()
    {
        var party = new PartyService();
        party.Invite("a9", "Ayla", "b9", "Borin");
        party.Accept("b9");
        party.Invite("a9", "Ayla", "c9", "Cara");
        party.Accept("c9");

        var result = party.Leave("c9");
        Assert.Equal(PartyResultCode.Ok, result.Code);
        Assert.NotNull(result.Roster);
        Assert.Equal(2, result.Roster!.Members.Count);
        Assert.Contains(result.Roster.Members, m => m.Id == "a9" && m.IsLeader);
        Assert.DoesNotContain(result.Roster.Members, m => m.Id == "c9");
        Assert.Equal(["a9", "b9"], result.NotifyMemberIds);
    }

    [Fact]
    public void Leave_ByTheLeader_HandsLeadershipToTheOldestRemainingMember()
    {
        var party = new PartyService();
        party.Invite("a10", "Ayla", "b10", "Borin");
        party.Accept("b10");
        party.Invite("a10", "Ayla", "c10", "Cara");
        party.Accept("c10");

        var result = party.Leave("a10");
        Assert.Equal(PartyResultCode.Ok, result.Code);
        Assert.Contains(result.Roster!.Members, m => m.Id == "b10" && m.IsLeader);
        Assert.Contains(result.Roster.Members, m => m.Id == "c10" && !m.IsLeader);
    }

    [Fact]
    public void Leave_DownToOneMember_DisbandsTheParty()
    {
        var party = new PartyService();
        party.Invite("a11", "Ayla", "b11", "Borin");
        party.Accept("b11");

        var result = party.Leave("b11");
        Assert.Equal(PartyResultCode.Ok, result.Code);
        Assert.Null(result.Roster);
        Assert.Equal(["a11"], result.NotifyMemberIds);

        Assert.Null(party.RosterOf("a11"));
    }

    [Fact]
    public void Leave_WhileNotInAParty_IsRefused()
    {
        var party = new PartyService();
        Assert.Equal(PartyResultCode.NotInParty, party.Leave("nobody12").Code);
    }

    [Fact]
    public void Kick_ByTheLeader_RemovesTheMemberAndKeepsTheRest()
    {
        var party = new PartyService();
        party.Invite("a13", "Ayla", "b13", "Borin");
        party.Accept("b13");
        party.Invite("a13", "Ayla", "c13", "Cara");
        party.Accept("c13");

        var result = party.Kick("a13", "c13");
        Assert.Equal(PartyResultCode.Ok, result.Code);
        Assert.Equal("c13", result.RemovedMemberId);
        Assert.NotNull(result.Roster);
        Assert.Equal(2, result.Roster!.Members.Count);
        Assert.Equal(["b13"], result.NotifyMemberIds);
        Assert.Null(party.RosterOf("c13"));
    }

    [Fact]
    public void Kick_ByAnyoneOtherThanTheLeader_IsRefused()
    {
        var party = new PartyService();
        party.Invite("a14", "Ayla", "b14", "Borin");
        party.Accept("b14");
        party.Invite("a14", "Ayla", "c14", "Cara");
        party.Accept("c14");

        var result = party.Kick("b14", "c14");
        Assert.Equal(PartyResultCode.NotLeader, result.Code);
    }

    [Fact]
    public void Kick_CannotTargetSelf()
    {
        var party = new PartyService();
        party.Invite("a15", "Ayla", "b15", "Borin");
        party.Accept("b15");

        Assert.Equal(PartyResultCode.CannotKickSelf, party.Kick("a15", "a15").Code);
    }

    [Fact]
    public void Kick_DownToOneMember_DisbandsTheParty()
    {
        var party = new PartyService();
        party.Invite("a16", "Ayla", "b16", "Borin");
        party.Accept("b16");

        var result = party.Kick("a16", "b16");
        Assert.Equal(PartyResultCode.Ok, result.Code);
        Assert.Null(result.Roster);
        Assert.Equal("b16", result.RemovedMemberId);
        Assert.Equal(["a16"], result.NotifyMemberIds);
        Assert.Null(party.RosterOf("a16"));
    }

    [Fact]
    public void Kick_ATargetNotInTheParty_IsRefused()
    {
        var party = new PartyService();
        party.Invite("a17", "Ayla", "b17", "Borin");
        party.Accept("b17");

        Assert.Equal(PartyResultCode.TargetNotInParty, party.Kick("a17", "somebodyElse17").Code);
    }

    [Fact]
    public void RosterOf_IsNullForSomeoneInNoParty()
    {
        Assert.Null(new PartyService().RosterOf("neverJoined18"));
    }

    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static PartyService PartyOfThree(string a, string b, string c)
    {
        var party = new PartyService();
        party.Invite(a, "Ayla", b, "Borin");
        party.Accept(b);
        party.Invite(a, "Ayla", c, "Cara");
        party.Accept(c);
        return party;
    }

    [Fact]
    public void AMemberGoingOffline_StaysInTheParty_ShownOfflineToEveryoneElse()
    {
        var party = PartyOfThree("a19", "b19", "c19");

        var result = party.MarkOffline("b19", Noon);

        Assert.Equal(PartyResultCode.Ok, result.Code);
        Assert.Equal(["a19", "c19"], result.NotifyMemberIds);
        Assert.Contains(result.Roster!.Members, m => m.Id == "b19" && !m.Online);
        Assert.Contains(result.Roster.Members, m => m.Id == "a19" && m.Online);
        Assert.Contains(party.RosterOf("a19")!.Members, m => m.Id == "b19" && !m.Online);
    }

    [Fact]
    public void AMemberComingBack_GetsTheirRoster_AndIsShownOnlineToEveryoneElse()
    {
        var party = PartyOfThree("a20", "b20", "c20");
        party.MarkOffline("b20", Noon);

        var result = party.MarkOnline("b20", Noon.AddMinutes(1));

        Assert.Equal(PartyResultCode.Ok, result.Code);
        Assert.Equal(["a20", "c20"], result.NotifyMemberIds);
        Assert.All(result.Roster!.Members, m => Assert.True(m.Online));
    }

    [Fact]
    public void TheLeaderGoingOfflineAndComingBack_KeepsTheLead()
    {
        var party = PartyOfThree("a21", "b21", "c21");

        var away = party.MarkOffline("a21", Noon);
        Assert.Contains(away.Roster!.Members, m => m.Id == "a21" && m.IsLeader && !m.Online);

        var back = party.MarkOnline("a21", Noon.AddMinutes(2));
        Assert.Contains(back.Roster!.Members, m => m.Id == "a21" && m.IsLeader && m.Online);
    }

    [Fact]
    public void ComingBackToNoParty_HasNothingToSay()
    {
        var result = new PartyService().MarkOnline("loner22", Noon);
        Assert.Equal(PartyResultCode.NotInParty, result.Code);
        Assert.Null(result.Roster);
        Assert.Empty(result.NotifyMemberIds);
    }

    [Fact]
    public void AMemberOfflineForTheWholeGrace_IsDropped_AndTheLeadPassesOn()
    {
        var party = PartyOfThree("a23", "b23", "c23");
        party.MarkOffline("a23", Noon);

        party.TickOffline(Noon + PartyService.OfflineGrace - TimeSpan.FromSeconds(1));
        Assert.NotNull(party.RosterOf("a23"));

        party.TickOffline(Noon + PartyService.OfflineGrace);
        Assert.Null(party.RosterOf("a23"));
        var roster = party.RosterOf("b23")!;
        Assert.Equal(["b23", "c23"], roster.Members.Select(m => m.Id));
        Assert.True(roster.Members[0].IsLeader);
        Assert.Contains(party.DrainNotices("b23"), n => n is PartyRosterChanged);
        Assert.Contains(party.DrainNotices("c23"), n => n is PartyRosterChanged);
    }

    [Fact]
    public void ComingBackAfterTheGrace_FindsNoParty()
    {
        var party = PartyOfThree("a24", "b24", "c24");
        party.MarkOffline("c24", Noon);

        var result = party.MarkOnline("c24", Noon + PartyService.OfflineGrace);

        Assert.Null(result.Roster);
        Assert.Equal(["a24", "b24"], party.RosterOf("a24")!.Members.Select(m => m.Id));
    }

    [Fact]
    public void DroppingAnOfflineMemberFromAPairOfTwo_DisbandsIt()
    {
        var party = new PartyService();
        party.Invite("a25", "Ayla", "b25", "Borin");
        party.Accept("b25");
        party.MarkOffline("b25", Noon);

        party.TickOffline(Noon + PartyService.OfflineGrace);

        Assert.Null(party.RosterOf("a25"));
        Assert.Null(party.RosterOf("b25"));
        Assert.Contains(party.DrainNotices("a25"), n => n is PartyRosterChanged);
    }

    [Fact]
    public void GoingOffline_DropsAnInviteWaitingForThem()
    {
        var party = new PartyService();
        party.Invite("a26", "Ayla", "b26", "Borin");

        party.MarkOffline("b26", Noon);

        Assert.Equal(PartyResultCode.NoPendingInvite, party.Accept("b26").Code);
    }
}
