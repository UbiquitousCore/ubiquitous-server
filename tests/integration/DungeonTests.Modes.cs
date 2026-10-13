using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed partial class DungeonTests
{
    [Fact]
    public async Task TheLeaderChoosingLegend_FillsTheNextCopyWithLegendCreatures()
    {
        var (session, _) = await JoinAsync("mode1", "Ayla",
            [new CreatureSpawnDefinition(3101, 14, 2666, -681, InsideFloorY, -2097, 0)],
            LegendContent());

        var chosen = await ChooseModeAsync(session, 2, WireCodes.DungeonMode.Legend);
        var entered = await MoveAsync(session, 3, GateInsideX, 2, GateApproachZ);

        Assert.Contains(chosen, m => m.Contains("\"type\":\"dungeon.modeChosen\"") && m.Contains("\"mode\":\"legend\""));
        Assert.Contains(entered, m => m.Contains("\"type\":\"entity.spawned\"") && m.Contains("\"entry\":2669"));
        Assert.DoesNotContain(entered, m => m.Contains("\"type\":\"entity.spawned\"") && m.Contains("\"entry\":2666"));
    }

    [Fact]
    public async Task ANormalCopy_KeepsTheCreaturesItWasBuiltWith()
    {
        var (session, _) = await JoinAsync("mode2", "Ayla",
            [new CreatureSpawnDefinition(3102, 14, 2666, -681, InsideFloorY, -2097, 0)],
            LegendContent());

        var entered = await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);

        Assert.Contains(entered, m => m.Contains("\"type\":\"entity.spawned\"") && m.Contains("\"entry\":2666"));
    }

    [Fact]
    public async Task ALegendBoss_CastsTheLegendVersionOfEachAbility()
    {
        var (session, _) = await JoinAsync("mode3", "Ayla",
            [new CreatureSpawnDefinition(3103, 14, 2666, -681, InsideFloorY, -2097, 0)],
            LegendContent());
        await ChooseModeAsync(session, 2, WireCodes.DungeonMode.Legend);
        await MoveAsync(session, 3, GateInsideX, 2, GateApproachZ);
        await MoveAsync(session, 4, InsideArrivalX, InsideFloorY, -2096);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 10, new { targetId = "Creature:1" })));

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        for (var i = 0; i < 40; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }

        Assert.Contains(log, m => m.Contains("\"type\":\"spell.released\"") && m.Contains("\"spellId\":2761"));
        Assert.Contains(log, m => m.Contains("\"type\":\"spell.released\"") && m.Contains("\"spellId\":2762"));
        Assert.Contains(log, m => m.Contains("\"type\":\"spell.released\"") && m.Contains("\"spellId\":2764"));
        Assert.DoesNotContain(log, m => m.Contains("\"type\":\"spell.released\"") && m.Contains("\"spellId\":2683"));
    }

    [Fact]
    public async Task KillingTheLegendBosses_OpensTheWayAndBringsOutTheLegendLastBoss()
    {
        var content = LegendContent() with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(LegendContent().Creatures)
            {
                [2669] = new(2669, "creature-2669", 60, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1),
                [2671] = new(2671, "creature-2671", 61, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1),
            },
        };
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("mode4", "Ayla",
            [
                new CreatureSpawnDefinition(3104, 14, 2668, -681, InsideFloorY, -2097, 0),
                new CreatureSpawnDefinition(3105, 14, 2666, -683, InsideFloorY, -2097, 0),
            ],
            content, instances);
        await ChooseModeAsync(session, 2, WireCodes.DungeonMode.Legend);
        await MoveAsync(session, 3, GateInsideX, 2, GateApproachZ);
        var key = session.CurrentInstanceKey!;

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        foreach (var target in new[] { "Creature:1", "Creature:2" })
        {
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AttackEngage, 290, new { targetId = target })));
            for (var i = 0; i < 6; i++)
            {
                now += TimeSpan.FromSeconds(2);
                log.AddRange(session.TickCombat(now));
            }
        }

        Assert.True(instances.HasReached(key, "boss.2668.down"));
        Assert.True(instances.HasReached(key, "boss.2666.down"));
        Assert.Contains(log, m => m.Contains("\"type\":\"dungeon.barrierOpened\""));
        Assert.Contains(log, m => m.Contains("\"type\":\"entity.spawned\"") && m.Contains("\"entry\":2670"));
    }

    [Fact]
    public async Task ChangingTheModeWhileInside_IsRefused()
    {
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("mode5", "Ayla",
            [new CreatureSpawnDefinition(3106, 14, 2666, -681, InsideFloorY, -2097, 0)],
            LegendContent(), instances);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);

        var refused = await ChooseModeAsync(session, 3, WireCodes.DungeonMode.Legend);

        Assert.Contains(refused, m => m.Contains("\"dungeon_mode_refused\""));
        Assert.Equal(DungeonMode.Normal, instances.ChosenMode(PlayerId("mode5")));
    }

    [Fact]
    public async Task AMemberWhoDoesNotLead_CannotChangeTheMode()
    {
        var hub = new PlaySessionHub();
        var instances = new InstanceService();
        var spawns = new[] { new CreatureSpawnDefinition(3107, 14, 2666, -681, InsideFloorY, -2097, 0) };
        var (ayla, _) = await JoinAsync(hub, instances, "mode6a", "Ayla", spawns, GateApproachX, GateApproachZ, LegendContent());
        var (borin, _) = await JoinAsync(hub, instances, "mode6b", "Borin", spawns, GateApproachX, GateApproachZ - 2, LegendContent());
        await SendAsync(hub, ayla, WebMessageTypes.PartyInvite, 5, new { networkId = "player:char-mode6b" });
        await SendAsync(hub, borin, WebMessageTypes.PartyAccept, 6, new { });

        var refused = await SendAsync(hub, borin, WebMessageTypes.DungeonChooseMode, 7,
            new { mode = WireCodes.DungeonMode.Legend });

        Assert.Contains(refused, m => m.Contains("\"dungeon_mode_refused\""));
        Assert.Equal(DungeonMode.Normal, instances.ChosenMode(PlayerId("mode6a")));
    }

    [Fact]
    public async Task AModeTheGameDoesNotHave_IsRefused()
    {
        var (session, _) = await JoinAsync("mode7", "Ayla", [], LegendContent());

        var refused = await ChooseModeAsync(session, 2, "heroic");

        Assert.Contains(refused, m => m.Contains("\"invalid_payload\""));
    }

    private static string PlayerId(string guid) => $"player:char-{guid}";

    private static async Task<IReadOnlyList<string>> ChooseModeAsync(PlaySession session, int sequence, string mode) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.DungeonChooseMode, sequence, new { mode })));

    private static GameContent LegendContent()
    {
        var boss = BossContent();
        return boss with
        {
            Spells = new Dictionary<uint, SpellDefinition>(boss.Spells)
            {
                [2761] = new(2761, "spell-2761", 0, 0, 0f),
                [2762] = new(2762, "spell-2762", 0, 0, 0f),
                [2764] = new(2764, "spell-2764", 0, 0, 0f, EffectDamage: 1),
            },
            Creatures = new Dictionary<uint, CreatureDefinition>(boss.Creatures)
            {
                [2669] = new(2669, "creature-2669", 60, 100000, MinDamage: 1, MaxDamage: 1,
                    NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
                [2671] = new(2671, "creature-2671", 61, 4000, MinDamage: 1, MaxDamage: 1,
                    NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
                [2670] = new(2670, "creature-2670", 61, 600, MinDamage: 1, MaxDamage: 1,
                    NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
            },
        };
    }
}
