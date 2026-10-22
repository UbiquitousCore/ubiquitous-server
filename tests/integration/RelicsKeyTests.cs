using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class RelicsKeyTests
{
    private const float MouthX = -889f;
    private const float MouthZ = -1928f;

    private static GameContent Content() => GameContent.Development with
    {
        Npcs = new Dictionary<uint, NpcDefinition>(GameContent.Development.Npcs)
        {
            [2659] = new(2659, NpcFlags.Gossip, GreetingMenuId: 418),
        },
        GossipMenus = new Dictionary<uint, GossipMenuDefinition>(GameContent.Development.GossipMenus)
        {
            [418] = new(418, [], [], [GossipScripts.StartsRitual, 0, 0, 0, 0, 0]),
        },
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [2387] = new(2387, "Summoning", 0, 0, 0f, DurationMs: 7200000, EffectTarget: SpellEffectTargets.Self),
        },
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [2659] = new(2659, "Tharrath", 28, 8000, MinDamage: 1, MaxDamage: 1, FactionType: UnitFactionTypes.Townsfolk),
            [2660] = new(2660, "Tharrath", 28, 30000, MinDamage: 1, MaxDamage: 1, FactionType: UnitFactionTypes.Townsfolk),
            [2627] = new(2627, "Son of the Earth", 29, 2807, MinDamage: 1, MaxDamage: 1, NoticeDistance: 1,
                FactionType: UnitFactionTypes.Hostile),
            [2628] = new(2628, "Son of the Lake", 29, 2807, MinDamage: 1, MaxDamage: 1, NoticeDistance: 1,
                FactionType: UnitFactionTypes.Hostile),
        },
    };

    private static async Task<IReadOnlyList<string>> Send(PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload)));

    private static async Task<IReadOnlyList<string>> Move(PlaySession session, int sequence, float x, float y, float z) =>
        await Send(session, WebMessageTypes.PlayerMove, sequence, new PlayerMovePayload(x, y, z, 0, true, MovementState.Walking));

    private static async Task<PlaySession> InsideAsync(string guid)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([new CharacterRecord(
                $"char-{guid}", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 1, MouthX + 14, -6, MouthZ + 14, 30, [100, 200])]),
            [], content: Content(), combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9);
        await Send(session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
        await Send(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        await Move(session, 2, MouthX + 6, -7, MouthZ + 6);
        await Move(session, 3, MouthX, -7.7f, MouthZ);
        return session;
    }

    [Fact]
    public async Task WalkingIntoTheSwampMouth_EntersTheDungeon_WhereTharrathWaits()
    {
        var session = await InsideAsync("rk1");

        Assert.Equal((ushort)GameContent.RelicsKeyMap, session.CurrentMapId);
        var log = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));
        Assert.Contains(log, m => m.Contains("\"type\":\"entity.spawned\"") && m.Contains("\"entry\":2659"));
    }

    [Fact]
    public async Task TalkingToTharrath_LeadsTheWayAndTheFirstWaveComesForHim()
    {
        var session = await InsideAsync("rk2");
        var now = DateTimeOffset.UtcNow;
        var log = new List<string>(session.TickCombat(now += TimeSpan.FromSeconds(1)));
        var keeper = NetworkIdOf(log, 2659);

        await Move(session, 4, -214, 39.5f, 216);
        var talked = await Send(session, WebMessageTypes.NpcPickOption, 5, new { networkId = keeper, menuId = 418, choice = 1 });
        Assert.Contains(talked, m => m.Contains("\"type\":\"npc.choiceResult\"") && m.Contains("\"started\""));
        Assert.Contains(talked, m => m.Contains("\"channel\":\"yell\"") && m.Contains("Follow me!"));
        await Move(session, 6, -203, 39.5f, 207);
        await Move(session, 7, -191, 39.5f, 198);
        await Move(session, 8, -179, 39.5f, 189);

        for (var i = 0; i < 60; i++) log.AddRange(session.TickCombat(now += TimeSpan.FromSeconds(1)));

        Assert.Contains(log, m => m.Contains("\"type\":\"entity.moved\"") && m.Contains(keeper));
        Assert.Contains(log, m => m.Contains("\"channel\":\"yell\"") && m.Contains("starting!"));
        var caster = NetworkIdOf(log, 2660);
        Assert.Contains(log, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"spellId\":2387") && m.Contains(caster));
        Assert.Contains(log, m => m.Contains("\"channel\":\"yell\"") && m.Contains("Here they come, right on time!"));
        Assert.Contains(log, m => m.Contains("\"type\":\"entity.spawned\"") && (m.Contains("\"entry\":2627") || m.Contains("\"entry\":2628")));
        Assert.Contains(log, m => m.Contains("\"type\":\"attack.meleeStart\"") && m.Contains($"\"targetId\":\"{caster}\""));
    }

    private static string NetworkIdOf(IEnumerable<string> log, uint entry)
    {
        var spawned = log.First(m => m.Contains("\"type\":\"entity.spawned\"") && m.Contains($"\"entry\":{entry}"));
        const string marker = "\"networkId\":\"";
        var start = spawned.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return spawned[start..spawned.IndexOf('"', start)];
    }
}
