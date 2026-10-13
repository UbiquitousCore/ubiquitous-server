using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class EmoteTests
{
    [Fact]
    public async Task AnEmote_BroadcastsToAnObserverOnTheSameMap()
    {
        var hub = new PlaySessionHub();
        var (first, _) = await JoinAsync(hub, "a", "Ayla");
        var (_, secondInbox) = await JoinAsync(hub, "b", "Borin");
        secondInbox.Forwarded.Clear();

        var direct = await SendAsync(hub, first, WebMessageTypes.PlayerEmote, 5, new { name = "wave" });

        Assert.Contains(direct, message => message.Contains("\"type\":\"emote.performed\"")
            && message.Contains("player:char-a") && message.Contains("\"name\":\"wave\""));
        Assert.Contains(secondInbox.Forwarded, message => message.Contains("\"type\":\"emote.performed\"")
            && message.Contains("player:char-a") && message.Contains("\"id\":141"));
    }

    [Fact]
    public async Task AnEmote_NeverReachesASessionOnAnotherMap()
    {
        var hub = new PlaySessionHub();
        var (first, _) = await JoinAsync(hub, "a", "Ayla", mapId: 13);
        var (_, secondInbox) = await JoinAsync(hub, "b", "Borin", mapId: 14);
        secondInbox.Forwarded.Clear();

        await SendAsync(hub, first, WebMessageTypes.PlayerEmote, 5, new { name = "cheer" });

        Assert.DoesNotContain(secondInbox.Forwarded, message => message.Contains("emote.performed"));
    }

    [Fact]
    public async Task AnUnknownEmoteName_IsRefusedAndNeverBroadcast()
    {
        var hub = new PlaySessionHub();
        var (first, _) = await JoinAsync(hub, "a", "Ayla");
        var (_, secondInbox) = await JoinAsync(hub, "b", "Borin");
        secondInbox.Forwarded.Clear();

        var direct = await SendAsync(hub, first, WebMessageTypes.PlayerEmote, 5, new { name = "moonwalk" });

        Assert.Contains(direct, message => message.Contains("\"type\":\"protocol.error\""));
        Assert.DoesNotContain(secondInbox.Forwarded, message => message.Contains("emote.performed"));
    }

    [Fact]
    public async Task AnEmote_BeforeChoosingACharacter_IsRefused()
    {
        var hub = new PlaySessionHub();
        var session = TestSessions.Create(new DevelopmentAccountStore("z", "p"), new InMemoryCharacterStore([]));
        hub.Register(session, _ => Task.CompletedTask);

        var direct = await SendAsync(hub, session, WebMessageTypes.PlayerEmote, 0, new { name = "wave" });

        Assert.Contains(direct, message => message.Contains("\"type\":\"protocol.error\""));
    }

    private sealed record Inbox(List<string> Direct, List<string> Forwarded);

    private static async Task<(PlaySession Session, Inbox Inbox)> JoinAsync(
        PlaySessionHub hub, string guid, string name, ushort mapId = 13)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([Character(guid, name, mapId)]));
        var inbox = new Inbox([], []);
        hub.Register(session, message => { inbox.Forwarded.Add(message); return Task.CompletedTask; });

        await SendRawAsync(hub, session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
        inbox.Direct.AddRange(await SendRawAsync(hub, session, WebMessageTypes.CharactersChoose, 1,
            new ChooseCharacterRequest(name)));

        return (session, inbox);
    }

    private static async Task<IReadOnlyList<string>> SendAsync(
        PlaySessionHub hub, PlaySession session, string type, int sequence, object payload) =>
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
