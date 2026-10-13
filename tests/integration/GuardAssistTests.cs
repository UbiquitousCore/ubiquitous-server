using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class GuardAssistTests
{
    [Fact]
    public async Task TickCombat_AGuardEngagesTheMobFightingThePlayer()
    {
        var session = await SessionAsync(ContentWith(
            new CreatureDefinition(9500, "wolf", 3, 100, FactionType: UnitFactionTypes.Hostile),
            new CreatureDefinition(9600, "Test Guard", 10, 500, MinDamage: 10, MaxDamage: 15,
                FactionType: UnitFactionTypes.Townsfolk)),
            new CreatureSpawnDefinition(1001, 13, 9600, 4, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9500, 2, 2, 4, 0));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:2" });

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"attackerId\":\"Creature:1\""));
    }

    [Fact]
    public async Task TickCombat_AGuardOutOfRangeDoesNotEngage()
    {
        var session = await SessionAsync(ContentWith(
            new CreatureDefinition(9500, "wolf", 3, 100, FactionType: UnitFactionTypes.Hostile),
            new CreatureDefinition(9600, "Test Guard", 10, 500, MinDamage: 10, MaxDamage: 15,
                FactionType: UnitFactionTypes.Townsfolk)),
            new CreatureSpawnDefinition(1001, 13, 9600, 40, 2, 3, 0),
            new CreatureSpawnDefinition(1002, 13, 9500, 2, 2, 4, 0));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:2" });

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message => message.Contains("\"attackerId\":\"Creature:1\""));
    }

    [Fact]
    public async Task TickCombat_ANonGuardTownsfolkDoesNotEngage()
    {
        var session = await SessionAsync(ContentWith(
            new CreatureDefinition(9500, "wolf", 3, 100, FactionType: UnitFactionTypes.Hostile),
            new CreatureDefinition(9600, "Shopkeeper", 10, 500, MinDamage: 10, MaxDamage: 15,
                FactionType: UnitFactionTypes.Townsfolk)),
            new CreatureSpawnDefinition(1001, 13, 9600, 4, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9500, 2, 2, 4, 0));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:2" });

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message => message.Contains("\"attackerId\":\"Creature:1\""));
    }

    [Fact]
    public async Task TickCombat_AnEnemyGuardDoesNotAssistThePlayer()
    {
        var session = await SessionAsync(ContentWith(
            new CreatureDefinition(9500, "wolf", 3, 100, FactionType: UnitFactionTypes.Hostile),
            new CreatureDefinition(9600, "Test Guard B", 10, 500, MinDamage: 10, MaxDamage: 15,
                FactionType: UnitFactionTypes.Hostile)),
            new CreatureSpawnDefinition(1001, 13, 9600, 4, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9500, 30, 2, 3, 0));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:2" });

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message => message.Contains("\"attackerId\":\"Creature:1\"")
            && message.Contains("\"targetId\":\"Creature:2\""));
    }

    [Fact]
    public async Task TickCombat_GuardsStandDownWhenThePlayerDies()
    {
        var session = await SessionAsync(ContentWith(
            new CreatureDefinition(9500, "wolf", 12, 500, MinDamage: 60, MaxDamage: 80,
                FactionType: UnitFactionTypes.Hostile),
            new CreatureDefinition(9600, "Test Guard", 10, 500, MinDamage: 10, MaxDamage: 15,
                FactionType: UnitFactionTypes.Townsfolk)),
            new CreatureSpawnDefinition(1001, 13, 9600, 4, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9500, 2, 2, 4, 0));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:2" });

        var now = DateTimeOffset.UtcNow;
        var dead = false;
        List<string> deathTick = [];
        for (var i = 0; i < 30 && !dead; i++)
        {
            now += TimeSpan.FromSeconds(2);
            deathTick = session.TickCombat(now).ToList();
            dead = deathTick.Any(message => message.Contains("unit.died")
                && message.Contains("player:char-a"));
        }

        Assert.True(dead, "expected the wolf to kill the player");
        Assert.Contains(deathTick, message => message.Contains("attack.halted")
            && message.Contains("Creature:1"));
    }

    [Fact]
    public async Task TickCombat_GuardWalksHomeAfterItsMobDies()
    {
        var session = await SessionAsync(ContentWith(
            new CreatureDefinition(9500, "wolf", 3, 30, MinDamage: 1, MaxDamage: 2,
                FactionType: UnitFactionTypes.Hostile),
            new CreatureDefinition(9600, "Test Guard", 10, 500, MinDamage: 10, MaxDamage: 15,
                FactionType: UnitFactionTypes.Townsfolk)),
            new CreatureSpawnDefinition(1001, 13, 9600, 10, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9500, 2, 2, 4, 0));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:2" });

        var now = DateTimeOffset.UtcNow;
        var dead = false;
        for (var i = 0; i < 40 && !dead; i++)
        {
            now += TimeSpan.FromSeconds(2);
            dead = session.TickCombat(now).Any(message => message.Contains("unit.died")
                && !message.Contains("player:char-a"));
        }

        Assert.True(dead, "expected the mob to die");
        session.TickCombat(now + TimeSpan.FromSeconds(2));
        session.TickWorld(now + TimeSpan.FromSeconds(4));
        var retreating = session.TickCombat(now + TimeSpan.FromSeconds(4));
        var leg = Assert.Single(retreating, message => message.Contains("entity.moved")
            && message.Contains("\"networkId\":\"Creature:1\"")
            && message.Contains("\"moving\":true"));
        Assert.Contains("\"target\":{\"x\":10", leg);
    }

    [Fact]
    public async Task TickCombat_GuardFacesItsSpawnYawOnceItArrivesHome()
    {
        var session = await SessionAsync(ContentWith(
            new CreatureDefinition(9500, "wolf", 3, 30, MinDamage: 1, MaxDamage: 2,
                FactionType: UnitFactionTypes.Hostile),
            new CreatureDefinition(9600, "Test Guard", 10, 500, MinDamage: 10, MaxDamage: 15,
                FactionType: UnitFactionTypes.Townsfolk)),
            new CreatureSpawnDefinition(1001, 13, 9600, 10, 2, 4, 1.5f),
            new CreatureSpawnDefinition(1002, 13, 9500, 2, 2, 4, 0));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:2" });

        var now = DateTimeOffset.UtcNow;
        var dead = false;
        for (var i = 0; i < 40 && !dead; i++)
        {
            now += TimeSpan.FromSeconds(2);
            dead = session.TickCombat(now).Any(message => message.Contains("unit.died")
                && !message.Contains("player:char-a"));
        }

        Assert.True(dead, "expected the mob to die");
        session.TickCombat(now + TimeSpan.FromSeconds(2));

        List<string> arrival = [];
        for (var step = 1; step <= 10 && arrival.Count == 0; step++)
        {
            now += TimeSpan.FromSeconds(2);
            session.TickWorld(now);
            arrival = session.TickCombat(now).Where(message => message.Contains("entity.moved")
                && message.Contains("\"networkId\":\"Creature:1\"")
                && message.Contains("\"moving\":false")).ToList();
        }

        var home = Assert.Single(arrival);
        Assert.Contains("\"yaw\":1.5", home);
    }

    [Fact]
    public async Task TickCombat_AHelperThatFallsRespawnsWithoutLoot()
    {
        var session = await SessionAsync(ContentWith(
            new CreatureDefinition(9500, "wolf", 3, 1000, MinDamage: 15, MaxDamage: 15,
                FactionType: UnitFactionTypes.Hostile),
            new CreatureDefinition(9600, "Test Guard", 10, 30, MinDamage: 50, MaxDamage: 50,
                FactionType: UnitFactionTypes.Townsfolk)),
            new CreatureSpawnDefinition(1001, 13, 9600, 4, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9500, 2, 2, 4, 0));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:2" });

        var now = DateTimeOffset.UtcNow;
        var dead = false;
        for (var i = 0; i < 40 && !dead; i++)
        {
            now += TimeSpan.FromSeconds(2);
            dead = session.TickCombat(now).Any(message => message.Contains("unit.died")
                && message.Contains("Creature:1") && !message.Contains("player"));
        }

        Assert.True(dead, "expected the wolf to kill the guard");
        string? respawned = null;
        for (var i = 0; i < 30 && respawned is null; i++)
        {
            now += TimeSpan.FromSeconds(5);
            respawned = session.TickCombat(now)
                .Where(message => message.Contains("entity.spawned"))
                .Select(SpawnedId)
                .FirstOrDefault(id => id is not null && id != "Creature:1" && id != "Creature:2");
        }

        Assert.NotNull(respawned);
        var reopened = await SendAsync(session, WebMessageTypes.LootOpen, 3, new { networkId = "Creature:1" });
        Assert.Empty(reopened);
    }

    private static string? SpawnedId(string message)
    {
        const string marker = "\"networkId\":\"";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        var end = message.IndexOf('"', start);
        return end < 0 ? null : message[start..end];
    }

    private static GameContent ContentWith(params CreatureDefinition[] defs)
    {
        var creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures);
        foreach (var def in defs) creatures[def.EntryId] = def;
        return GameContent.Development with { Creatures = creatures };
    }

    private static async Task<PlaySession> SessionAsync(GameContent content, params CreatureSpawnDefinition[] spawns)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            spawns, content: content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        return session;
    }

    private static async Task<IReadOnlyList<string>> SendAsync(
        PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
}
