using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PartyChatTests
{
    [Fact]
    public async Task PartyMessage_ReachesEveryCurrentMember_ButNotANonMember()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "pa1", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, "pb1", "Borin");
        var (_, caraInbox) = await JoinAsync(hub, "pc1", "Cara");
        await FormPartyAsync(ayla, borin, "pb1");

        await SendAsync(hub, ayla, WebMessageTypes.PlayerChat, 7, new ChatPayload("party", "form up"));

        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"chat.received\"")
            && m.Contains("\"channel\":\"party\"") && m.Contains("form up"));
        Assert.DoesNotContain(caraInbox, m => m.Contains("form up"));
    }

    [Fact]
    public async Task LeavingTheParty_StopsFurtherPartyChatDelivery()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "pa3", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, "pb3", "Borin");
        await FormPartyAsync(ayla, borin, "pb3");

        await SendAsync(hub, borin, WebMessageTypes.PartyLeave, 7, new { });
        borinInbox.Clear();
        await SendAsync(hub, ayla, WebMessageTypes.PlayerChat, 8, new ChatPayload("party", "still there?"));

        Assert.DoesNotContain(borinInbox, m => m.Contains("still there?"));
    }

    [Fact]
    public async Task PartyMessage_IsRefusedWhenTheSenderIsInNoParty()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "pa4", "Ayla");

        var responses = await SendAsync(hub, ayla, WebMessageTypes.PlayerChat, 5, new ChatPayload("party", "hello?"));

        Assert.Contains(responses, m => m.Contains("\"type\":\"protocol.error\""));
    }

    private static async Task FormPartyAsync(PlaySession inviter, PlaySession invitee, string inviteeGuid)
    {
        await inviter.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PartyInvite, 5, new { networkId = $"player:char-{inviteeGuid}" })));
        await invitee.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PartyAccept, 6, new { })));
    }

    private static async Task<(PlaySession Session, List<string> Inbox)> JoinAsync(
        PlaySessionHub hub, string guid, string name, ushort mapId = 13)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([Character(guid, name, mapId)]), parties: TestSessions.PartiesOf(hub));
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });

        await SendRawAsync(hub, session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
        await SendRawAsync(hub, session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name));

        return (session, inbox);
    }

    private static async Task<IReadOnlyList<string>> SendAsync(PlaySessionHub hub, PlaySession session, string type, int sequence, object payload) =>
        await SendRawAsync(hub, session, type, sequence, payload);

    private static async Task<IReadOnlyList<string>> SendRawAsync(
        PlaySessionHub hub, PlaySession session, string type, int sequence, object payload)
    {
        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
        var presence = await hub.BroadcastAsync(session, responses);
        return [.. responses, .. presence];
    }

    private static CharacterRecord Character(string guid, string name, ushort mapId) => new(
        $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, mapId, 1, 2, 3, 4, [100, 200]);
}
