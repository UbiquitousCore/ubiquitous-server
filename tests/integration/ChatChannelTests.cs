using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class ChatChannelTests
{
    [Fact]
    public async Task Say_OnlyReachesAPlayerWithinRange()
    {
        var hub = new PlaySessionHub();
        var (near, _) = await JoinAsync(hub, "a", "Ayla", x: 0f);
        var (_, nearInbox) = await JoinAsync(hub, "b", "Borin", x: 5f);
        var (_, farInbox) = await JoinAsync(hub, "c", "Cara", x: 500f);

        await SendAsync(hub, near, WebMessageTypes.PlayerChat, 5, new ChatPayload("say", "hello nearby"));

        Assert.Contains(nearInbox.Forwarded, message => message.Contains("\"type\":\"chat.received\"")
            && message.Contains("hello nearby"));
        Assert.DoesNotContain(farInbox.Forwarded, message => message.Contains("hello nearby"));
    }

    [Fact]
    public async Task General_ReachesEveryoneOnTheMapRegardlessOfDistance()
    {
        var hub = new PlaySessionHub();
        var (speaker, _) = await JoinAsync(hub, "a", "Ayla", x: 0f);
        var (_, farInbox) = await JoinAsync(hub, "b", "Borin", x: 5000f);

        await SendAsync(hub, speaker, WebMessageTypes.PlayerChat, 5, new ChatPayload("general", "hello map"));

        Assert.Contains(farInbox.Forwarded, message => message.Contains("\"type\":\"chat.received\"")
            && message.Contains("hello map"));
    }

    [Fact]
    public async Task General_NeverReachesAPlayerOnADifferentMap()
    {
        var hub = new PlaySessionHub();
        var (speaker, _) = await JoinAsync(hub, "a", "Ayla", x: 0f, mapId: 13);
        var (_, otherMapInbox) = await JoinAsync(hub, "b", "Borin", x: 0f, mapId: 14);

        await SendAsync(hub, speaker, WebMessageTypes.PlayerChat, 5, new ChatPayload("general", "hello map"));

        Assert.DoesNotContain(otherMapInbox.Forwarded, message => message.Contains("hello map"));
    }

    [Fact]
    public async Task Trade_OnlyReachesSessionsThatHaveJoinedIt()
    {
        var hub = new PlaySessionHub();
        var (speaker, _) = await JoinAsync(hub, "a", "Ayla", x: 0f);
        var (joined, joinedInbox) = await JoinAsync(hub, "b", "Borin", x: 5000f);
        var (_, notJoinedInbox) = await JoinAsync(hub, "c", "Cara", x: 0f);

        await SendAsync(hub, speaker, WebMessageTypes.ChatJoinChannel, 4, new ChatChannelPayload("trade"));
        await SendAsync(hub, joined, WebMessageTypes.ChatJoinChannel, 5, new ChatChannelPayload("trade"));
        await SendAsync(hub, speaker, WebMessageTypes.PlayerChat, 6, new ChatPayload("trade", "wts sword"));

        Assert.Contains(joinedInbox.Forwarded, message => message.Contains("\"type\":\"chat.received\"")
            && message.Contains("wts sword"));
        Assert.DoesNotContain(notJoinedInbox.Forwarded, message => message.Contains("wts sword"));
    }

    [Fact]
    public async Task Trade_MessageIsRefusedUntilTheSenderJoins()
    {
        var hub = new PlaySessionHub();
        var (speaker, speakerInbox) = await JoinAsync(hub, "a", "Ayla");

        var responses = await SendAsync(hub, speaker, WebMessageTypes.PlayerChat, 5, new ChatPayload("trade", "wts sword"));

        Assert.Contains(responses, message => message.Contains("\"type\":\"protocol.error\""));
        Assert.DoesNotContain(speakerInbox.Forwarded, message => message.Contains("wts sword"));
    }

    [Fact]
    public async Task LeavingGeneral_StopsFurtherDelivery()
    {
        var hub = new PlaySessionHub();
        var (speaker, _) = await JoinAsync(hub, "a", "Ayla", x: 0f);
        var (leaver, leaverInbox) = await JoinAsync(hub, "b", "Borin", x: 5000f);

        await SendAsync(hub, leaver, WebMessageTypes.ChatLeaveChannel, 5, new ChatChannelPayload("general"));
        leaverInbox.Forwarded.Clear();
        await SendAsync(hub, speaker, WebMessageTypes.PlayerChat, 6, new ChatPayload("general", "still here?"));

        Assert.DoesNotContain(leaverInbox.Forwarded, message => message.Contains("still here?"));
    }

    [Fact]
    public async Task JoiningAndLeavingSay_IsRefused()
    {
        var hub = new PlaySessionHub();
        var (session, _) = await JoinAsync(hub, "a", "Ayla");

        var joinResponses = await SendAsync(hub, session, WebMessageTypes.ChatJoinChannel, 5, new ChatChannelPayload("say"));
        var leaveResponses = await SendAsync(hub, session, WebMessageTypes.ChatLeaveChannel, 6, new ChatChannelPayload("say"));

        Assert.Contains(joinResponses, message => message.Contains("\"type\":\"protocol.error\""));
        Assert.Contains(leaveResponses, message => message.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task Yell_ReachesAPlayerWithin100Metres_ButNotFurther()
    {
        var hub = new PlaySessionHub();
        var (shouter, _) = await JoinAsync(hub, "a", "Ayla", x: 0f);
        var (_, nearInbox) = await JoinAsync(hub, "b", "Borin", x: 80f);
        var (_, farInbox) = await JoinAsync(hub, "c", "Cara", x: 150f);

        await SendAsync(hub, shouter, WebMessageTypes.PlayerChat, 5, new ChatPayload("yell", "over here"));

        Assert.Single(nearInbox.Forwarded, message => message.Contains("over here"));
        Assert.DoesNotContain(farInbox.Forwarded, message => message.Contains("over here"));
    }

    [Fact]
    public async Task Global_ReachesPlayersOnOtherMaps()
    {
        var hub = new PlaySessionHub();
        var (speaker, _) = await JoinAsync(hub, "a", "Ayla", x: 0f, mapId: 13);
        var (_, otherMapInbox) = await JoinAsync(hub, "b", "Borin", x: 0f, mapId: 14);

        await SendAsync(hub, speaker, WebMessageTypes.PlayerChat, 5, new ChatPayload("global", "hello everyone"));

        Assert.Contains(otherMapInbox.Forwarded, message => message.Contains("\"type\":\"chat.received\"")
            && message.Contains("hello everyone"));
    }

    [Fact]
    public async Task Global_TooManyLines_IsRefusedWithTheGamesWords()
    {
        var hub = new PlaySessionHub();
        var (speaker, _) = await JoinAsync(hub, "a", "Ayla");

        for (var line = 0; line < 3; line++)
            await SendAsync(hub, speaker, WebMessageTypes.PlayerChat, 5 + line, new ChatPayload("global", $"line {line}"));
        var refused = await SendAsync(hub, speaker, WebMessageTypes.PlayerChat, 8, new ChatPayload("global", "one more"));

        Assert.Contains(refused, message => message.Contains("chat_too_frequent")
            && message.Contains("You speak too frequently, please hold on."));
    }

    [Fact]
    public async Task Guild_IsRefusedOutsideAGuild()
    {
        var hub = new PlaySessionHub();
        var (speaker, _) = await JoinAsync(hub, "a", "Ayla");

        var responses = await SendAsync(hub, speaker, WebMessageTypes.PlayerChat, 5, new ChatPayload("guild", "anyone?"));

        Assert.Contains(responses, message => message.Contains("\"type\":\"protocol.error\""));
    }

    private sealed record Inbox(List<string> Direct, List<string> Forwarded);

    private static async Task<(PlaySession Session, Inbox Inbox)> JoinAsync(
        PlaySessionHub hub, string guid, string name, float x = 0f, ushort mapId = 13)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([Character(guid, name, mapId, x)]));
        var inbox = new Inbox([], []);
        hub.Register(session, message => { inbox.Forwarded.Add(message); return Task.CompletedTask; });

        await SendRawAsync(hub, session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
        inbox.Direct.AddRange(await SendRawAsync(hub, session, WebMessageTypes.CharactersChoose, 1,
            new ChooseCharacterRequest(name)));

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

    private static CharacterRecord Character(string guid, string name, ushort mapId, float x) => new(
        $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, mapId, x, 2, 3, 4, [100, 200]);
}
