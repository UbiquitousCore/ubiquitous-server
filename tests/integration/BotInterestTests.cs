using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class BotInterestTests
{
    [Fact]
    public async Task APlayersStep_ReachesABotNearbyAndAPersonAnywhere_ButNotABotOutOfSight()
    {
        var hub = new PlaySessionHub();
        var (walker, _) = await JoinAsync(hub, "a", "Ayla", x: 1, playedByModule: false);
        var (_, nearBot) = await JoinAsync(hub, "b", "Borin", x: 20, playedByModule: true);
        var (_, farBot) = await JoinAsync(hub, "c", "Cedra", x: 300, playedByModule: true);
        var (_, farPerson) = await JoinAsync(hub, "d", "Dunn", x: 300, playedByModule: false);
        nearBot.Clear();
        farBot.Clear();
        farPerson.Clear();

        await SendAsync(hub, walker, WebMessageTypes.PlayerMove, 2,
            new PlayerMovePayload(1.5, 2, 3, 0, true, MovementState.Walking));

        static bool Moved(string message) =>
            message.Contains("\"type\":\"entity.moved\"") && message.Contains("player:char-a");
        Assert.Contains(nearBot, Moved);
        Assert.Contains(farPerson, Moved);
        Assert.DoesNotContain(farBot, Moved);
    }

    private static async Task<(PlaySession Session, List<string> Forwarded)> JoinAsync(
        PlaySessionHub hub, string guid, string name, float x, bool playedByModule)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([new CharacterRecord(
                $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, x, 2, 3, 4, [100, 200])]));
        var forwarded = new List<string>();
        hub.Register(session, message => { forwarded.Add(message); return Task.CompletedTask; }, playedByModule);

        await SendAsync(hub, session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
        await SendAsync(hub, session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name));
        return (session, forwarded);
    }

    private static async Task SendAsync(PlaySessionHub hub, PlaySession session, string type, int sequence, object payload)
    {
        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
        await hub.BroadcastAsync(session, responses);
    }
}
