using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PartyTests
{
    [Fact]
    public async Task InvitingAVisiblePlayer_ThenAccepting_PutsBothOnEachOthersRoster()
    {
        var hub = new PlaySessionHub();
        var (ayla, aylaInbox) = await JoinAsync(hub, "a1", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, "b1", "Borin");

        var inviteReply = await SendAsync(ayla, WebMessageTypes.PartyInvite, 5,
            new { networkId = "player:char-b1" });
        Assert.Contains(inviteReply, m => m.Contains("\"type\":\"party.inviteSent\""));
        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"party.invited\"")
            && m.Contains("player:char-a1"));

        var acceptReply = await SendAsync(borin, WebMessageTypes.PartyAccept, 6, new { });
        var roster = Assert.Single(acceptReply);
        Assert.Contains("\"type\":\"party.roster\"", roster);
        Assert.Contains("player:char-a1", roster);
        Assert.Contains("player:char-b1", roster);
        Assert.Contains("\"isLeader\":true", roster);

        Assert.Contains(aylaInbox, m => m.Contains("\"type\":\"party.roster\"")
            && m.Contains("player:char-b1"));
    }

    [Fact]
    public async Task InvitingAPlayerWithNoLiveSession_IsRefusedAsNotOnline()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "a2", "Ayla");

        var reply = await SendAsync(ayla, WebMessageTypes.PartyInvite, 5,
            new { networkId = "player:char-ghost2" });

        Assert.Contains(reply, m => m.Contains("\"type\":\"protocol.error\"") && m.Contains("not online"));
    }

    [Fact]
    public async Task InvitingAPlayerOnAnotherMap_ReachesThem()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "a3", "Ayla", mapId: 13);
        var (_, borinInbox) = await JoinAsync(hub, "b3", "Borin", mapId: 14);

        var reply = await SendAsync(ayla, WebMessageTypes.PartyInvite, 5,
            new { networkId = "player:char-b3" });

        Assert.Contains(reply, m => m.Contains("\"type\":\"party.inviteSent\""));
        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"party.invited\""));
    }

    [Fact]
    public async Task InvitingAPlayerByName_ReachesThem()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "a9", "Ayla", mapId: 13);
        var (_, borinInbox) = await JoinAsync(hub, "b9", "Borin", mapId: 14);

        var reply = await SendAsync(ayla, WebMessageTypes.PartyInvite, 5, new { name = "borin" });

        Assert.Contains(reply, m => m.Contains("\"type\":\"party.inviteSent\"") && m.Contains("player:char-b9"));
        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"party.invited\""));
    }

    [Fact]
    public async Task DecliningAnInvite_NotifiesTheInviterAndLeavesNoParty()
    {
        var hub = new PlaySessionHub();
        var (ayla, aylaInbox) = await JoinAsync(hub, "a4", "Ayla");
        var (borin, _) = await JoinAsync(hub, "b4", "Borin");

        await SendAsync(ayla, WebMessageTypes.PartyInvite, 5, new { networkId = "player:char-b4" });
        var declineReply = await SendAsync(borin, WebMessageTypes.PartyDecline, 6, new { });

        Assert.Contains(declineReply, m => m.Contains("\"type\":\"party.roster\"") && m.Contains("\"members\":[]"));
        Assert.Contains(aylaInbox, m => m.Contains("\"type\":\"party.declined\"") && m.Contains("player:char-b4"));
    }

    [Fact]
    public async Task LeavingA_TwoPersonParty_DisbandsItAndTellsTheOtherMember()
    {
        var hub = new PlaySessionHub();
        var (ayla, aylaInbox) = await JoinAsync(hub, "a5", "Ayla");
        var (borin, _) = await JoinAsync(hub, "b5", "Borin");
        await SendAsync(ayla, WebMessageTypes.PartyInvite, 5, new { networkId = "player:char-b5" });
        await SendAsync(borin, WebMessageTypes.PartyAccept, 6, new { });
        aylaInbox.Clear();

        var leaveReply = await SendAsync(borin, WebMessageTypes.PartyLeave, 7, new { });

        Assert.Contains(leaveReply, m => m.Contains("\"type\":\"party.roster\"") && m.Contains("\"members\":[]"));
        Assert.Contains(aylaInbox, m => m.Contains("\"type\":\"party.roster\"") && m.Contains("\"members\":[]"));
    }

    [Fact]
    public async Task LeaderLeavingAThreePersonParty_HandsLeadershipToTheOldestRemainingMember()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "a6", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, "b6", "Borin");
        var (cara, _) = await JoinAsync(hub, "c6", "Cara");
        await SendAsync(ayla, WebMessageTypes.PartyInvite, 5, new { networkId = "player:char-b6" });
        await SendAsync(borin, WebMessageTypes.PartyAccept, 6, new { });
        await SendAsync(ayla, WebMessageTypes.PartyInvite, 7, new { networkId = "player:char-c6" });
        await SendAsync(cara, WebMessageTypes.PartyAccept, 8, new { });
        borinInbox.Clear();

        await SendAsync(ayla, WebMessageTypes.PartyLeave, 9, new { });

        var roster = Assert.Single(borinInbox, m => m.Contains("\"type\":\"party.roster\""));
        Assert.Contains("player:char-b6\",\"name\":\"Borin\",\"isLeader\":true", roster);
        Assert.DoesNotContain("player:char-a6", roster);
    }

    [Fact]
    public async Task LeaderCanKickAMember_WhoIsToldSeparatelyFromTheSurvivors()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "a7", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, "b7", "Borin");
        var (cara, _) = await JoinAsync(hub, "c7", "Cara");
        await SendAsync(ayla, WebMessageTypes.PartyInvite, 5, new { networkId = "player:char-b7" });
        await SendAsync(borin, WebMessageTypes.PartyAccept, 6, new { });
        await SendAsync(ayla, WebMessageTypes.PartyInvite, 7, new { networkId = "player:char-c7" });
        await SendAsync(cara, WebMessageTypes.PartyAccept, 8, new { });
        borinInbox.Clear();

        var kickReply = await SendAsync(ayla, WebMessageTypes.PartyKick, 9,
            new { networkId = "player:char-c7" });

        Assert.Contains(kickReply, m => m.Contains("\"type\":\"party.roster\"") && !m.Contains("player:char-c7"));
        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"party.roster\"") && !m.Contains("player:char-c7"));
    }

    [Fact]
    public async Task ANonLeaderCannotKickAnyone()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "a8", "Ayla");
        var (borin, _) = await JoinAsync(hub, "b8", "Borin");
        var (cara, _) = await JoinAsync(hub, "c8", "Cara");
        await SendAsync(ayla, WebMessageTypes.PartyInvite, 5, new { networkId = "player:char-b8" });
        await SendAsync(borin, WebMessageTypes.PartyAccept, 6, new { });
        await SendAsync(ayla, WebMessageTypes.PartyInvite, 7, new { networkId = "player:char-c8" });
        await SendAsync(cara, WebMessageTypes.PartyAccept, 8, new { });

        var reply = await SendAsync(borin, WebMessageTypes.PartyKick, 9, new { networkId = "player:char-c8" });

        Assert.Contains(reply, m => m.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task Invite_ReachesTheNewestSessionOfACharacter_NotAStaleOneLeftOnAnotherMap()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "a9", "Ayla");
        await JoinAsync(hub, "b9", "Borin", mapId: 14);
        var (_, borinInbox) = await JoinAsync(hub, "b9", "Borin");

        var responses = await SendAsync(ayla, WebMessageTypes.PartyInvite, 5,
            new { networkId = "player:char-b9" });

        Assert.DoesNotContain(responses, m => m.Contains("\"type\":\"protocol.error\""));
        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"party.invited\""));
    }

    private static async Task<(PlaySession Session, List<string> Inbox)> JoinAsync(
        PlaySessionHub hub, string guid, string name, ushort mapId = 13)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([Character(guid, name, mapId)]), parties: TestSessions.PartiesOf(hub));
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

    private static CharacterRecord Character(string guid, string name, ushort mapId) => new(
        $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, mapId, 1, 2, 3, 4, [100, 200]);
}
