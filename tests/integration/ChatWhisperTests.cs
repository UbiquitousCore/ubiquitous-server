using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class ChatWhisperTests
{
    [Fact]
    public async Task Whisper_ReachesTheNamedRecipientOnADifferentMap()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "wa1", "Ayla", mapId: 13);
        var (_, borinInbox) = await JoinAsync(hub, "wb1", "Borin", mapId: 42);

        await SendAsync(hub, ayla, WebMessageTypes.ChatWhisper, 5, new ChatWhisperPayload("player:char-wb1", "hey there"));

        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"chat.whisperReceived\"")
            && m.Contains("\"senderName\":\"Ayla\"") && m.Contains("hey there"));
    }

    [Fact]
    public async Task Whisper_DoesNotReachAnyoneElseOnTheSenderOwnMap()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "wa2", "Ayla");
        var (_, borinInbox) = await JoinAsync(hub, "wb2", "Borin");
        var (_, caraInbox) = await JoinAsync(hub, "wc2", "Cara");

        await SendAsync(hub, ayla, WebMessageTypes.ChatWhisper, 5, new ChatWhisperPayload("player:char-wb2", "just for you"));

        Assert.Contains(borinInbox, m => m.Contains("just for you"));
        Assert.DoesNotContain(caraInbox, m => m.Contains("just for you"));
    }

    [Fact]
    public async Task Whisper_EchoesASentConfirmationBackToTheSender()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "wa3", "Ayla");
        await JoinAsync(hub, "wb3", "Borin");

        var responses = await SendAsync(hub, ayla, WebMessageTypes.ChatWhisper, 5, new ChatWhisperPayload("player:char-wb3", "hi"));

        Assert.Contains(responses, m => m.Contains("\"type\":\"chat.whisperSent\"")
            && m.Contains("\"targetName\":\"Borin\"") && m.Contains("hi"));
    }

    [Fact]
    public async Task Whisper_ToYourselfIsRefused()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "wa4", "Ayla");

        var responses = await SendAsync(hub, ayla, WebMessageTypes.ChatWhisper, 5, new ChatWhisperPayload("player:char-wa4", "hi me"));

        Assert.Contains(responses, m => m.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task Whisper_ToAnUnknownOrOfflineTargetIsRefusedRatherThanSilentlyDropped()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "wa5", "Ayla");

        var responses = await SendAsync(hub, ayla, WebMessageTypes.ChatWhisper, 5, new ChatWhisperPayload("player:char-nobody", "hi?"));

        Assert.Contains(responses, m => m.Contains("\"type\":\"protocol.error\""));
    }

    private static async Task<(PlaySession Session, List<string> Inbox)> JoinAsync(
        PlaySessionHub hub, string guid, string name, ushort mapId = 13)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([Character(guid, name, mapId)]));
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
