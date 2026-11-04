using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
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
            LegendContent(), level: 60);

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
            LegendContent(), level: 60);
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
            content, instances, level: 60);
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

        for (var i = 0; i < 8; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }

        Assert.True(instances.HasReached(key, "boss.2668.down"));
        Assert.True(instances.HasReached(key, "boss.2666.down"));
        Assert.Contains(log, m => m.Contains("\"type\":\"dungeon.barrierOpened\""));
        Assert.Contains(log, m => m.Contains("\"type\":\"entity.spawned\"") && m.Contains("\"entry\":2670"));
    }

    [Fact]
    public async Task ALegendCopy_WearsThePlayerDownForEachBossKilled_NotForTimeSpent()
    {
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("mode7", "Ayla", [], LegendContent(), instances, level: 60);
        await ChooseModeAsync(session, 2, WireCodes.DungeonMode.Legend);
        await MoveAsync(session, 3, GateInsideX, 2, GateApproachZ);
        var key = session.CurrentInstanceKey!;

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        log.AddRange(session.TickCombat(now));
        log.AddRange(session.TickCombat(now + TimeSpan.FromMinutes(10)));
        Assert.DoesNotContain(log, m => m.Contains("\"exhaustion\""));

        instances.Reach(key, "boss.2667.down");
        var afterCosmo = session.TickCombat(now + TimeSpan.FromMinutes(10.1));
        Assert.Contains(afterCosmo, m => m.Contains("\"type\":\"player.updated\"") && m.Contains("\"exhaustion\":8"));
    }

    [Fact]
    public async Task ALegendCopy_TurnsAwayAHeroBelowLevelSixty()
    {
        var (session, _) = await JoinAsync("mode9", "Ayla", [], LegendContent(), level: 59);
        await ChooseModeAsync(session, 2, WireCodes.DungeonMode.Legend);

        var refused = await MoveAsync(session, 3, GateInsideX, 2, GateApproachZ);

        Assert.Contains(refused, m => m.Contains(WireCodes.ProtocolError.LegendTooLow));
        Assert.Null(session.CurrentInstanceKey);
    }

    [Fact]
    public async Task ALegendCopy_TurnsAwayAHeroWornDownToOneHundred_ButANormalOneDoesNot()
    {
        var (legend, _) = await JoinAsync("mode10", "Ayla", [], LegendContent(), level: 60, exhaustion: 100);
        await ChooseModeAsync(legend, 2, WireCodes.DungeonMode.Legend);
        var refused = await MoveAsync(legend, 3, GateInsideX, 2, GateApproachZ);

        Assert.Contains(refused, m => m.Contains(WireCodes.ProtocolError.Exhausted));
        Assert.Null(legend.CurrentInstanceKey);

        var (normal, _) = await JoinAsync("mode11", "Ayla", [], LegendContent(), level: 60, exhaustion: 100);
        await MoveAsync(normal, 2, GateInsideX, 2, GateApproachZ);
        Assert.NotNull(normal.CurrentInstanceKey);
    }

    [Fact]
    public async Task ANormalCopy_NeverWearsThePlayerDown()
    {
        var (session, _) = await JoinAsync("mode8", "Ayla", [], LegendContent());
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);

        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);
        var later = session.TickCombat(now + TimeSpan.FromMinutes(5));

        Assert.DoesNotContain(later, m => m.Contains("\"exhaustion\""));
    }

    [Fact]
    public async Task ABossKilledInNormal_StandsAndFightsInTheLegendCopyEnteredAfter()
    {
        var legend = LegendContent();
        var content = legend with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(legend.Creatures)
            {
                [2666] = new(2666, "Executioner", 21, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1),
            },
        };
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("mode12", "Ayla",
            [new CreatureSpawnDefinition(3112, 14, 2666, -681, InsideFloorY, -2097, 0)],
            content, instances, level: 60);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await MoveAsync(session, 20, InsideArrivalX, InsideFloorY, -2096);
        var normalKey = session.CurrentInstanceKey!;
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:1" })));
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 12 && !instances.HasReached(normalKey, "boss.2666.down"); i++)
        {
            now += TimeSpan.FromSeconds(2);
            session.TickCombat(now);
        }
        Assert.True(instances.HasReached(normalKey, "boss.2666.down"));

        await MoveAsync(session, 4, -684, InsideFloorY, -2090);
        await ChooseModeAsync(session, 5, WireCodes.DungeonMode.Legend);
        // Back in from where the exit leaves the player, at the height it leaves them at.
        await MoveAsync(session, 6, GateApproachX, 39, GateApproachZ);
        await MoveAsync(session, 7, GateInsideX, 39, GateApproachZ);
        // A fresh copy: changing the mode ended the one the boss died in.
        Assert.False(instances.HasReached(session.CurrentInstanceKey!, "boss.2666.down"));

        var engaged = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 8, new { targetId = "Creature:1" })));

        Assert.DoesNotContain(engaged, m => m.Contains(WireCodes.ProtocolError.InvalidTarget));
        Assert.Contains(engaged, m => m.Contains("\"type\":\"attack.meleeStart\""));
    }

    [Fact]
    public async Task ReloggingInsideALegendCopy_KeepsItLegend_SaysSo_AndBossesStillTire()
    {
        var legend = LegendContent();
        var content = legend with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(legend.Creatures)
            {
                [2669] = new(2669, "Executioner", 60, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1,
                    FactionType: UnitFactionTypes.Hostile),
            },
        };
        var instances = new InstanceService();
        var characters = new InMemoryCharacterStore([new CharacterRecord(
            "char-mode13", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 1, GateApproachX, 2, GateApproachZ, 60, [100, 200])]);
        var spawns = new[] { new CreatureSpawnDefinition(3113, 14, 2666, -681, InsideFloorY, -2097, 0) };
        async Task<(PlaySession Session, IReadOnlyList<string> Entered)> SignInAsync()
        {
            var session = TestSessions.Create(new DevelopmentAccountStore("mode13", "p"), characters, spawns,
                content: content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9, instances: instances);
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("mode13", "p"))));
            var entered = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
            return (session, entered);
        }

        var (first, _) = await SignInAsync();
        await ChooseModeAsync(first, 2, WireCodes.DungeonMode.Legend);
        await MoveAsync(first, 3, GateInsideX, 2, GateApproachZ);
        await first.SaveProgressAsync();
        first.LeaveWorld();

        var (again, entered) = await SignInAsync();
        Assert.Contains(entered, m => m.Contains("\"type\":\"dungeon.modeChosen\"") && m.Contains("\"mode\":\"legend\""));
        var key = again.CurrentInstanceKey!;
        var now = DateTimeOffset.UtcNow;
        again.TickCombat(now);
        await again.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 4, new { targetId = "Creature:1" })));
        var log = new List<string>();
        for (var i = 0; i < 12 && !instances.HasReached(key, "boss.2666.down"); i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(again.TickCombat(now));
        }
        log.AddRange(again.TickCombat(now + TimeSpan.FromSeconds(1)));

        Assert.True(instances.HasReached(key, "boss.2666.down"));
        Assert.Contains(log, m => m.Contains("\"exhaustion\":5"));
    }

    [Fact]
    public async Task AfterARelogAResetAndLegend_TheExecutionerFights_AndTheModeHoldsThroughTheNextRelog()
    {
        var legend = LegendContent();
        var content = legend with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(legend.Creatures)
            {
                [2666] = new(2666, "Executioner", 21, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1),
                [2669] = new(2669, "Executioner", 60, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1,
                    FactionType: UnitFactionTypes.Hostile),
            },
        };
        var instances = new InstanceService();
        var characters = new InMemoryCharacterStore([new CharacterRecord(
            "char-mode14", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 1, GateApproachX, 2, GateApproachZ, 60, [100, 200])]);
        var spawns = new[] { new CreatureSpawnDefinition(3114, 14, 2666, -681, InsideFloorY, -2097, 0) };
        async Task<(PlaySession Session, IReadOnlyList<string> Entered)> SignInAsync()
        {
            var session = TestSessions.Create(new DevelopmentAccountStore("mode14", "p"), characters, spawns,
                content: content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9, instances: instances);
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("mode14", "p"))));
            var entered = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
            return (session, entered);
        }
        async Task SignOutAsync(PlaySession session)
        {
            await session.SaveProgressAsync();
            session.LeaveWorld();
        }

        // Into Normal, and the Executioner dies.
        var (first, _) = await SignInAsync();
        await MoveAsync(first, 2, GateInsideX, 2, GateApproachZ);
        await MoveAsync(first, 3, InsideArrivalX, InsideFloorY, -2096);
        var normalKey = first.CurrentInstanceKey!;
        await first.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 4, new { targetId = "Creature:1" })));
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 12 && !instances.HasReached(normalKey, "boss.2666.down"); i++)
            first.TickCombat(now += TimeSpan.FromSeconds(2));
        Assert.True(instances.HasReached(normalKey, "boss.2666.down"));
        await SignOutAsync(first);

        // Back in, inside the copy; out through the exit, reset, Legend, and in again.
        var (second, _) = await SignInAsync();
        second.TickCombat(now += TimeSpan.FromSeconds(1));
        await MoveAsync(second, 2, -684, InsideFloorY, -2090);
        Assert.Null(second.CurrentInstanceKey);
        await second.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.DungeonRestart, 3, new { })));
        await ChooseModeAsync(second, 4, WireCodes.DungeonMode.Legend);
        await MoveAsync(second, 5, GateApproachX, 39, GateApproachZ);
        await MoveAsync(second, 6, GateInsideX, 39, GateApproachZ);
        Assert.NotNull(second.CurrentInstanceKey);
        await MoveAsync(second, 7, InsideArrivalX, InsideFloorY, -2096);

        var engaged = await second.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 8, new { targetId = "Creature:1" })));
        Assert.DoesNotContain(engaged, m => m.Contains(WireCodes.ProtocolError.InvalidTarget));
        Assert.Contains(engaged, m => m.Contains("\"type\":\"attack.meleeStart\""));
        await SignOutAsync(second);

        var (third, entered) = await SignInAsync();
        Assert.Contains(entered, m => m.Contains("\"type\":\"dungeon.modeChosen\"") && m.Contains("\"mode\":\"legend\""));
    }

    [Fact]
    public async Task TheChosenModeOutlastsAServerRestart()
    {
        var characters = new InMemoryCharacterStore([new CharacterRecord(
            "char-mode15", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 1, GateApproachX, 2, GateApproachZ, 60, [100, 200])]);
        async Task<(PlaySession Session, IReadOnlyList<string> Entered)> SignInAsync(InstanceService instances)
        {
            var session = TestSessions.Create(new DevelopmentAccountStore("mode15", "p"), characters, [],
                content: LegendContent(), instances: instances);
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("mode15", "p"))));
            var entered = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
            return (session, entered);
        }

        var (before, _) = await SignInAsync(new InstanceService());
        await ChooseModeAsync(before, 2, WireCodes.DungeonMode.Legend);
        await before.SaveProgressAsync();
        before.LeaveWorld();

        var restarted = new InstanceService();
        var (after, entered) = await SignInAsync(restarted);

        Assert.Contains(entered, m => m.Contains("\"type\":\"dungeon.modeChosen\"") && m.Contains("\"mode\":\"legend\""));
        Assert.Equal(DungeonMode.Legend, restarted.ChosenMode("player:char-mode15"));
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
