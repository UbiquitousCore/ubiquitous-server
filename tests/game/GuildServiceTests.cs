using Ubiquitous.Common;

namespace Ubiquitous.Game.Tests;

public sealed class GuildServiceTests
{
    private sealed class FakeGuildStore : IGuildStore
    {
        private readonly Dictionary<string, PersistedGuild> _byId = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<PersistedGuild>> ListAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PersistedGuild> result = _byId.Values.ToArray();
            return Task.FromResult(result);
        }

        public Task SaveAsync(PersistedGuild guild, CancellationToken cancellationToken = default)
        {
            _byId[guild.Id] = guild;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string guildId, CancellationToken cancellationToken = default)
        {
            _byId.Remove(guildId);
            return Task.CompletedTask;
        }
    }

    private static GuildService NewService(out FakeGuildStore store)
    {
        store = new FakeGuildStore();
        return new GuildService(store);
    }

    [Fact]
    public async Task Found_CreatesAGuildLedByTheFounder()
    {
        var service = NewService(out _);
        var result = await service.Found("a1", "Ayla", 10, "Ironclad");
        Assert.Equal(GuildResultCode.Ok, result.Code);
        Assert.NotNull(result.Roster);
        Assert.Equal("Ironclad", result.Roster!.GuildName);
        var member = Assert.Single(result.Roster.Members);
        Assert.Equal("a1", member.Id);
        Assert.True(member.IsLeader);
    }

    [Fact]
    public async Task Found_RejectsABlankName()
    {
        var service = NewService(out _);
        var result = await service.Found("a2", "Ayla", 10, "   ");
        Assert.Equal(GuildResultCode.InvalidName, result.Code);
    }

    [Fact]
    public async Task Found_RejectsANameOverSixteenCharacters()
    {
        var service = NewService(out _);
        var result = await service.Found("a2b", "Ayla", 10, new string('x', 17));
        Assert.Equal(GuildResultCode.InvalidName, result.Code);
    }

    [Fact]
    public async Task Found_AcceptsANameOfExactlySixteenCharacters()
    {
        var service = NewService(out _);
        var result = await service.Found("a2c", "Ayla", 10, new string('x', 16));
        Assert.Equal(GuildResultCode.Ok, result.Code);
    }

    [Fact]
    public async Task Found_RejectsAFounderBelowTheRealMinimumLevel()
    {
        var service = NewService(out _);
        var result = await service.Found("a2c", "Ayla", GuildService.MinFoundingLevel - 1, "Ironclad");
        Assert.Equal(GuildResultCode.TooLowLevel, result.Code);
    }

    [Fact]
    public async Task Found_RejectsAnAlreadyTakenName_CaseInsensitively()
    {
        var service = NewService(out _);
        await service.Found("a3", "Ayla", 10, "Ironclad");

        var result = await service.Found("b3", "Borin", 10, "ironclad");
        Assert.Equal(GuildResultCode.NameTaken, result.Code);
    }

    [Fact]
    public async Task Found_RejectsAFounderAlreadyInAGuild()
    {
        var service = NewService(out _);
        await service.Found("a4", "Ayla", 10, "Ironclad");

        var result = await service.Found("a4", "Ayla", 10, "SecondGuild");
        Assert.Equal(GuildResultCode.AlreadyInGuild, result.Code);
    }

    [Fact]
    public async Task Invite_ThenAccept_AddsAnOrdinaryMember()
    {
        var service = NewService(out _);
        await service.Found("a5", "Ayla", 10, "Ironclad");

        var invite = await service.Invite("a5", "b5", "Borin");
        Assert.Equal(GuildResultCode.Ok, invite.Code);
        Assert.Equal("Ironclad", invite.GuildName);

        var accept = await service.Accept("b5");
        Assert.Equal(GuildResultCode.Ok, accept.Code);
        Assert.Equal(["a5"], accept.NotifyMemberIds);
        Assert.Equal(2, accept.Roster!.Members.Count);
        Assert.Contains(accept.Roster.Members, m => m.Id == "a5" && m.IsLeader);
        Assert.Contains(accept.Roster.Members, m => m.Id == "b5" && !m.IsLeader);
    }

    [Fact]
    public async Task Invite_CannotTargetSelf()
    {
        var service = NewService(out _);
        await service.Found("a6", "Ayla", 10, "Ironclad");
        Assert.Equal(GuildResultCode.SelfInvite, (await service.Invite("a6", "a6", "Ayla")).Code);
    }

    [Fact]
    public async Task Invite_RefusedWhenNotInAGuild()
    {
        var service = NewService(out _);
        Assert.Equal(GuildResultCode.NotInGuild, (await service.Invite("nobody7", "b7", "Borin")).Code);
    }

    [Fact]
    public async Task Invite_OnlyTheLeaderCanInvite()
    {
        var service = NewService(out _);
        await service.Found("a8", "Ayla", 10, "Ironclad");
        await service.Invite("a8", "b8", "Borin");
        await service.Accept("b8");

        var result = await service.Invite("b8", "c8", "Cara");
        Assert.Equal(GuildResultCode.NotLeader, result.Code);
    }

    [Fact]
    public async Task Invite_RefusedWhenTargetAlreadyInAGuild()
    {
        var service = NewService(out _);
        await service.Found("a9", "Ayla", 10, "Ironclad");
        await service.Found("b9", "Borin", 10, "Stormguard");

        var result = await service.Invite("a9", "b9", "Borin");
        Assert.Equal(GuildResultCode.TargetAlreadyInGuild, result.Code);
    }

    [Fact]
    public async Task Invite_RefusedWhenTargetAlreadyHasAPendingInvite()
    {
        var service = NewService(out _);
        await service.Found("a10", "Ayla", 10, "Ironclad");
        await service.Found("c10", "Cara", 10, "Stormguard");
        await service.Invite("a10", "b10", "Borin");

        var result = await service.Invite("c10", "b10", "Borin");
        Assert.Equal(GuildResultCode.AlreadyPending, result.Code);
    }

    [Fact]
    public async Task Decline_RemovesThePendingInviteAndNamesTheInviterAndGuild()
    {
        var service = NewService(out _);
        await service.Found("a11", "Ayla", 10, "Ironclad");
        await service.Invite("a11", "b11", "Borin");

        var result = await service.Decline("b11");
        Assert.Equal(GuildResultCode.Ok, result.Code);
        Assert.Equal("a11", result.InviterId);
        Assert.Equal("Ayla", result.InviterName);
        Assert.Equal("Ironclad", result.GuildName);

        Assert.Equal(GuildResultCode.NoPendingInvite, (await service.Decline("b11")).Code);
    }

    [Fact]
    public async Task Accept_WithNoPendingInvite_IsRefused()
    {
        var service = NewService(out _);
        Assert.Equal(GuildResultCode.NoPendingInvite, (await service.Accept("nobody12")).Code);
    }

    [Fact]
    public async Task Leave_ByAnOrdinaryMember_KeepsTheGuildAndItsLeader()
    {
        var service = NewService(out _);
        await service.Found("a13", "Ayla", 10, "Ironclad");
        await service.Invite("a13", "b13", "Borin");
        await service.Accept("b13");
        await service.Invite("a13", "c13", "Cara");
        await service.Accept("c13");

        var result = await service.Leave("c13");
        Assert.Equal(GuildResultCode.Ok, result.Code);
        Assert.Equal(2, result.Roster!.Members.Count);
        Assert.Contains(result.Roster.Members, m => m.Id == "a13" && m.IsLeader);
        Assert.DoesNotContain(result.Roster.Members, m => m.Id == "c13");
        Assert.Equal(["a13", "b13"], result.NotifyMemberIds);
    }

    [Fact]
    public async Task Leave_ByTheLeaderWithCompany_DissolvesTheGuildForEveryone_NoLeadershipTransfer()
    {
        var service = NewService(out _);
        await service.Found("a14", "Ayla", 10, "Ironclad");
        await service.Invite("a14", "b14", "Borin");
        await service.Accept("b14");
        await service.Invite("a14", "c14", "Cara");
        await service.Accept("c14");

        var result = await service.Leave("a14");
        Assert.Equal(GuildResultCode.Ok, result.Code);
        Assert.Null(result.Roster);
        Assert.Equal(["b14", "c14"], result.NotifyMemberIds);

        Assert.Null(await service.RosterOf("b14"));
        Assert.Null(await service.RosterOf("c14"));
    }

    [Fact]
    public async Task Leave_ByALoneLeader_DissolvesTheGuild()
    {
        var service = NewService(out _);
        await service.Found("a15", "Ayla", 10, "Ironclad");

        var result = await service.Leave("a15");
        Assert.Equal(GuildResultCode.Ok, result.Code);
        Assert.Null(result.Roster);
        Assert.Empty(result.NotifyMemberIds);

        Assert.Null(await service.RosterOf("a15"));
        Assert.Equal(GuildResultCode.Ok, (await service.Found("a15", "Ayla", 10, "Ironclad")).Code);
    }

    [Fact]
    public async Task Leave_ByASoloOrdinaryMember_DoesNotAutoDisband_UnlikeParty()
    {
        var service = NewService(out _);
        await service.Found("a16", "Ayla", 10, "Ironclad");
        await service.Invite("a16", "b16", "Borin");
        await service.Accept("b16");

        var result = await service.Leave("b16");
        Assert.Equal(GuildResultCode.Ok, result.Code);
        Assert.Equal(["a16"], result.NotifyMemberIds);

        var roster = await service.RosterOf("a16");
        Assert.NotNull(roster);
        Assert.Single(roster!.Members);
    }

    [Fact]
    public async Task Leave_WhileNotInAGuild_IsRefused()
    {
        var service = NewService(out _);
        Assert.Equal(GuildResultCode.NotInGuild, (await service.Leave("nobody17")).Code);
    }

    [Fact]
    public async Task Kick_ByTheLeader_RemovesTheMemberAndKeepsTheRest()
    {
        var service = NewService(out _);
        await service.Found("a18", "Ayla", 10, "Ironclad");
        await service.Invite("a18", "b18", "Borin");
        await service.Accept("b18");
        await service.Invite("a18", "c18", "Cara");
        await service.Accept("c18");

        var result = await service.Kick("a18", "c18");
        Assert.Equal(GuildResultCode.Ok, result.Code);
        Assert.Equal("c18", result.RemovedMemberId);
        Assert.Equal(2, result.Roster!.Members.Count);
        Assert.Equal(["b18"], result.NotifyMemberIds);
        Assert.Null(await service.RosterOf("c18"));
    }

    [Fact]
    public async Task Kick_ByAnyoneOtherThanTheLeader_IsRefused()
    {
        var service = NewService(out _);
        await service.Found("a19", "Ayla", 10, "Ironclad");
        await service.Invite("a19", "b19", "Borin");
        await service.Accept("b19");
        await service.Invite("a19", "c19", "Cara");
        await service.Accept("c19");

        Assert.Equal(GuildResultCode.NotLeader, (await service.Kick("b19", "c19")).Code);
    }

    [Fact]
    public async Task Kick_CannotTargetSelf()
    {
        var service = NewService(out _);
        await service.Found("a20", "Ayla", 10, "Ironclad");
        Assert.Equal(GuildResultCode.CannotKickSelf, (await service.Kick("a20", "a20")).Code);
    }

    [Fact]
    public async Task Kick_ATargetNotInTheGuild_IsRefused()
    {
        var service = NewService(out _);
        await service.Found("a21", "Ayla", 10, "Ironclad");
        Assert.Equal(GuildResultCode.TargetNotInGuild, (await service.Kick("a21", "somebodyElse21")).Code);
    }

    [Fact]
    public async Task Disband_ByTheLeader_DissolvesTheGuildRegardlessOfMemberCount()
    {
        var service = NewService(out _);
        await service.Found("a22", "Ayla", 10, "Ironclad");
        await service.Invite("a22", "b22", "Borin");
        await service.Accept("b22");

        var result = await service.Disband("a22");
        Assert.Equal(GuildResultCode.Ok, result.Code);
        Assert.Null(result.Roster);
        Assert.Equal(["b22"], result.NotifyMemberIds);

        Assert.Null(await service.RosterOf("a22"));
        Assert.Null(await service.RosterOf("b22"));
    }

    [Fact]
    public async Task Disband_ByAnyoneOtherThanTheLeader_IsRefused()
    {
        var service = NewService(out _);
        await service.Found("a23", "Ayla", 10, "Ironclad");
        await service.Invite("a23", "b23", "Borin");
        await service.Accept("b23");

        Assert.Equal(GuildResultCode.NotLeader, (await service.Disband("b23")).Code);
    }

    [Fact]
    public async Task RosterOf_IsNullForSomeoneInNoGuild()
    {
        var service = NewService(out _);
        Assert.Null(await service.RosterOf("neverJoined24"));
    }

    [Fact]
    public async Task TwoServices_DoNotSeeEachOthersInvites()
    {
        var first = NewService(out _);
        var second = NewService(out _);
        await first.Found("a26", "Ayla", 10, "Ironclad");
        await first.Invite("a26", "b26", "Borin");

        Assert.Equal(GuildResultCode.NoPendingInvite, (await second.Decline("b26")).Code);
    }

    [Fact]
    public async Task Guild_SurvivesAsANewServiceInstanceBackedByTheSameStore()
    {
        var store = new FakeGuildStore();
        var first = new GuildService(store);
        await first.Found("a25", "Ayla", 10, "Ironclad");

        var second = new GuildService(store);
        var roster = await second.RosterOf("a25");
        Assert.NotNull(roster);
        Assert.Equal("Ironclad", roster!.GuildName);
    }
}
