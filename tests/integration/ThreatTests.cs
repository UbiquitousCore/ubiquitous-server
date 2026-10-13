using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class ThreatTests
{
    [Fact]
    // The creature turns on its attacker as the fight starts, not at its first swing a moment later.
    public async Task Engage_TurnsTheCreatureOnItsAttackerAtOnceAndNotAgainAtItsFirstSwing()
    {
        var session = await SessionAsync(ContentWith(
            new CreatureDefinition(9500, "wolf", 3, 500, MinDamage: 1, MaxDamage: 2,
                FactionType: UnitFactionTypes.Hostile)),
            new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0));
        static bool FacesTheAttacker(string message) => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"attackerId\":\"Creature:1\"") && message.Contains("\"targetId\":\"player:char-a\"");

        var engaged = await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });
        var tick = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        Assert.Single(engaged, FacesTheAttacker);
        Assert.DoesNotContain(tick, FacesTheAttacker);
    }

    [Fact]
    public async Task TickCombat_MobSwitchesToWhoeverHurtsItMost()
    {
        var session = await SessionAsync(ContentWith(
            new CreatureDefinition(9500, "wolf", 3, 1000, MinDamage: 1, MaxDamage: 2,
                FactionType: UnitFactionTypes.Hostile),
            new CreatureDefinition(9600, "Test Guard", 10, 500, MinDamage: 50, MaxDamage: 50,
                FactionType: UnitFactionTypes.Townsfolk)),
            new CreatureSpawnDefinition(1001, 13, 9600, 4, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9500, 2, 2, 4, 0));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:2" });

        var now = DateTimeOffset.UtcNow;
        List<string> switched = [];
        for (var i = 0; i < 30 && switched.Count == 0; i++)
        {
            now += TimeSpan.FromSeconds(2);
            var tick = session.TickCombat(now).ToList();
            switched = tick.Where(message => message.Contains("\"type\":\"entity.updated\"")
                && message.Contains("\"networkId\":\"Creature:1\"")).ToList();
        }

        Assert.NotEmpty(switched);
    }

    [Fact]
    public async Task TickCombat_QuestGiverComesToThePlayersAid()
    {
        var content = ContentWith(
            new CreatureDefinition(9500, "wolf", 3, 100, FactionType: UnitFactionTypes.Hostile),
            new CreatureDefinition(9501, "Herbalist", 10, 500, MinDamage: 10, MaxDamage: 15,
                FactionType: UnitFactionTypes.Townsfolk));
        content = content with
        {
            Npcs = new Dictionary<uint, NpcDefinition>(content.Npcs)
            {
                [9501] = new(9501, NpcFlags.None, OffersQuests: true),
            },
        };
        var session = await SessionAsync(content,
            new CreatureSpawnDefinition(1001, 13, 9501, 4, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9500, 2, 2, 4, 0));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:2" });

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"attackerId\":\"Creature:1\""));
    }

    [Fact]
    public async Task TickCombat_TwoSessionsAgreeOnTheTopThreat()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var content = ContentWith(
            new CreatureDefinition(9500, "wolf", 3, 1000, MinDamage: 1, MaxDamage: 2,
                FactionType: UnitFactionTypes.Hostile));
        var spawns = new[] { new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0) };
        var ayla = await JoinAsync(maps, content, spawns, "ayla", "Ayla", "char-a", 1);
        var borin = await JoinAsync(maps, content, spawns, "borin", "Borin", "char-b", 2);
        await SendAsync(ayla, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });
        await SendAsync(borin, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        now += TimeSpan.FromSeconds(2);
        ayla.TickCombat(now);
        now += TimeSpan.FromSeconds(2);
        borin.TickCombat(now);
        now += TimeSpan.FromSeconds(2);
        borin.TickCombat(now);

        var state = maps.For(13);
        Assert.True(state.HostilityFor("Creature:1").ContainsKey("player:char-a"));
        Assert.True(state.HostilityFor("Creature:1").ContainsKey("player:char-b"));
        Assert.Equal("player:char-b", state.MobVictim("Creature:1"));

        now += TimeSpan.FromSeconds(2);
        borin.TickCombat(now);
        var flushed = borin.TickCombat(now + TimeSpan.FromMilliseconds(700));
        Assert.Contains(flushed, message => message.Contains("player.updated"));
    }

    private static async Task<PlaySession> JoinAsync(
        SharedMapRegistry maps, GameContent content, CreatureSpawnDefinition[] spawns,
        string userId, string name, string guid, int accountId)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(userId, "p", accountId),
            new InMemoryCharacterStore([new CharacterRecord(
                guid, accountId, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            spawns, content: content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9,
            sharedMaps: maps);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest(userId, "p"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name));
        return session;
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
