using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class LevelingTests
{
    [Fact]
    public async Task KillingACreature_GrantsKillXp_WithNoLevelFieldsWhenNothingLevelsUp()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>
            {
                [9001] = new(9001, "creature-9001", Level: 1, MaxHealth: 1, MinDamage: 0, MaxDamage: 0),
            },
        };
        var session = await SessionAsync(content, level: 1);

        await AttackAsync(session, "Creature:1");
        var now = DateTimeOffset.UtcNow;
        var responses = new List<string>();
        for (var swing = 0; swing < 20; swing++)
        {
            now += TimeSpan.FromSeconds(2);
            responses.AddRange(session.TickCombat(now));
            if (responses.Any(m => m.Contains("\"type\":\"unit.died\""))) break;
        }

        var updated = Assert.Single(responses, m => m.Contains("\"type\":\"player.updated\"") && m.Contains("\"xp\""));
        Assert.Contains("\"xp\":55", updated);
        Assert.DoesNotContain("\"level\":", updated);
        Assert.DoesNotContain("\"health\":", updated);
    }

    [Fact]
    public async Task EnoughKills_LevelsUpAndSendsTheLevelFieldsFullyHealed()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>
            {
                [9001] = new(9001, "creature-9001", Level: 11, MaxHealth: 1, MinDamage: 0, MaxDamage: 0,
                    RespawnTimeMinSeconds: 1, RespawnTimeMaxSeconds: 1),
            },
        };
        var session = await SessionAsync(content, level: 1);

        var targetId = "Creature:1";
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<string> lastKillResponses = [];
        for (var kill = 1; kill <= 4; kill++)
        {
            (now, lastKillResponses, targetId) = await KillAndRespawnAsync(session, targetId, now);
        }

        var updated = Assert.Single(lastKillResponses, m => m.Contains("\"type\":\"player.updated\""));
        Assert.Contains("\"level\":2", updated);
        Assert.Contains("\"xp\":82", updated);
        Assert.Contains("\"xpToNextLevel\":600", updated);
        Assert.Contains("\"health\":", updated);
        Assert.Contains("\"maxHealth\":", updated);
        var gained = Assert.Single(lastKillResponses, m => m.Contains("\"type\":\"player.expGained\""));
        Assert.Contains("\"amount\":83", gained);
        Assert.Contains("\"levelUp\":true", gained);
    }

    // Other players see a character through its presence and the unit values it sends them.
    [Fact]
    public async Task ALevelUp_IsShownToOtherPlayersToo()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>
            {
                [9001] = new(9001, "creature-9001", Level: 11, MaxHealth: 1, MinDamage: 0, MaxDamage: 0,
                    RespawnTimeMinSeconds: 1, RespawnTimeMaxSeconds: 1),
            },
        };
        var session = await SessionAsync(content, level: 1);
        var targetId = "Creature:1";
        var now = DateTimeOffset.UtcNow;
        for (var kill = 1; kill <= 3; kill++) (now, _, targetId) = await KillAndRespawnAsync(session, targetId, now);

        await AttackAsync(session, targetId);
        var seen = new List<string>();
        for (var tick = 0; tick < 20; tick++)
        {
            now += TimeSpan.FromSeconds(2);
            seen.AddRange(session.TickCombat(now));
        }

        Assert.Equal(2, session.Presence!.Level);
        Assert.Contains(seen, m => m.Contains("\"type\":\"entity.updated\"") && m.Contains("\"level\":2"));
    }

    [Fact]
    public async Task ReachingLevelTen_SendsTheFirstTalentPoint()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>
            {
                [9001] = new(9001, "creature-9001", Level: 9, MaxHealth: 1, MinDamage: 0, MaxDamage: 0),
            },
        };
        var session = await SessionAsync(content, level: 9, xp: LevelingService.XpToNextLevel(9) - 1);

        await AttackAsync(session, "Creature:1");
        var now = DateTimeOffset.UtcNow;
        var responses = new List<string>();
        for (var swing = 0; swing < 20; swing++)
        {
            now += TimeSpan.FromSeconds(2);
            responses.AddRange(session.TickCombat(now));
            if (responses.Any(m => m.Contains("\"type\":\"unit.died\""))) break;
        }

        var state = Assert.Single(responses, m => m.Contains("\"type\":\"talent.state\""));
        Assert.Contains("\"points\":1", state);
    }

    [Fact]
    public async Task AtTheLevelCap_XpLeftOverFromAnEarlierSaveIsZeroedOnEntry()
    {
        var store = new InMemoryCharacterStore([Character(LevelingService.MaxLevel) with { Xp = 12_345 }]);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), store,
            [], null, GameContent.Development);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));

        var listed = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersList, 1, new { })));
        Assert.DoesNotContain(listed, m => m.Contains("\"xp\":12345"));

        var entered = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 2, new ChooseCharacterRequest("Ayla"))));
        var values = Assert.Single(entered, m => m.Contains("\"type\":\"player.updated\"") && m.Contains("\"xp\""));
        Assert.Contains("\"xp\":0", values);
        Assert.Contains("\"xpToNextLevel\":0", values);

        await session.SaveCharacterAsync();
        Assert.Equal(0, (await store.GetByGuidAsync("char-a"))!.Xp);
    }

    [Fact]
    public async Task AtTheLevelCap_AKillPaysNoXpAndSendsNoGain()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>
            {
                [9001] = new(9001, "creature-9001", Level: LevelingService.MaxLevel, MaxHealth: 1, MinDamage: 0, MaxDamage: 0),
            },
        };
        var session = await SessionAsync(content, level: LevelingService.MaxLevel, xp: 500);

        await AttackAsync(session, "Creature:1");
        var now = DateTimeOffset.UtcNow;
        var responses = new List<string>();
        for (var swing = 0; swing < 20; swing++)
        {
            now += TimeSpan.FromSeconds(2);
            responses.AddRange(session.TickCombat(now));
            if (responses.Any(m => m.Contains("\"type\":\"unit.died\""))) break;
        }

        Assert.Contains(responses, m => m.Contains("\"type\":\"unit.died\""));
        Assert.DoesNotContain(responses, m => m.Contains("\"type\":\"player.expGained\""));
        Assert.DoesNotContain(responses, m => m.Contains("\"type\":\"player.updated\"") && m.Contains("\"xp\""));
    }

    private static async Task<(DateTimeOffset Now, IReadOnlyList<string> KillResponses, string NextTargetId)> KillAndRespawnAsync(
        PlaySession session, string targetId, DateTimeOffset now)
    {
        await AttackAsync(session, targetId);
        IReadOnlyList<string> killResponses = [];
        for (var swing = 0; swing < 20; swing++)
        {
            now += TimeSpan.FromSeconds(2);
            killResponses = session.TickCombat(now);
            if (killResponses.Any(m => m.Contains("\"type\":\"unit.died\""))) break;
        }

        now += TimeSpan.FromSeconds(151);
        session.TickCombat(now);

        now += TimeSpan.FromSeconds(2);
        var respawned = session.TickCombat(now);
        var nextTargetId = ExtractCreatureNetworkId(respawned);

        return (now, killResponses, nextTargetId);
    }

    private static string ExtractCreatureNetworkId(IEnumerable<string> responses)
    {
        var spawned = responses.Single(r => r.Contains("\"type\":\"entity.spawned\"") && r.Contains("\"entry\""));
        const string marker = "\"networkId\":\"";
        var start = spawned.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = spawned.IndexOf('"', start);
        return spawned[start..end];
    }

    private static async Task<IReadOnlyList<string>> AttackAsync(PlaySession session, string targetId) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId })));

    private static async Task<PlaySession> SessionAsync(GameContent content, int level, int xp = 0)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character(level) with { Xp = xp }]),
            [new CreatureSpawnDefinition(1, 13, 9001, 2f, 2f, 3f, 0f)], null, content,
            combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static CharacterRecord Character(int level) => new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, level, [100, 200]);
}
