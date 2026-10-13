using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class GuildTests
{
    [Fact]
    public async Task FoundingAGuild_ThenInvitingAndAccepting_PutsBothOnEachOthersRoster()
    {
        var hub = new PlaySessionHub();
        var guildStore = new InMemoryGuildStore();
        var (ayla, _) = await JoinAsync(hub, guildStore, "a1", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, guildStore, "b1", "Borin");

        var foundReply = await SendAsync(ayla, WebMessageTypes.GuildFound, 5, new { name = "Ironclad" });
        var foundRoster = Assert.Single(foundReply);
        Assert.Contains("\"type\":\"guild.roster\"", foundRoster);
        Assert.Contains("\"guildName\":\"Ironclad\"", foundRoster);

        var inviteReply = await SendAsync(ayla, WebMessageTypes.GuildInvite, 6, new { networkId = "player:char-b1" });
        Assert.Contains(inviteReply, m => m.Contains("\"type\":\"guild.inviteSent\""));
        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"guild.invited\"")
            && m.Contains("player:char-a1") && m.Contains("\"guildName\":\"Ironclad\""));

        var acceptReply = await SendAsync(borin, WebMessageTypes.GuildAccept, 7, new { });
        var roster = Assert.Single(acceptReply);
        Assert.Contains("\"type\":\"guild.roster\"", roster);
        Assert.Contains("player:char-a1", roster);
        Assert.Contains("player:char-b1", roster);
        Assert.Contains("\"isLeader\":true", roster);

        Assert.Contains(await ayla.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.GuildRosterRequest, 8, new { }))),
            m => m.Contains("player:char-b1"));
    }

    [Fact]
    public async Task Founding_RejectsATakenName()
    {
        var hub = new PlaySessionHub();
        var guildStore = new InMemoryGuildStore();
        var (ayla, _) = await JoinAsync(hub, guildStore, "a2", "Ayla");
        var (borin, _) = await JoinAsync(hub, guildStore, "b2", "Borin");
        await SendAsync(ayla, WebMessageTypes.GuildFound, 5, new { name = "Ironclad" });

        var reply = await SendAsync(borin, WebMessageTypes.GuildFound, 5, new { name = "Ironclad" });
        Assert.Contains(reply, m => m.Contains("\"type\":\"protocol.error\"") && m.Contains("already taken"));
    }

    [Fact]
    public async Task InvitingAPlayerWithNoLiveSession_IsRefusedAsNotOnline()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, new InMemoryGuildStore(), "a3", "Ayla");
        await SendAsync(ayla, WebMessageTypes.GuildFound, 5, new { name = "Ironclad" });

        var reply = await SendAsync(ayla, WebMessageTypes.GuildInvite, 6, new { networkId = "player:char-ghost3" });
        Assert.Contains(reply, m => m.Contains("\"type\":\"protocol.error\"") && m.Contains("not online"));
    }

    [Fact]
    public async Task DecliningAnInvite_NotifiesTheInviterAndLeavesNoGuild()
    {
        var hub = new PlaySessionHub();
        var guildStore = new InMemoryGuildStore();
        var (ayla, aylaInbox) = await JoinAsync(hub, guildStore, "a4", "Ayla");
        var (borin, _) = await JoinAsync(hub, guildStore, "b4", "Borin");
        await SendAsync(ayla, WebMessageTypes.GuildFound, 5, new { name = "Ironclad" });

        await SendAsync(ayla, WebMessageTypes.GuildInvite, 6, new { networkId = "player:char-b4" });
        var declineReply = await SendAsync(borin, WebMessageTypes.GuildDecline, 7, new { });

        Assert.Contains(declineReply, m => m.Contains("\"type\":\"guild.roster\"") && m.Contains("\"members\":[]"));
        Assert.Contains(aylaInbox, m => m.Contains("\"type\":\"guild.declined\"") && m.Contains("player:char-b4"));
    }

    [Fact]
    public async Task LeaderLeavingAGuildWithCompany_DissolvesItForEveryone_NoLeadershipTransfer()
    {
        var hub = new PlaySessionHub();
        var guildStore = new InMemoryGuildStore();
        var (ayla, _) = await JoinAsync(hub, guildStore, "a5", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, guildStore, "b5", "Borin");
        await SendAsync(ayla, WebMessageTypes.GuildFound, 5, new { name = "Ironclad" });
        await SendAsync(ayla, WebMessageTypes.GuildInvite, 6, new { networkId = "player:char-b5" });
        await SendAsync(borin, WebMessageTypes.GuildAccept, 7, new { });
        borinInbox.Clear();

        var leaveReply = await SendAsync(ayla, WebMessageTypes.GuildLeave, 8, new { });

        Assert.Contains(leaveReply, m => m.Contains("\"type\":\"guild.roster\"") && m.Contains("\"members\":[]"));
        var pushed = Assert.Single(borinInbox, m => m.Contains("\"type\":\"guild.roster\""));
        Assert.Contains("\"members\":[]", pushed);
    }

    [Fact]
    public async Task LeaderCanKickAMember_WhoIsToldSeparatelyFromTheSurvivors()
    {
        var hub = new PlaySessionHub();
        var guildStore = new InMemoryGuildStore();
        var (ayla, _) = await JoinAsync(hub, guildStore, "a6", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, guildStore, "b6", "Borin");
        var (cara, caraInbox) = await JoinAsync(hub, guildStore, "c6", "Cara");
        await SendAsync(ayla, WebMessageTypes.GuildFound, 5, new { name = "Ironclad" });
        await SendAsync(ayla, WebMessageTypes.GuildInvite, 6, new { networkId = "player:char-b6" });
        await SendAsync(borin, WebMessageTypes.GuildAccept, 7, new { });
        await SendAsync(ayla, WebMessageTypes.GuildInvite, 8, new { networkId = "player:char-c6" });
        await SendAsync(cara, WebMessageTypes.GuildAccept, 9, new { });
        borinInbox.Clear();

        var kickReply = await SendAsync(ayla, WebMessageTypes.GuildKick, 10, new { networkId = "player:char-c6" });

        Assert.Contains(kickReply, m => m.Contains("\"type\":\"guild.roster\"") && !m.Contains("player:char-c6"));
        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"guild.roster\"") && !m.Contains("player:char-c6"));
        Assert.Contains(caraInbox, m => m.Contains("\"type\":\"guild.kicked\""));
    }

    [Fact]
    public async Task ANonLeaderCannotKickAnyone()
    {
        var hub = new PlaySessionHub();
        var guildStore = new InMemoryGuildStore();
        var (ayla, _) = await JoinAsync(hub, guildStore, "a7", "Ayla");
        var (borin, _) = await JoinAsync(hub, guildStore, "b7", "Borin");
        var (cara, _) = await JoinAsync(hub, guildStore, "c7", "Cara");
        await SendAsync(ayla, WebMessageTypes.GuildFound, 5, new { name = "Ironclad" });
        await SendAsync(ayla, WebMessageTypes.GuildInvite, 6, new { networkId = "player:char-b7" });
        await SendAsync(borin, WebMessageTypes.GuildAccept, 7, new { });
        await SendAsync(ayla, WebMessageTypes.GuildInvite, 8, new { networkId = "player:char-c7" });
        await SendAsync(cara, WebMessageTypes.GuildAccept, 9, new { });

        var reply = await SendAsync(borin, WebMessageTypes.GuildKick, 10, new { networkId = "player:char-c7" });
        Assert.Contains(reply, m => m.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task LeaderCanDisbandTheGuildOutright_NotifyingEveryOtherMember()
    {
        var hub = new PlaySessionHub();
        var guildStore = new InMemoryGuildStore();
        var (ayla, _) = await JoinAsync(hub, guildStore, "a8", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, guildStore, "b8", "Borin");
        await SendAsync(ayla, WebMessageTypes.GuildFound, 5, new { name = "Ironclad" });
        await SendAsync(ayla, WebMessageTypes.GuildInvite, 6, new { networkId = "player:char-b8" });
        await SendAsync(borin, WebMessageTypes.GuildAccept, 7, new { });
        borinInbox.Clear();

        var disbandReply = await SendAsync(ayla, WebMessageTypes.GuildDisband, 8, new { });

        Assert.Contains(disbandReply, m => m.Contains("\"type\":\"guild.roster\"") && m.Contains("\"members\":[]"));
        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"guild.roster\"") && m.Contains("\"members\":[]"));
    }

    [Fact]
    public async Task GuildFoundedBeforeARestart_SurvivesItWithFullMembership()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-guild-session-{Guid.NewGuid():N}");
        try
        {
            var firstStore = new FileGuildStore(directory);
            var firstHub = new PlaySessionHub();
            var (ayla, _) = await JoinAsync(firstHub, firstStore, "a9", "Ayla");
            var (borin, _) = await JoinAsync(firstHub, firstStore, "b9", "Borin");
            await SendAsync(ayla, WebMessageTypes.GuildFound, 5, new { name = "Ironclad" });
            await SendAsync(ayla, WebMessageTypes.GuildInvite, 6, new { networkId = "player:char-b9" });
            await SendAsync(borin, WebMessageTypes.GuildAccept, 7, new { });

            var secondStore = new FileGuildStore(directory);
            var secondHub = new PlaySessionHub();
            var (aylaAgain, _) = await JoinAsync(secondHub, secondStore, "a9", "Ayla");

            var reopened = await SendAsync(aylaAgain, WebMessageTypes.GuildRosterRequest, 8, new { });
            var roster = Assert.Single(reopened);
            Assert.Contains("\"guildName\":\"Ironclad\"", roster);
            Assert.Contains("player:char-a9", roster);
            Assert.Contains("player:char-b9", roster);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task<(PlaySession Session, List<string> Inbox)> JoinAsync(
        PlaySessionHub hub, Ubiquitous.Common.IGuildStore guildStore, string guid, string name)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([Character(guid, name)]), guildStore: guildStore, guilds: TestSessions.GuildsOf(hub, guildStore));
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });

        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name))));

        return (session, inbox);
    }

    private static async Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload)));

    private static CharacterRecord Character(string guid, string name) => new(
        $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 10, [100, 200]);
}
