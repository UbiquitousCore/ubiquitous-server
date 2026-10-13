using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class SharedMapTests
{
    [Fact]
    public async Task TwoSessionsOnOneMap_SeeTheSameWireIdsForTheSameSpawns()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, maps, "shared1", "Ayla");
        var (borin, _) = await JoinAsync(hub, maps, "shared2", "Borin");

        var aylaIds = SpawnedIds(await ChooseAsync(hub, ayla, "Ayla"));
        var borinIds = SpawnedIds(await ChooseAsync(hub, borin, "Borin"));

        Assert.Equal(["Creature:1"], aylaIds);
        Assert.Equal(aylaIds, borinIds);
    }

    [Fact]
    public async Task TwoSessionsOnOneMap_AgreeOnWanderDestinations()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, maps, "shared3", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, maps, "shared4", "Borin");
        await ChooseAsync(hub, ayla, "Ayla");
        await ChooseAsync(hub, borin, "Borin");

        var tick = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        ayla.TickCombat(tick);
        borin.TickCombat(tick);
        var at = tick + CreatureWanderService.PauseDuration;
        var aylaMoves = ayla.TickCombat(at);
        await hub.BroadcastAsync(ayla, aylaMoves);
        borin.TickCombat(at);

        var aylaTarget = TargetOf(aylaMoves);
        var borinTarget = TargetOf(borinInbox);
        Assert.NotNull(aylaTarget);
        Assert.Equal(aylaTarget, borinTarget);
    }

    [Fact]
    public async Task KillInOneSession_IsDeadInTheOther()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, maps, "shared5", "Ayla");
        var (borin, _) = await JoinAsync(hub, maps, "shared6", "Borin");
        await ChooseAsync(hub, ayla, "Ayla");
        await ChooseAsync(hub, borin, "Borin");

        await SendAsync(hub, ayla, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        for (var swing = 0; swing < 20; swing++)
        {
            var responses = ayla.TickCombat(now).ToList();
            await hub.BroadcastAsync(ayla, responses);
            if (responses.Any(m => m.Contains("unit.died"))) break;
            now += TimeSpan.FromSeconds(2);
        }

        var reattack = await SendAsync(hub, borin, WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:1" });
        Assert.Contains(reattack, m => m.Contains("protocol.error") && m.Contains("already dead"));

        var later = now + TimeSpan.FromSeconds(10);
        borin.TickCombat(later);
        var wanderTick = borin.TickCombat(later + CreatureWanderService.PauseDuration);
        Assert.DoesNotContain(wanderTick, m => m.Contains("entity.moved") && m.Contains("Creature:1"));
    }

    [Fact]
    public async Task PlayerRevivesInOneSession_ComesBackAliveInTheOther()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var hub = new PlaySessionHub();
        var wolfSpawns = new[] { new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0, IdleBehaviour: 1, WanderRadius: 10) };
        var (ayla, _) = await JoinAsync(hub, maps, "shared9", "Ayla", wolfSpawns, WolfContent());
        var (borin, borinInbox) = await JoinAsync(hub, maps, "shared10", "Borin", wolfSpawns, WolfContent());
        await ChooseAsync(hub, ayla, "Ayla");
        await ChooseAsync(hub, borin, "Borin");

        await SendAsync(hub, ayla, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });
        var now = DateTimeOffset.UtcNow;
        var dead = false;
        for (var i = 0; i < 30 && !dead; i++)
        {
            now += TimeSpan.FromSeconds(2);
            var tick = ayla.TickCombat(now).ToList();
            await hub.BroadcastAsync(ayla, tick);
            dead = tick.Any(m => m.Contains("unit.died") && m.Contains("player:char-shared9"));
        }
        Assert.True(dead, "expected the wolf to kill Ayla");

        borinInbox.Clear();
        var released = await SendAsync(hub, ayla, WebMessageTypes.PlayerUnstuck, 3, new { });
        Assert.Contains(released, m => m.Contains("player.revived"));

        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"player.revived\"")
            && m.Contains("player:char-shared9"));
    }

    [Fact]
    public async Task LevelingUp_TellsTheOtherSessionTheNewMaxHealth()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9500] = new(9500, "trainingBoss", Level: 11, MaxHealth: 1, MinDamage: 0, MaxDamage: 0,
                    RespawnTimeMinSeconds: 1, RespawnTimeMaxSeconds: 1),
            },
        };
        var spawns = new[] { new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0) };
        var maps = new SharedMapRegistry(() => 0.9);
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, maps, "shared11", "Ayla", spawns, content, level: 1);
        var (borin, borinInbox) = await JoinAsync(hub, maps, "shared12", "Borin", spawns, content);
        await ChooseAsync(hub, ayla, "Ayla");
        await ChooseAsync(hub, borin, "Borin");

        var targetId = "Creature:1";
        var now = DateTimeOffset.UtcNow;
        for (var kill = 1; kill <= 4; kill++)
        {
            await SendAsync(hub, ayla, WebMessageTypes.AttackEngage, 2, new { targetId });
            IReadOnlyList<string> killResponses = [];
            for (var swing = 0; swing < 20; swing++)
            {
                now += TimeSpan.FromSeconds(2);
                killResponses = ayla.TickCombat(now);
                await hub.BroadcastAsync(ayla, killResponses);
                if (killResponses.Any(m => m.Contains("\"type\":\"unit.died\""))) break;
            }

            now += TimeSpan.FromSeconds(151);
            await hub.BroadcastAsync(ayla, ayla.TickCombat(now));
            now += TimeSpan.FromSeconds(2);
            var respawned = ayla.TickCombat(now);
            await hub.BroadcastAsync(ayla, respawned);
            targetId = SpawnedId(respawned) ?? targetId;
        }

        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"entity.updated\"")
            && m.Contains("\"networkId\":\"player:char-shared11\"")
            && m.Contains("\"health\":"));
    }

    [Fact]
    public async Task RespawnGetsOneNewIdForBothSessions()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, maps, "shared7", "Ayla");
        var (borin, _) = await JoinAsync(hub, maps, "shared8", "Borin");
        await ChooseAsync(hub, ayla, "Ayla");
        await ChooseAsync(hub, borin, "Borin");

        await SendAsync(hub, ayla, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        for (var swing = 0; swing < 20; swing++)
        {
            var responses = ayla.TickCombat(now).ToList();
            await hub.BroadcastAsync(ayla, responses);
            if (responses.Any(m => m.Contains("unit.died"))) break;
            now += TimeSpan.FromSeconds(2);
        }

        string? aylaId = null;
        string? borinId = null;
        for (var step = 0; step < 45 && (aylaId is null || borinId is null); step++)
        {
            now += TimeSpan.FromSeconds(5);
            aylaId ??= SpawnedId(ayla.TickCombat(now));
            await hub.BroadcastAsync(ayla, []);
            borinId ??= SpawnedId(borin.TickCombat(now));
        }

        Assert.NotNull(aylaId);
        Assert.NotNull(borinId);
        Assert.NotEqual("Creature:1", aylaId);
        Assert.Equal(aylaId, borinId);
    }

    private static readonly CreatureSpawnDefinition[] Spawns =
        [new(1001, 13, 9001, 2, 2, 4, 0, IdleBehaviour: 1, WanderRadius: 10)];

    private static GameContent WolfContent() => GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9500] = new(9500, "wolf", 3, 100, MinDamage: 50, MaxDamage: 60,
                FactionType: UnitFactionTypes.Hostile),
        },
    };

    private static async Task<(PlaySession Session, List<string> Inbox)> JoinAsync(
        PlaySessionHub hub, SharedMapRegistry maps, string guid, string name,
        IReadOnlyList<CreatureSpawnDefinition>? spawns = null, GameContent? content = null, int? level = null)
    {
        var character = level is int atLevel ? Character(guid, name) with { Level = atLevel } : Character(guid, name);
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([character]),
            spawns ?? Spawns, content: content ?? GameContent.Development,
            combatRollUnit: () => 1.0, sharedMaps: maps);
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });
        await SendRawAsync(hub, session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
        return (session, inbox);
    }

    private static async Task<IReadOnlyList<string>> ChooseAsync(PlaySessionHub hub, PlaySession session, string name) =>
        await SendRawAsync(hub, session, WebMessageTypes.CharactersChoose, 1,
            new ChooseCharacterRequest(name));

    private static List<string> SpawnedIds(IReadOnlyList<string> responses) =>
        responses.Where(m => m.Contains("\"type\":\"entity.spawned\""))
            .Select(m => NetworkIdOf(m)).OfType<string>()
            .Where(id => id.StartsWith("Creature:", StringComparison.Ordinal))
            .Order().ToList();

    private static string? SpawnedId(IReadOnlyList<string> responses) =>
        responses.Where(m => m.Contains("\"type\":\"entity.spawned\""))
            .Select(m => NetworkIdOf(m)).FirstOrDefault(id => id is not null);

    private static string? NetworkIdOf(string message)
    {
        const string marker = "\"networkId\":\"";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        var end = message.IndexOf('"', start);
        return end < 0 ? null : message[start..end];
    }

    private static string? TargetOf(IReadOnlyList<string> responses)
    {
        const string marker = "\"target\":";
        var move = responses.FirstOrDefault(m => m.Contains("\"type\":\"entity.moved\"") && m.Contains(marker));
        if (move is null) return null;
        var start = move.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = move.IndexOf('}', start);
        return move[start..(end < 0 ? move.Length : end + 1)];
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

    private static CharacterRecord Character(string guid, string name) => new(
        $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);
}
