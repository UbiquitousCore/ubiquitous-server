using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class CreatureHuntTests
{
    private const uint WolfEntry = 9700;
    private const uint RabbitEntry = 9701;

    private static readonly CreatureSpawnDefinition Wolf = new(1001, 13, WolfEntry, 1, 2, 30, 0);
    private static readonly CreatureSpawnDefinition NearRabbit = new(1002, 13, RabbitEntry, 4, 2, 30, 0);

    [Fact]
    public async Task AWolfAttacksARabbitInItsRange_KillsIt_AndWalksHome()
    {
        var hub = new PlaySessionHub();
        var (session, _) = await JoinAsync(hub, new SharedMapRegistry(() => 0.9), "hunt1", [Wolf, NearRabbit]);

        var seen = await RunAsync(hub, session, DateTimeOffset.UtcNow, seconds: 3);

        Assert.Contains(seen, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"attackerId\":\"Creature:1\"") && message.Contains("\"targetId\":\"Creature:2\""));
        Assert.Contains(seen, message => message.Contains("\"type\":\"attack.swingResult\"")
            && message.Contains("\"attackerId\":\"Creature:1\""));
        Assert.Contains(seen, message => message.Contains("\"type\":\"unit.died\"") && message.Contains("\"Creature:2\""));
        Assert.DoesNotContain(seen, message => message.Contains("\"type\":\"entity.lootable\""));
        Assert.DoesNotContain(seen, message => message.Contains("\"type\":\"player.expGained\""));
        Assert.Contains(seen, message => message.Contains("\"type\":\"attack.exitCombat\"")
            && message.Contains("\"Creature:1\""));
    }

    [Fact]
    public async Task ARabbitThatRespawnsInRangeIsHuntedAgain()
    {
        var hub = new PlaySessionHub();
        var (session, _) = await JoinAsync(hub, new SharedMapRegistry(() => 0.9), "hunt2", [Wolf, NearRabbit]);
        var start = DateTimeOffset.UtcNow;
        await RunAsync(hub, session, start, seconds: 3);

        var later = await RunAsync(hub, session, start + TimeSpan.FromSeconds(3), seconds: 60);

        var respawned = later.FirstOrDefault(message => message.Contains("\"type\":\"entity.spawned\"")
            && !message.Contains("\"Creature:1\"") && !message.Contains("\"Creature:2\""));
        Assert.NotNull(respawned);
        var newId = NetworkIdOf(respawned!);
        Assert.Contains(later, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"attackerId\":\"Creature:1\"") && message.Contains($"\"targetId\":\"{newId}\""));
    }

    [Fact]
    public async Task AWolfOutOfRangeLeavesTheRabbitAlone()
    {
        var farRabbit = NearRabbit with { X = 25 };
        var hub = new PlaySessionHub();
        var (session, _) = await JoinAsync(hub, new SharedMapRegistry(() => 0.9), "hunt3", [Wolf, farRabbit]);

        var seen = await RunAsync(hub, session, DateTimeOffset.UtcNow, seconds: 10);

        Assert.DoesNotContain(seen, message => message.Contains("\"type\":\"attack.meleeStart\""));
        Assert.DoesNotContain(seen, message => message.Contains("\"type\":\"unit.died\""));
    }

    [Fact]
    public async Task AWolfAlreadyFightingThePlayerDoesNotTurnOnARabbit()
    {
        var closeWolf = Wolf with { Z = 8 };
        var rabbit = NearRabbit with { Z = 8 };
        var hub = new PlaySessionHub();
        var (session, _) = await JoinAsync(hub, new SharedMapRegistry(() => 0.9), "hunt4", [closeWolf, rabbit]);

        var seen = await RunAsync(hub, session, DateTimeOffset.UtcNow, seconds: 4);

        Assert.Contains(seen, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"attackerId\":\"Creature:1\"") && message.Contains("player:char-hunt4"));
        Assert.DoesNotContain(seen, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"targetId\":\"Creature:2\""));
    }

    [Fact]
    public async Task APlayerWhoAttacksAHunterTakesItOffItsQuarry()
    {
        var hub = new PlaySessionHub();
        var (session, _) = await JoinAsync(hub, new SharedMapRegistry(() => 0.9), "hunt7",
            [Wolf, NearRabbit], toughRabbit: true);
        var start = DateTimeOffset.UtcNow;
        var opening = await RunAsync(hub, session, start, seconds: 1);
        Assert.Contains(opening, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"targetId\":\"Creature:2\""));

        await SendAsync(hub, session, WebMessageTypes.PlayerMove, 2, new PlayerMovePayload(1, 2, 15, 0, true, MovementState.Walking));
        await SendAsync(hub, session, WebMessageTypes.PlayerMove, 3, new PlayerMovePayload(1, 2, 28, 0, false, MovementState.Standing));
        var approached = await RunAsync(hub, session, start + TimeSpan.FromSeconds(1), seconds: 1);
        Assert.DoesNotContain(approached, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("player:char-hunt7"));

        var engaged = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.AttackEngage, 4, new { targetId = "Creature:1" })));
        Assert.Contains(engaged, message => message.Contains("\"type\":\"attack.meleeStart\""));
        var fight = await RunAsync(hub, session, start + TimeSpan.FromSeconds(2), seconds: 6);

        Assert.Contains(fight, message => message.Contains("\"type\":\"attack.swingResult\"")
            && message.Contains("\"attackerId\":\"Creature:1\"") && message.Contains("\"targetId\":\"player:char-hunt7\""));
        Assert.DoesNotContain(fight, message => message.Contains("\"type\":\"attack.swingResult\"")
            && message.Contains("\"attackerId\":\"Creature:1\"") && message.Contains("\"targetId\":\"Creature:2\""));
        Assert.DoesNotContain(fight, message => message.Contains("\"evade\""));
    }

    [Fact]
    public async Task AFightBetweenCreaturesReachesAWatchingPlayer_AndIsRunOnce()
    {
        var hub = new PlaySessionHub();
        var maps = new SharedMapRegistry(() => 0.9);
        var (ayla, _) = await JoinAsync(hub, maps, "hunt5", [Wolf, NearRabbit]);
        var (borin, borinInbox) = await JoinAsync(hub, maps, "hunt6", [Wolf, NearRabbit]);
        borinInbox.Clear();

        var produced = new List<string>();
        var now = DateTimeOffset.UtcNow;
        for (var step = 0; step < 8; step++)
        {
            now += TimeSpan.FromMilliseconds(400);
            foreach (var session in new[] { ayla, borin })
            {
                session.TickWorld(now);
                var tick = session.TickCombat(now);
                produced.AddRange(tick);
                await hub.BroadcastAsync(session, tick);
            }
        }

        Assert.Contains(borinInbox, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"attackerId\":\"Creature:1\"") && message.Contains("\"targetId\":\"Creature:2\""));
        Assert.Contains(borinInbox, message => message.Contains("\"type\":\"attack.swingResult\"")
            && message.Contains("\"attackerId\":\"Creature:1\""));
        Assert.Contains(borinInbox, message => message.Contains("\"type\":\"unit.died\"") && message.Contains("\"Creature:2\""));
        Assert.Single(produced, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"attackerId\":\"Creature:1\""));
        Assert.Single(produced, message => message.Contains("\"type\":\"unit.died\""));
    }

    private static async Task<List<string>> RunAsync(
        PlaySessionHub hub, PlaySession session, DateTimeOffset from, int seconds)
    {
        var seen = new List<string>();
        for (var step = 1; step <= seconds * 2; step++)
        {
            var now = from + TimeSpan.FromMilliseconds(500 * step);
            session.TickWorld(now);
            var tick = session.TickCombat(now);
            seen.AddRange(tick);
            await hub.BroadcastAsync(session, tick);
        }

        return seen;
    }

    private static GameContent Content(bool toughRabbit = false)
    {
        var creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [WolfEntry] = new(WolfEntry, "wolf", 3, 150, MinDamage: 6, MaxDamage: 9,
                RespawnTimeMinSeconds: 5, RespawnTimeMaxSeconds: 5, MeleeAttackIntervalMs: 1500,
                FactionType: UnitFactionTypes.Hostile, Family: 2),
            [RabbitEntry] = new(RabbitEntry, "rabbit", 1, toughRabbit ? 100000 : 1, MinDamage: 1, MaxDamage: 1,
                RespawnTimeMinSeconds: 5, RespawnTimeMaxSeconds: 5,
                FactionType: UnitFactionTypes.Neutral, Family: CreatureFamilies.Critter),
        };
        return GameContent.Development with { Creatures = creatures };
    }

    private static async Task<(PlaySession Session, List<string> Inbox)> JoinAsync(
        PlaySessionHub hub, SharedMapRegistry maps, string guid, CreatureSpawnDefinition[] spawns, bool toughRabbit = false)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([new CharacterRecord(
                $"char-{guid}", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            spawns, content: Content(toughRabbit), combatRollUnit: () => 1.0, sharedMaps: maps);
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });
        await SendAsync(hub, session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
        await SendAsync(hub, session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        return (session, inbox);
    }

    private static async Task SendAsync(PlaySessionHub hub, PlaySession session, string type, int sequence, object payload)
    {
        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
        await hub.BroadcastAsync(session, responses);
    }

    private static string? NetworkIdOf(string message)
    {
        const string marker = "\"networkId\":\"";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        var end = message.IndexOf('"', start);
        return end < 0 ? null : message[start..end];
    }
}
