using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class IgnoreTests
{
    [Fact]
    public async Task Ignoring_SomeoneDropsThemFromFriends()
    {
        var hub = new PlaySessionHub();
        var characters = new InMemoryCharacterStore([Character("ia1", "Ayla"), Character("ib1", "Borin")]);
        var (ayla, _) = await JoinAsync(hub, characters, "ia1", "Ayla");
        await JoinAsync(hub, characters, "ib1", "Borin");

        await SendAsync(hub, ayla, WebMessageTypes.SocialFriendAdd, 5, new SocialTargetPayload(Name: "Borin"));
        var ignored = await SendAsync(hub, ayla, WebMessageTypes.SocialIgnoreAdd, 6, new SocialTargetPayload(Name: "Borin"));

        Assert.Contains(ignored, m => m.Contains("\"friends\":[]"));
        Assert.Contains(ignored, m => m.Contains("\"name\":\"Borin\""));
    }

    [Fact]
    public async Task Ignored_WhispersNeverArriveButTheSenderStillSeesThemSent()
    {
        var hub = new PlaySessionHub();
        var characters = new InMemoryCharacterStore([Character("ia2", "Ayla"), Character("ib2", "Borin")]);
        var (ayla, _) = await JoinAsync(hub, characters, "ia2", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, characters, "ib2", "Borin");

        await SendAsync(hub, borin, WebMessageTypes.SocialIgnoreAdd, 5, new SocialTargetPayload("player:char-ia2"));
        var responses = await SendAsync(hub, ayla, WebMessageTypes.ChatWhisper, 6,
            new ChatWhisperPayload("player:char-ib2", "can you hear me"));

        Assert.Contains(responses, m => m.Contains("\"type\":\"chat.whisperSent\""));
        Assert.DoesNotContain(borinInbox, m => m.Contains("can you hear me"));
    }

    [Fact]
    public async Task UnIgnoring_ByNameRestoresWhispers()
    {
        var hub = new PlaySessionHub();
        var characters = new InMemoryCharacterStore([Character("ia3", "Ayla"), Character("ib3", "Borin")]);
        var (ayla, _) = await JoinAsync(hub, characters, "ia3", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, characters, "ib3", "Borin");

        await SendAsync(hub, borin, WebMessageTypes.SocialIgnoreAdd, 5, new SocialTargetPayload("player:char-ia3"));
        await SendAsync(hub, borin, WebMessageTypes.SocialIgnoreRemove, 6, new SocialTargetPayload(Name: "Ayla"));
        await SendAsync(hub, ayla, WebMessageTypes.ChatWhisper, 6, new ChatWhisperPayload("player:char-ib3", "now?"));

        Assert.Contains(borinInbox, m => m.Contains("now?"));
    }

    [Fact]
    public async Task Ignored_SayNeverArrives()
    {
        var hub = new PlaySessionHub();
        var characters = new InMemoryCharacterStore([Character("ia4", "Ayla"), Character("ib4", "Borin")]);
        var (ayla, _) = await JoinAsync(hub, characters, "ia4", "Ayla", x: 0f);
        var (borin, borinInbox) = await JoinAsync(hub, characters, "ib4", "Borin", x: 5f);

        await SendAsync(hub, borin, WebMessageTypes.SocialIgnoreAdd, 5, new SocialTargetPayload("player:char-ia4"));
        await SendAsync(hub, ayla, WebMessageTypes.PlayerChat, 6, new ChatPayload("say", "hello nearby"));

        Assert.DoesNotContain(borinInbox, m => m.Contains("hello nearby"));
    }

    [Fact]
    public async Task Ignoring_ATeamMemberKeepsTheTeamButSilencesTheirPartyChat()
    {
        var hub = new PlaySessionHub();
        var characters = new InMemoryCharacterStore(
            [Character("ia5", "Ayla"), Character("ib5", "Borin"), Character("ic5", "Cara")]);
        var (ayla, _) = await JoinAsync(hub, characters, "ia5", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, characters, "ib5", "Borin");
        var (cara, caraInbox) = await JoinAsync(hub, characters, "ic5", "Cara");
        await FormPartyAsync(ayla, borin, "ib5");
        await FormPartyAsync(ayla, cara, "ic5");

        await SendAsync(hub, cara, WebMessageTypes.SocialIgnoreAdd, 7, new SocialTargetPayload("player:char-ia5"));
        await SendAsync(hub, ayla, WebMessageTypes.PlayerChat, 8, new ChatPayload("party", "form up"));

        Assert.DoesNotContain(caraInbox, m => m.Contains("form up"));
        Assert.Contains(borinInbox, m => m.Contains("form up"));
    }

    private static async Task FormPartyAsync(PlaySession inviter, PlaySession invitee, string inviteeGuid)
    {
        await inviter.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PartyInvite, 5, new { networkId = $"player:char-{inviteeGuid}" })));
        await invitee.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PartyAccept, 6, new { })));
    }

    private static async Task<(PlaySession Session, List<string> Inbox)> JoinAsync(
        PlaySessionHub hub, InMemoryCharacterStore characters, string guid, string name, ushort mapId = 13, float x = 0f)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"), characters,
            parties: TestSessions.PartiesOf(hub));
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

    private static CharacterRecord Character(string guid, string name, ushort mapId = 13, float x = 0f) => new(
        $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, mapId, x, 2, 3, 4, [100, 200]);
}
