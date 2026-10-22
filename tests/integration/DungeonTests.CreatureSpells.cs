using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Scripts;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed partial class DungeonTests
{
    private static GameContent CreatureSpellContent() => GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [2651] = new(2651, "Putrefying", 0, 0, 0f, AuraPeriodicTriggerSpellId: 2679,
                AuraPeriodicTriggerChancePercent: 100, EffectTarget: SpellEffectTargets.Self),
            [2679] = new(2679, "Putrefying", 0, 0, 0f, EffectDamage: 1, EffectTarget: SpellEffectTargets.AreaEnemy),
            [2652] = new(2652, "Decayed Seed", 0, 0, 30f, AuraStun: true, DurationMs: 3000,
                EffectTarget: SpellEffectTargets.SingleEnemy),
            [2632] = new(2632, "Thunder Curse", 2000, 0, 40f, EffectDamage: 1, EffectTarget: SpellEffectTargets.SingleEnemy),
            [2633] = new(2633, "Cold Curse", 0, 0, 35f, EffectDamage: 1, EffectTarget: SpellEffectTargets.SingleEnemy),
            [2634] = new(2634, "Flame Curse", 0, 0, 35f, EffectDamage: 1, EffectTarget: SpellEffectTargets.PositionAreaEnemy),
            [2635] = new(2635, "Demon Metamorphosis", 0, 0, 0f, EffectTarget: SpellEffectTargets.Self),
            [2637] = new(2637, "Shadow Erode", 0, 0, 0f, EffectTarget: SpellEffectTargets.Self),
            [2658] = new(2658, "Bone Thorn", 0, 0, 0f, EffectTarget: SpellEffectTargets.Self),
            [2641] = new(2641, "Poison Teeth", 0, 0, 0f, AuraSlowPercent: 10, DurationMs: 20000,
                EffectTarget: SpellEffectTargets.SingleEnemy),
        },
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [2668] = new(2668, "Ariel", 20, 100000, MinDamage: 1, MaxDamage: 1,
                NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
            [2667] = new(2667, "Cosmo", 21, 600, MinDamage: 1, MaxDamage: 1,
                NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
            [2697] = new(2697, "Undead Puppet", 19, 200, MinDamage: 1, MaxDamage: 1,
                NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
            [2674] = new(2674, "Bone Golem", 20, 10, MinDamage: 1, MaxDamage: 1,
                NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
            [2673] = new(2673, "Rotten Jail Hound", 19, 100000, MinDamage: 1, MaxDamage: 1,
                NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
            [2692] = new(2692, "Inferior Bone Golem", 20, 500, MinDamage: 1, MaxDamage: 1,
                NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
        },
    };

    private sealed record Party(PlaySession Ayla, PlaySession Borin);

    private static async Task<Party> PartyInsideAsync(string tag, CreatureSpawnDefinition[] spawns, GameContent content)
    {
        var hub = new PlaySessionHub();
        var instances = new InstanceService();
        var maps = new SharedMapRegistry(() => 0.9);
        var blows = new PlayerBlowRelay();

        async Task<PlaySession> Join(string guid, string name, float z)
        {
            var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
                new InMemoryCharacterStore([new CharacterRecord(
                    $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 1, GateApproachX, 2, z, 4, [100, 200])]),
                spawns, content: content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9,
                instances: instances, parties: TestSessions.PartiesOf(hub), sharedMaps: maps, blows: blows);
            hub.Register(session, _ => Task.CompletedTask);
            await SendAsync(hub, session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
            await SendAsync(hub, session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name));
            return session;
        }

        var ayla = await Join($"{tag}a", "Ayla", GateApproachZ);
        var borin = await Join($"{tag}b", "Borin", GateApproachZ - 2);
        await SendAsync(hub, ayla, WebMessageTypes.PartyInvite, 5, new { networkId = $"player:char-{tag}b" });
        await SendAsync(hub, borin, WebMessageTypes.PartyAccept, 6, new { });
        await SendAsync(hub, ayla, WebMessageTypes.PlayerMove, 7,
            new PlayerMovePayload(GateInsideX, 2, GateApproachZ, 0, true, MovementState.Walking));
        await SendAsync(hub, borin, WebMessageTypes.PlayerMove, 7,
            new PlayerMovePayload(GateInsideX, 2, GateApproachZ - 2, 0, true, MovementState.Walking));
        await MoveAsync(ayla, 8, InsideArrivalX, InsideFloorY, -2096);
        await MoveAsync(borin, 8, InsideArrivalX, InsideFloorY, -2095);
        return new Party(ayla, borin);
    }

    private static async Task EngageAsync(PlaySession session, string targetId) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 20, new { targetId })));

    [Fact]
    public async Task ABossAimingAtAnyone_CanLandItsSpellOnAPartyMemberWhoIsNotItsTarget()
    {
        var party = await PartyInsideAsync("cs1",
            [new CreatureSpawnDefinition(3201, 14, 2668, -681, InsideFloorY, -2097, 0)], CreatureSpellContent());
        await EngageAsync(party.Ayla, "Creature:1");
        await EngageAsync(party.Borin, "Creature:1");

        var now = DateTimeOffset.UtcNow;
        var aylaLog = new List<string>();
        var borinLog = new List<string>();
        for (var i = 0; i < 15; i++)
        {
            now += TimeSpan.FromSeconds(2);
            aylaLog.AddRange(party.Ayla.TickCombat(now));
            borinLog.AddRange(party.Borin.TickCombat(now));
        }

        // Ayla pulled her, so Ayla's session acts for her; the roll that picks the victim always lands on Borin.
        Assert.Contains(borinLog, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"spellId\":2652")
            && m.Contains("\"unitId\":\"player:char-cs1b\""));
        Assert.Equal(1, aylaLog.Concat(borinLog).Count(m => m.Contains("\"type\":\"spell.released\"")
            && m.Contains("\"spellId\":2651")));
    }

    [Fact]
    public async Task ABossWearingAPulsingAura_HurtsEveryoneAroundItEachPulse()
    {
        var party = await PartyInsideAsync("cs2",
            [new CreatureSpawnDefinition(3202, 14, 2668, -681, InsideFloorY, -2097, 0)], CreatureSpellContent());
        await EngageAsync(party.Ayla, "Creature:1");

        var now = DateTimeOffset.UtcNow;
        var aylaLog = new List<string>();
        var borinLog = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            now += TimeSpan.FromSeconds(2);
            aylaLog.AddRange(party.Ayla.TickCombat(now));
            borinLog.AddRange(party.Borin.TickCombat(now));
        }

        bool Rotted(string m, string who) => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"spellId\":2679")
            && m.Contains($"\"targetId\":\"{who}\"");
        Assert.True(aylaLog.Count(m => Rotted(m, "player:char-cs2a")) >= 3);
        Assert.Contains(borinLog, m => Rotted(m, "player:char-cs2b"));
    }

    [Fact]
    public async Task ASpellWithACastTime_ShowsItsCastAndLandsOnlyWhenTheCastEnds()
    {
        var (session, _) = await JoinAsync("cs3", "Ayla",
            [new CreatureSpawnDefinition(3203, 14, 2667, -681, InsideFloorY, -2097, 0)], CreatureSpellContent());
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await MoveAsync(session, 3, InsideArrivalX, InsideFloorY, -2096);
        await EngageAsync(session, "Creature:1");

        var now = DateTimeOffset.UtcNow;
        var log = new List<(DateTimeOffset At, string Message)>();
        for (var i = 0; i < 6; i++)
        {
            now += TimeSpan.FromSeconds(1);
            log.AddRange(session.TickCombat(now).Select(m => (now, m)));
        }

        var started = log.First(e => e.Message.Contains("\"type\":\"spell.casting\"") && e.Message.Contains("\"spellId\":2632"));
        var landed = log.First(e => e.Message.Contains("\"type\":\"spell.outcome\"") && e.Message.Contains("\"spellId\":2632"));
        Assert.True(landed.At - started.At >= TimeSpan.FromSeconds(2), $"landed {landed.At - started.At} after it began");
    }

    [Fact]
    public async Task APuppetNobodyEats_CrumblesAwayOnItsOwn()
    {
        var content = CreatureSpellContent();
        var (session, _) = await JoinAsync("cs4", "Ayla",
            [new CreatureSpawnDefinition(3204, 14, 2667, -681, InsideFloorY, -2097, 0)], content);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await MoveAsync(session, 3, InsideArrivalX, InsideFloorY, -2096);
        await EngageAsync(session, "Creature:1");

        var now = DateTimeOffset.UtcNow;
        var log = new List<(DateTimeOffset At, string Message)>();
        for (var i = 0; i < 60; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now).Select(m => (now, m)));
        }

        var raised = log.First(e => e.Message.Contains("\"type\":\"entity.spawned\"") && e.Message.Contains("\"entry\":2697"));
        var puppet = CreatureNetworkId([raised.Message]);
        var gone = log.First(e => e.Message.Contains("\"type\":\"entity.removed\"") && e.Message.Contains(puppet));
        Assert.True(gone.At - raised.At >= CosmoScript.PuppetCrumblesAfter, $"crumbled {gone.At - raised.At} after rising");
    }

    [Fact]
    public async Task ABoneGolem_BreaksIntoTwoLesserGolemsWhenItDies()
    {
        var (session, _) = await JoinAsync("cs5", "Ayla",
            [new CreatureSpawnDefinition(3205, 14, 2674, -681, InsideFloorY, -2097, 0)], CreatureSpellContent());
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await MoveAsync(session, 3, InsideArrivalX, InsideFloorY, -2096);
        await EngageAsync(session, "Creature:1");

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }

        Assert.Contains(log, m => m.Contains("\"type\":\"unit.died\"") && m.Contains("Creature:1"));
        Assert.Equal(2, log.Count(m => m.Contains("\"type\":\"entity.spawned\"") && m.Contains("\"entry\":2692")));
    }

    [Fact]
    public async Task AJailHound_SinksItsPoisonedTeethIntoWhatItIsFighting()
    {
        var (session, _) = await JoinAsync("cs6", "Ayla",
            [new CreatureSpawnDefinition(3206, 14, 2673, -681, InsideFloorY, -2097, 0)], CreatureSpellContent());
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await MoveAsync(session, 3, InsideArrivalX, InsideFloorY, -2096);
        await EngageAsync(session, "Creature:1");

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }

        Assert.Contains(log, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"spellId\":2641")
            && m.Contains("\"unitId\":\"player:char-cs6\""));
    }
}
