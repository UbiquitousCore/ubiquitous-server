using Ubiquitous.Data;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PresenceTests
{
    [Fact]
    public async Task EnteringAMap_SpawnsForEveryoneAlreadyThere_AndTellsTheEntrantAboutThem()
    {
        var hub = new PlaySessionHub();
        var (first, firstInbox) = await JoinAsync(hub, "a", "Ayla");

        var (_, secondInbox) = await JoinAsync(hub, "b", "Borin");

        Assert.Contains(secondInbox.Direct, message => message.Contains("\"type\":\"entity.spawned\"")
            && message.Contains("player:char-a"));
        Assert.Contains(firstInbox.Forwarded, message => message.Contains("\"type\":\"entity.spawned\"")
            && message.Contains("player:char-b"));
    }

    [Fact]
    public async Task ASpawnedPlayerCarriesNoEntry_SoTheClientReadsItAsAPlayerNotACreature()
    {
        var hub = new PlaySessionHub();
        await JoinAsync(hub, "a", "Ayla");

        var (_, secondInbox) = await JoinAsync(hub, "b", "Borin");

        var spawn = Assert.Single(secondInbox.Direct, message => message.Contains("player:char-a"));
        Assert.DoesNotContain("\"entry\"", spawn);
        Assert.Contains("\"name\":\"Ayla\"", spawn);
    }

    [Fact]
    public async Task PlayersOnDifferentMapsNeverSeeEachOther()
    {
        var hub = new PlaySessionHub();
        var (_, firstInbox) = await JoinAsync(hub, "a", "Ayla", mapId: 13);

        var (_, secondInbox) = await JoinAsync(hub, "b", "Borin", mapId: 14);

        Assert.DoesNotContain(secondInbox.Direct, message => message.Contains("player:char-a"));
        Assert.DoesNotContain(firstInbox.Forwarded, message => message.Contains("player:char-b"));
    }

    [Fact]
    public async Task MovingOnePlayer_IsStillBroadcastToTheOtherAlongsidePresence()
    {
        var hub = new PlaySessionHub();
        var (first, _) = await JoinAsync(hub, "a", "Ayla");
        var (_, secondInbox) = await JoinAsync(hub, "b", "Borin");
        secondInbox.Forwarded.Clear();

        await SendAsync(hub, first, WebMessageTypes.PlayerMove, 5,
            new PlayerMovePayload(5, 2, 3, 0, true, MovementState.Walking));

        Assert.Contains(secondInbox.Forwarded, message => message.Contains("\"type\":\"entity.moved\"")
            && message.Contains("player:char-a"));
    }

    [Fact]
    public async Task TravellingToAnotherMap_MovesPresenceWithoutAnySpecialCasing()
    {
        var hub = new PlaySessionHub();
        var (first, _) = await JoinAsync(hub, "a", "Ayla");
        var (_, secondInbox) = await JoinAsync(hub, "b", "Borin");
        secondInbox.Forwarded.Clear();

        await SendAsync(hub, first, WebMessageTypes.BusConfirm, 6, new { from = 1, to = 3 });

        Assert.Contains(secondInbox.Forwarded, message => message.Contains("\"type\":\"entity.removed\"")
            && message.Contains("player:char-a"));
        Assert.Equal((ushort)14, first.CurrentMapId);
    }

    [Fact]
    public async Task LeavingRemovesThePlayerFromWhoeverCouldSeeThem()
    {
        var hub = new PlaySessionHub();
        var (first, _) = await JoinAsync(hub, "a", "Ayla");
        var (_, secondInbox) = await JoinAsync(hub, "b", "Borin");
        secondInbox.Forwarded.Clear();

        await hub.DisconnectAsync(first);

        Assert.Contains(secondInbox.Forwarded, message => message.Contains("\"type\":\"entity.removed\"")
            && message.Contains("player:char-a"));
    }

    [Fact]
    public async Task AnObserverSeesAnotherPlayersHealthDropInCombat()
    {
        var hub = new PlaySessionHub();
        var (first, _) = await JoinWithACreatureAsync(hub, "a", "Ayla");
        var (_, secondInbox) = await JoinAsync(hub, "b", "Borin");
        secondInbox.Forwarded.Clear();

        await SendAsync(hub, first, WebMessageTypes.AttackEngage, 5, new { targetId = "Creature:1" });
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        await hub.TickAsync(session => session.TickCombat(now));
        await hub.TickAsync(session => session.TickCombat(now + TimeSpan.FromMilliseconds(700)));

        Assert.Contains(secondInbox.Forwarded, message => message.Contains("\"type\":\"entity.updated\"")
            && message.Contains("player:char-a"));
    }

    [Fact]
    public async Task AnObserverSeesAnotherPlayersEquipmentChange()
    {
        var hub = new PlaySessionHub();
        var (first, _) = await JoinAsync(hub, "a", "Ayla");
        var (_, secondInbox) = await JoinAsync(hub, "b", "Borin");
        secondInbox.Forwarded.Clear();

        await SendAsync(hub, first, WebMessageTypes.InventoryEquip, 5, new
        {
            itemId = $"worn-{EquipmentSlots.Head}-{WornOnTheHead}",
            from = new { area = "equipment", index = EquipmentSlots.Head },
            equip = false,
        });

        Assert.Contains(secondInbox.Forwarded, message => message.Contains("\"type\":\"entity.appearanceChanged\"")
            && message.Contains("player:char-a"));
    }

    [Fact]
    public async Task LeavingBeforeEverEnteringAWorldDoesNothing()
    {
        var hub = new PlaySessionHub();
        var session = TestSessions.Create(new DevelopmentAccountStore("z", "p"), new InMemoryCharacterStore([]));
        hub.Register(session, _ => Task.CompletedTask);

        await hub.DisconnectAsync(session);
    }

    [Fact]
    public async Task APlayerIsNearOnlyOnTheirOwnMapAndWithinReach()
    {
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, "a", "Ayla", mapId: 13);
        var standing = ayla.PlayerPosition!.Value;

        Assert.True(hub.IsPlayerNear(13, standing with { X = standing.X + 30 }, 40));
        Assert.False(hub.IsPlayerNear(13, standing with { X = standing.X + 50 }, 40));
        Assert.False(hub.IsPlayerNear(14, standing, 40));
    }

    [Fact]
    public async Task ACharacterAModulePlaysIsNoPlayerNearby()
    {
        var hub = new PlaySessionHub();
        var (bot, _) = await JoinAsync(hub, "a", "Ayla", 13, spawns: [], playedByModule: true);

        Assert.False(hub.IsPlayerNear(13, bot.PlayerPosition!.Value, 40));
    }

    private sealed record Inbox(List<string> Direct, List<string> Forwarded);

    private static Task<(PlaySession Session, Inbox Inbox)> JoinAsync(
        PlaySessionHub hub, string guid, string name, ushort mapId = 13) =>
        JoinAsync(hub, guid, name, mapId, spawns: []);

    private static Task<(PlaySession Session, Inbox Inbox)> JoinWithACreatureAsync(
        PlaySessionHub hub, string guid, string name, ushort mapId = 13) =>
        JoinAsync(hub, guid, name, mapId, spawns: [new(1001, mapId, 9001, 2, 2, 4, 0)]);

    private static async Task<(PlaySession Session, Inbox Inbox)> JoinAsync(
        PlaySessionHub hub, string guid, string name, ushort mapId, IReadOnlyList<Ubiquitous.Data.CreatureSpawnDefinition> spawns,
        bool playedByModule = false)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([Character(guid, name, mapId)]), spawns);
        var inbox = new Inbox([], []);
        hub.Register(session, message => { inbox.Forwarded.Add(message); return Task.CompletedTask; }, playedByModule);

        await SendRawAsync(hub, session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
        inbox.Direct.AddRange(await SendRawAsync(hub, session, WebMessageTypes.CharactersChoose, 1,
            new ChooseCharacterRequest(name)));

        return (session, inbox);
    }

    private static async Task SendAsync(PlaySessionHub hub, PlaySession session, string type, int sequence, object payload) =>
        await SendRawAsync(hub, session, type, sequence, payload);

    private static async Task<IReadOnlyList<string>> SendRawAsync(
        PlaySessionHub hub, PlaySession session, string type, int sequence, object payload)
    {
        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
        var presence = await hub.BroadcastAsync(session, responses);
        return [.. responses, .. presence];
    }

    private const int WornOnTheHead = 100;

    private static CharacterRecord Character(string guid, string name, ushort mapId) => new(
        $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, mapId, 1, 2, 3, 4,
        [WornOnTheHead, 200]);
}
