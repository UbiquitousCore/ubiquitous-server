using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class EvadeResetTests
{
    private const int WolfHealth = 1000;
    private const uint DotSpellId = 107;
    private const uint NukeSpellId = 108;
    private const int NukeDamage = 50;

    [Fact]
    public async Task Leash_PutsTheCreatureBackAtFullHealthAndClearsItsAurasOnTheTickItLetsGo()
    {
        var session = await SessionAsync(Content(wolfMinDamage: 1, wolfMaxDamage: 2));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });
        var now = DateTimeOffset.UtcNow;
        var wounded = WolfHealth;
        for (var i = 0; i < 3; i++)
        {
            now += TimeSpan.FromSeconds(2);
            wounded = Math.Min(wounded, LowestHealthOf(session.TickCombat(now), "Creature:1") ?? wounded);
        }
        Assert.True(wounded < WolfHealth, "expected the player's swings to wound the wolf");
        var cast = await SendAsync(session, WebMessageTypes.SpellRequest, 3, new { spellId = DotSpellId, targetId = "Creature:1" });
        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\""));

        await RunAwayAsync(session, 4);
        now += TimeSpan.FromSeconds(2);
        var leash = session.TickCombat(now);

        Assert.Contains(leash, m => m.Contains("\"type\":\"attack.exitCombat\"") && m.Contains("Creature:1"));
        Assert.Contains(leash, m => IsHealthUpdate(m, "Creature:1", WolfHealth));
        Assert.Contains(leash, m => m.Contains("\"type\":\"aura.ended\"")
            && m.Contains("\"unitId\":\"Creature:1\"") && m.Contains($"\"spellId\":{DotSpellId}"));

        for (var i = 1; i <= AuraService.TickCount + 1; i++)
        {
            var later = session.TickCombat(now + AuraService.TickInterval * i);
            Assert.DoesNotContain(later, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("Creature:1"));
        }
    }

    [Fact]
    public async Task Leash_ReachesAnotherPlayerWatchingTheCreature()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, maps, "evade1", "Ayla", "char-a", 1);
        var (_, borinInbox) = await JoinAsync(hub, maps, "evade2", "Borin", "char-b", 2);
        await SendAsync(ayla, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            now += TimeSpan.FromSeconds(2);
            await hub.BroadcastAsync(ayla, ayla.TickCombat(now));
        }

        await RunAwayAsync(ayla, 3);
        borinInbox.Clear();
        await hub.BroadcastAsync(ayla, ayla.TickCombat(now + TimeSpan.FromSeconds(2)));

        Assert.Contains(borinInbox, m => IsHealthUpdate(m, "Creature:1", WolfHealth));
    }

    [Fact]
    public async Task PlayerDeath_PutsTheKillerBackAtFullHealthOnTheTickThePlayerDies()
    {
        var session = await SessionAsync(Content(wolfMinDamage: 30, wolfMaxDamage: 40));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        var wounded = WolfHealth;
        List<string> deathTick = [];
        for (var i = 0; i < 60; i++)
        {
            now += TimeSpan.FromSeconds(2);
            deathTick = session.TickCombat(now).ToList();
            wounded = Math.Min(wounded, LowestHealthOf(deathTick, "Creature:1") ?? wounded);
            if (deathTick.Any(m => m.Contains("unit.died") && m.Contains("player:char-a"))) break;
        }

        Assert.Contains(deathTick, m => m.Contains("unit.died") && m.Contains("player:char-a"));
        Assert.True(wounded < WolfHealth, "expected the player's swings to wound the wolf before dying");
        Assert.Contains(deathTick, m => IsHealthUpdate(m, "Creature:1", WolfHealth));
    }

    [Fact]
    public async Task PlayerDeath_TheNextFightStartsAgainstAWholeCreature()
    {
        var session = await SessionAsync(Content(wolfMinDamage: 30, wolfMaxDamage: 40));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });
        var now = DateTimeOffset.UtcNow;
        var dead = false;
        for (var i = 0; i < 60 && !dead; i++)
        {
            now += TimeSpan.FromSeconds(2);
            dead = session.TickCombat(now).Any(m => m.Contains("unit.died") && m.Contains("player:char-a"));
        }
        Assert.True(dead, "expected the wolf to kill the player");

        await SendAsync(session, WebMessageTypes.PlayerUnstuck, 3, new { });
        await SendAsync(session, WebMessageTypes.PlayerMove, 4, new PlayerMovePayload(1, 2, 3, 0, false, MovementState.Standing));
        session.TickWorld(now);
        session.TickWorld(now + TimeSpan.FromMinutes(1));
        await SendAsync(session, WebMessageTypes.AttackEngage, 5, new { targetId = "Creature:1" });
        int? firstHit = null;
        for (var i = 0; i < 5 && firstHit is null; i++)
        {
            now += TimeSpan.FromSeconds(2);
            firstHit = FirstPlayerHitOn(session.TickCombat(now), "Creature:1");
        }

        Assert.NotNull(firstHit);
        Assert.Equal(WolfHealth, firstHit!.Value);
    }

    [Fact]
    public async Task WalkingHome_EveryBlowAndSpellLandsAsAnEvadeForNothing_AndNothingPullsItBack()
    {
        var session = await SessionAsync(Content(wolfMinDamage: 1, wolfMaxDamage: 2));
        var now = await LeashAsync(session);

        await WalkBackAsync(session, 9);
        var engage = await SendAsync(session, WebMessageTypes.AttackEngage, 20, new { targetId = "Creature:1" });
        Assert.DoesNotContain(engage, m => m.Contains("\"type\":\"attack.meleeStart\""));
        Assert.Contains(engage, m => m.Contains("\"code\":\"invalid_target\""));

        var nuke = await SendAsync(session, WebMessageTypes.SpellRequest, 21, new { spellId = NukeSpellId, targetId = "Creature:1" });
        var outcome = Assert.Single(nuke, m => m.Contains("\"type\":\"spell.outcome\""));
        Assert.Contains($"\"result\":\"{WireCodes.Outcome.Evade}\"", outcome);
        Assert.Contains("\"damage\":0", outcome);
        Assert.DoesNotContain(nuke, m => m.Contains("\"type\":\"attack.meleeStart\"")
            || m.Contains("\"type\":\"attack.enterCombat\""));

        var dot = await SendAsync(session, WebMessageTypes.SpellRequest, 22, new { spellId = DotSpellId, targetId = "Creature:1" });
        Assert.DoesNotContain(dot, m => m.Contains("\"type\":\"aura.applied\""));
        Assert.Contains(dot, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains($"\"result\":\"{WireCodes.Outcome.Evade}\""));

        for (var i = 0; i < 4; i++)
        {
            now += TimeSpan.FromSeconds(2);
            var tick = session.TickCombat(now);
            Assert.DoesNotContain(tick, m => m.Contains("\"type\":\"attack.meleeStart\"")
                || m.Contains("\"type\":\"attack.enterCombat\"")
                || m.Contains("\"type\":\"attack.swingResult\""));
            Assert.Null(LowestHealthOf(tick, "Creature:1"));
        }
        Assert.True(session.IsReturningHome("Creature:1"));
    }

    [Fact]
    public async Task WalkingHome_OnceItIsBackItCanBePulledAgain()
    {
        var session = await SessionAsync(Content(wolfMinDamage: 1, wolfMaxDamage: 2));
        var now = await LeashAsync(session);
        await WalkBackAsync(session, 9);

        session.TickWorld(now);
        session.TickWorld(now + TimeSpan.FromMinutes(1));
        Assert.False(session.IsReturningHome("Creature:1"));

        var pulled = false;
        for (var i = 0; i < 3 && !pulled; i++)
        {
            now += TimeSpan.FromSeconds(2);
            pulled = session.TickCombat(now).Any(m => m.Contains("\"type\":\"attack.meleeStart\"") && m.Contains("Creature:1"));
        }
        Assert.True(pulled, "expected the wolf to notice the player standing beside it once it was home");

        var nuke = await SendAsync(session, WebMessageTypes.SpellRequest, 30, new { spellId = NukeSpellId, targetId = "Creature:1" });
        var outcome = Assert.Single(nuke, m => m.Contains("\"type\":\"spell.outcome\""));
        Assert.Contains($"\"result\":\"{WireCodes.Outcome.Hit}\"", outcome);
        Assert.Contains($"\"damage\":{NukeDamage}", outcome);
    }

    private static async Task<DateTimeOffset> LeashAsync(PlaySession session)
    {
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            now += TimeSpan.FromSeconds(2);
            session.TickCombat(now);
        }
        await RunAwayAsync(session, 4);
        now += TimeSpan.FromSeconds(2);
        var leash = session.TickCombat(now);
        Assert.Contains(leash, m => m.Contains("\"type\":\"attack.exitCombat\"") && m.Contains("Creature:1"));
        Assert.True(session.IsReturningHome("Creature:1"));
        return now;
    }

    private static async Task WalkBackAsync(PlaySession session, int sequence)
    {
        for (var x = 57; x >= 1; x -= 14)
            await SendAsync(session, WebMessageTypes.PlayerMove, sequence++,
                new PlayerMovePayload(x, 2, 3, 0, true, MovementState.Walking));
        await SendAsync(session, WebMessageTypes.PlayerMove, sequence,
            new PlayerMovePayload(1, 2, 3, 0, false, MovementState.Standing));
    }

    private static bool IsHealthUpdate(string message, string unitId, int health)
    {
        using var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;
        if (root.GetProperty("type").GetString() != WebMessageTypes.EntityValuesChanged) return false;
        var payload = root.GetProperty("payload");
        if (payload.GetProperty("networkId").GetString() != unitId) return false;
        var values = payload.GetProperty("changed");
        return values.TryGetProperty(WireCodes.UnitValue.Health, out var h) && h.GetInt32() == health
            && values.TryGetProperty(WireCodes.UnitValue.MaxHealth, out var max) && max.GetInt32() == health;
    }

    private static int? LowestHealthOf(IEnumerable<string> messages, string unitId)
    {
        int? lowest = null;
        foreach (var message in messages)
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (root.GetProperty("type").GetString() != WebMessageTypes.AttackSwingResult) continue;
            var payload = root.GetProperty("payload");
            if (payload.GetProperty("targetId").GetString() != unitId) continue;
            var health = payload.GetProperty("finalHealth").GetInt32();
            lowest = lowest is null ? health : Math.Min(lowest.Value, health);
        }
        return lowest;
    }

    private static int? FirstPlayerHitOn(IEnumerable<string> messages, string unitId)
    {
        foreach (var message in messages)
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (root.GetProperty("type").GetString() != WebMessageTypes.AttackSwingResult) continue;
            var payload = root.GetProperty("payload");
            if (payload.GetProperty("targetId").GetString() != unitId) continue;
            var damage = payload.GetProperty("damage").GetInt32();
            if (damage <= 0) continue;
            return payload.GetProperty("finalHealth").GetInt32() + damage;
        }
        return null;
    }

    private static async Task RunAwayAsync(PlaySession session, int sequence)
    {
        for (var x = 15; x <= 60; x += 14)
            await SendAsync(session, WebMessageTypes.PlayerMove, sequence++,
                new PlayerMovePayload(x, 2, 3, 0, true, MovementState.Walking));
    }

    private static GameContent Content(int wolfMinDamage = 1, int wolfMaxDamage = 2) => GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9500] = new(9500, "wolf", 3, WolfHealth, MinDamage: wolfMinDamage, MaxDamage: wolfMaxDamage,
                FactionType: UnitFactionTypes.Hostile),
        },
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [DotSpellId] = new(DotSpellId, "spell-107", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                AuraPeriodicDamage: 6),
            [NukeSpellId] = new(NukeSpellId, "spell-108", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                EffectDamage: NukeDamage),
        },
        StartingSpells = [.. GameContent.Development.StartingSpells, DotSpellId, NukeSpellId],
        Maps = new Dictionary<ushort, MapDefinition>
        {
            [13] = new(13, "test", RecallMapId: 13, RecallX: 1, RecallY: 2, RecallZ: 3),
        },
        Graveyards = [],
    };

    private static readonly CreatureSpawnDefinition[] Spawns =
        [new(1001, 13, 9500, 2, 2, 4, 0)];

    private static async Task<PlaySession> SessionAsync(GameContent content)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            Spawns, content: content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9,
            spellRollUnit: () => 1.0);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        return session;
    }

    private static async Task<(PlaySession Session, List<string> Inbox)> JoinAsync(
        PlaySessionHub hub, SharedMapRegistry maps, string guid, string name, string charGuid, int accountId)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p", accountId),
            new InMemoryCharacterStore([new CharacterRecord(
                charGuid, accountId, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            Spawns, content: Content(), combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9,
            sharedMaps: maps);
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });
        var login = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"))));
        await hub.BroadcastAsync(session, login);
        var chosen = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name))));
        await hub.BroadcastAsync(session, chosen);
        return (session, inbox);
    }

    private static async Task<IReadOnlyList<string>> SendAsync(
        PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
}
