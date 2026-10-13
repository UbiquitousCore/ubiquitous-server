namespace Ubiquitous.Game.Tests;

public sealed class PartyKickVoteTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    private static PartyService PartyOf(params string[] ids)
    {
        var party = new PartyService();
        for (var i = 1; i < ids.Length; i++)
        {
            Assert.Equal(PartyResultCode.Ok, party.Invite(ids[0], ids[0].ToUpperInvariant(), ids[i], ids[i].ToUpperInvariant()).Code);
            Assert.Equal(PartyResultCode.Ok, party.Accept(ids[i]).Code);
        }
        foreach (var id in ids) party.DrainNotices(id);
        return party;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    public void VotesNeeded_IsAStrictMajorityOfEveryoneButTheTarget(int membersBesideTarget, int needed) =>
        Assert.Equal(needed, PartyService.KickVotesNeeded(membersBesideTarget));

    [Fact]
    public void StartingAVote_AsksEveryoneButTheAskerAndTheTarget()
    {
        var party = PartyOf("kv1l", "kv1a", "kv1b", "kv1t");

        Assert.Equal(PartyResultCode.Ok, party.StartKickVote("kv1a", "kv1t", Start));

        foreach (var voter in new[] { "kv1l", "kv1b" })
        {
            var asked = Assert.IsType<KickVoteAsked>(Assert.Single(party.DrainNotices(voter)));
            Assert.Equal("kv1a", asked.InitiatorId);
            Assert.Equal("kv1t", asked.TargetId);
            Assert.Equal("KV1T", asked.TargetName);
        }
        Assert.Empty(party.DrainNotices("kv1a"));
        Assert.Empty(party.DrainNotices("kv1t"));
    }

    [Fact]
    public void TheLeaderKicksAndIsNotOfferedAVote()
    {
        var party = PartyOf("kv2l", "kv2a", "kv2t");

        Assert.Equal(PartyResultCode.LeaderKicks, party.StartKickVote("kv2l", "kv2t", Start));
    }

    [Fact]
    public void AMajority_RemovesTheTarget_AndTellsEveryone()
    {
        var party = PartyOf("kv3l", "kv3a", "kv3b", "kv3t");
        party.StartKickVote("kv3a", "kv3t", Start);
        Assert.NotNull(party.RosterOf("kv3t"));

        Assert.Equal(PartyResultCode.Ok, party.AnswerKickVote("kv3b", agree: true, Start));

        Assert.Null(party.RosterOf("kv3t"));
        Assert.Equal(3, party.RosterOf("kv3a")!.Members.Count);
        Assert.Contains(party.DrainNotices("kv3t"), notice => notice is KickVoteRemoved);
        foreach (var member in new[] { "kv3l", "kv3a", "kv3b" })
        {
            var notices = party.DrainNotices(member);
            Assert.Contains(notices, notice => notice is KickVoteEnded { Passed: true, TargetId: "kv3t" });
            Assert.Contains(notices, notice => notice is PartyRosterChanged);
        }
        Assert.Equal(PartyResultCode.NoKickVote, party.AnswerKickVote("kv3l", agree: true, Start));
    }

    [Fact]
    public void EnoughRefusals_EndTheVoteAtOnce_AndEachRefusalIsNamed()
    {
        var party = PartyOf("kv4l", "kv4a", "kv4b", "kv4t");
        party.StartKickVote("kv4a", "kv4t", Start);

        party.AnswerKickVote("kv4l", agree: false, Start);
        var toAsker = party.DrainNotices("kv4a");
        var refused = Assert.IsType<KickVoteRefused>(Assert.Single(toAsker));
        Assert.Equal("kv4l", refused.VoterId);
        Assert.Equal("kv4a", refused.InitiatorId);

        party.AnswerKickVote("kv4b", agree: false, Start);

        Assert.NotNull(party.RosterOf("kv4t"));
        Assert.Contains(party.DrainNotices("kv4a"), notice => notice is KickVoteEnded { Passed: false });
        Assert.Empty(party.DrainNotices("kv4t"));
    }

    [Fact]
    public void AnUnansweredQuestion_CountsAsARefusalOnceTheVoteRunsOut()
    {
        var party = PartyOf("kv5l", "kv5a", "kv5t");
        party.StartKickVote("kv5a", "kv5t", Start);
        party.DrainNotices("kv5l");

        party.TickKickVotes(Start + PartyService.KickVoteTimeout - TimeSpan.FromSeconds(1));
        Assert.Empty(party.DrainNotices("kv5a"));

        party.TickKickVotes(Start + PartyService.KickVoteTimeout);

        var notices = party.DrainNotices("kv5a");
        Assert.Contains(notices, notice => notice is KickVoteRefused { VoterId: "kv5l" });
        Assert.Contains(notices, notice => notice is KickVoteEnded { Passed: false });
        Assert.NotNull(party.RosterOf("kv5t"));
    }

    [Fact]
    public void InATwoMemberParty_TheAskerAloneIsAMajority()
    {
        var party = PartyOf("kv6l", "kv6a");

        Assert.Equal(PartyResultCode.Ok, party.StartKickVote("kv6a", "kv6l", Start));

        Assert.Null(party.RosterOf("kv6l"));
        Assert.Null(party.RosterOf("kv6a"));
    }

    [Fact]
    public void VotingOutTheLeader_HandsLeadershipOnTheWayLeavingDoes()
    {
        var party = PartyOf("kv7l", "kv7a", "kv7b");
        party.StartKickVote("kv7b", "kv7l", Start);

        party.AnswerKickVote("kv7a", agree: true, Start);

        var roster = party.RosterOf("kv7b")!;
        Assert.DoesNotContain(roster.Members, m => m.Id == "kv7l");
        Assert.Contains(roster.Members, m => m.Id == "kv7a" && m.IsLeader);
    }

    [Fact]
    public void OneVoteAtATime()
    {
        var party = PartyOf("kv8l", "kv8a", "kv8b", "kv8t");
        party.StartKickVote("kv8a", "kv8t", Start);

        Assert.Equal(PartyResultCode.KickVoteUnderWay, party.StartKickVote("kv8b", "kv8t", Start));
    }

    [Fact]
    public void AnswersFromAnyoneNotAsked_AreRefused()
    {
        var party = PartyOf("kv9l", "kv9a", "kv9b", "kv9t");
        party.StartKickVote("kv9a", "kv9t", Start);

        Assert.Equal(PartyResultCode.NoKickVote, party.AnswerKickVote("kv9t", agree: false, Start));
        Assert.Equal(PartyResultCode.NoKickVote, party.AnswerKickVote("kv9a", agree: true, Start));
        party.AnswerKickVote("kv9l", agree: true, Start);
        Assert.Equal(PartyResultCode.NoKickVote, party.AnswerKickVote("kv9l", agree: false, Start));
    }

    [Fact]
    public void AnyoneLeavingMidVote_CallsItOff()
    {
        var party = PartyOf("kv10l", "kv10a", "kv10b", "kv10t");
        party.StartKickVote("kv10a", "kv10t", Start);
        party.DrainNotices("kv10l");

        party.Leave("kv10b");

        Assert.Contains(party.DrainNotices("kv10l"), notice => notice is KickVoteEnded { Passed: false });
        Assert.Equal(PartyResultCode.NoKickVote, party.AnswerKickVote("kv10l", agree: true, Start));
        Assert.NotNull(party.RosterOf("kv10t"));
    }

    [Fact]
    public void RefusedFromOutsideTheParty_AndAtOneself()
    {
        var party = PartyOf("kv11l", "kv11a");

        Assert.Equal(PartyResultCode.NotInParty, party.StartKickVote("kv11x", "kv11l", Start));
        Assert.Equal(PartyResultCode.CannotKickSelf, party.StartKickVote("kv11a", "kv11a", Start));
        Assert.Equal(PartyResultCode.TargetNotInParty, party.StartKickVote("kv11a", "kv11x", Start));
    }
}
