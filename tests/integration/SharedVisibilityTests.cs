using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class SharedVisibilityTests
{
    [Fact]
    public async Task WalkerLeavingRange_DoesNotDespawnCreaturesForThoseStaying()
    {
        var maps = new SharedMapRegistry();
        var hub = new PlaySessionHub();
        var (ayla, aylaInbox) = Join(hub, maps, "vis-ayla", "Ayla", "char-vis-ayla");
        var (borin, borinInbox) = Join(hub, maps, "vis-borin", "Borin", "char-vis-borin");
        var aylaEntered = await EnterAsync(hub, ayla, aylaInbox, "vis-ayla", "Ayla");
        var borinEntered = await EnterAsync(hub, borin, borinInbox, "vis-borin", "Borin");

        Assert.Equal(CreatureIds(aylaEntered), CreatureIds(borinEntered));
        Assert.Equal(["Creature:1", "Creature:2"], CreatureIds(borinEntered));

        aylaInbox.Clear();
        borinInbox.Clear();

        IReadOnlyList<string> last = [];
        for (var step = 1; step <= 5; step++)
            last = await MoveAsync(hub, ayla, aylaInbox, 1 + 15 * step, 2, 3);
        Assert.Contains(last, message =>
            message.Contains("\"type\":\"entity.removed\"") && message.Contains("Creature:"));

        Assert.Contains(borinInbox, message =>
            message.Contains("\"type\":\"entity.moved\"") && message.Contains("player:"));
        Assert.DoesNotContain(borinInbox, message =>
            message.Contains("\"type\":\"entity.removed\"")
            && (message.Contains("Creature:") || message.Contains("StaticObject:")));

        var reattack = await borin.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 9, new { targetId = "Creature:2" })));
        await hub.BroadcastAsync(borin, reattack);
        Assert.DoesNotContain(string.Concat(reattack), "invalid_target");
    }

    private static (PlaySession Session, List<string> Inbox) Join(
        PlaySessionHub hub, SharedMapRegistry maps, string user, string name, string guid)
    {
        var session = TestSessions.Create(
            new DevelopmentAccountStore(user, "p"),
            new InMemoryCharacterStore([Character(guid, name)]),
            TravelSession.Spawns, TravelSession.StaticSpawns, GameContent.Development,
            sharedMaps: maps);
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });
        return (session, inbox);
    }

    private static async Task<IReadOnlyList<string>> EnterAsync(
        PlaySessionHub hub, PlaySession session, List<string> inbox, string user, string name)
    {
        var login = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest(user, "p"))));
        inbox.AddRange(login);
        await hub.BroadcastAsync(session, login);
        var entered = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name))));
        inbox.AddRange(entered);
        inbox.AddRange(await hub.BroadcastAsync(session, entered));
        return entered;
    }

    private static async Task<IReadOnlyList<string>> MoveAsync(
        PlaySessionHub hub, PlaySession session, List<string> inbox, float x, float y, float z)
    {
        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 5,
            new { x, y, z, yaw = 0f, moving = true, state = 0, speed = 7f })));
        inbox.AddRange(responses);
        inbox.AddRange(await hub.BroadcastAsync(session, responses));
        return responses;
    }

    private static List<string> CreatureIds(IReadOnlyList<string> responses) =>
        responses.Where(message => message.Contains("\"type\":\"entity.spawned\""))
            .Select(NetworkIdOf).OfType<string>()
            .Where(id => id.StartsWith("Creature:", StringComparison.Ordinal))
            .Order().ToList();

    private static string? NetworkIdOf(string message)
    {
        const string marker = "\"networkId\":\"";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        var end = message.IndexOf('"', start);
        return end < 0 ? null : message[start..end];
    }

    private static CharacterRecord Character(string guid, string name) => new(
        guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);
}
