using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class SpellExtraEffectTests
{
    private static readonly CharacterRecord Ranger = new(
        "char-a", 1, "Ayla", 1, 2, PlayerClassIds.Ranger, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4,
        [.. EquipmentSlots.Build((EquipmentSlots.MainHand, 3907), (EquipmentSlots.OffHand, 3907), (EquipmentSlots.Ranged, 4149))]);

    private static SpellDefinition Extra(uint id, int target) =>
        new(id, $"spell-{id}", 0, 0, 30f, DurationMs: 5000, EffectTarget: target);

    private static async Task<PlaySession> SessionAsync(
        IReadOnlyDictionary<uint, SpellDefinition> spells, Func<double>? spellRoll = null,
        IReadOnlyDictionary<uint, ItemDefinition>? items = null)
    {
        var content = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells.Concat(spells)),
            StartingSpells = [.. GameContent.Development.StartingSpells, .. spells.Keys],
            Items = items ?? GameContent.Development.Items,
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Ranger]), [new CreatureSpawnDefinition(1001, 13, 9001, 3, 2, 3, 0)],
            null, content, spellRollUnit: spellRoll ?? (() => 1.0), combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static Task<IReadOnlyList<string>> CastAsync(PlaySession session, uint spellId, string targetId = "Creature:1") =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 5, new { spellId, targetId })));

    private static bool Is(string message, string type) => message.Contains($"\"type\":\"{type}\"");

    [Fact]
    public async Task ADamageSpellThatBurnsOverTimeDealsItsDamageAndStartsTheBurn()
    {
        var spell = new SpellDefinition(1400, "spell-1400", 0, 1000, 30f, PowerCost: 10, EffectDamage: 20,
            EffectTarget: SpellEffectTargets.SingleEnemy,
            ExtraEffects: [Extra(1400, SpellEffectTargets.SingleEnemy) with { AuraPeriodicDamage = 2 }]);
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1400] = spell });

        var cast = await CastAsync(session, 1400);

        Assert.Single(cast, m => Is(m, "spell.released"));
        Assert.Contains(cast, m => Is(m, "spell.outcome") && m.Contains("\"damage\":20"));
        Assert.Contains(cast, m => Is(m, "aura.applied") && m.Contains("\"unitId\":\"Creature:1\"")
            && m.Contains("\"spellId\":1400") && m.Contains("\"harmful\":1"));
        var tick = session.TickCombat(DateTimeOffset.UtcNow + AuraService.TickInterval + TimeSpan.FromMilliseconds(500));
        Assert.Contains(tick, m => Is(m, "spell.outcome") && m.Contains("\"spellId\":1400") && m.Contains("\"damage\":2"));
    }

    [Fact]
    public async Task AWeaponBlowThatStunsLandsTheBlowAndStunsTheCreature()
    {
        var spell = new SpellDefinition(1410, "spell-1410", 0, 1000, 0f, PowerCost: 10,
            EffectMeleeWeaponHit: true, EffectMeleeWeaponDamagePercent: 100, EffectTarget: SpellEffectTargets.SingleEnemy,
            ExtraEffects: [Extra(1410, SpellEffectTargets.SingleEnemy) with { AuraStun = true }]);
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1410] = spell });

        var cast = await CastAsync(session, 1410);

        Assert.Contains(cast, m => Is(m, "spell.outcome") && m.Contains("\"spellId\":1410"));
        Assert.Contains(cast, m => Is(m, "aura.applied") && m.Contains("\"unitId\":\"Creature:1\"")
            && m.Contains("\"remaining\":5000"));
    }

    [Fact]
    public async Task AStunnedCreatureDoesNotSwingBackUntilTheStunEnds()
    {
        var spell = new SpellDefinition(1410, "spell-1410", 0, 1000, 0f, PowerCost: 10,
            EffectMeleeWeaponHit: true, EffectMeleeWeaponDamagePercent: 100, EffectTarget: SpellEffectTargets.SingleEnemy,
            ExtraEffects: [Extra(1410, SpellEffectTargets.SingleEnemy) with { AuraStun = true }]);
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1410] = spell });
        var cast = DateTimeOffset.UtcNow;
        await CastAsync(session, 1410);

        var stunned = new List<string>();
        for (int second = 1; second <= 4; second++) stunned.AddRange(session.TickCombat(cast + TimeSpan.FromSeconds(second)));
        var free = new List<string>();
        for (int second = 6; second <= 9; second++) free.AddRange(session.TickCombat(cast + TimeSpan.FromSeconds(second)));

        static bool CreatureSwings(string m) => Is(m, "attack.swingResult") && m.Contains("\"attackerId\":\"Creature:1\"");
        Assert.DoesNotContain(stunned, CreatureSwings);
        Assert.Contains(free, CreatureSwings);
    }

    [Fact]
    public async Task AFurtherEffectNamingTheCasterLandsOnTheCasterWhileTheBlowLandsOnTheCreature()
    {
        var spell = new SpellDefinition(1420, "spell-1420", 0, 1000, 30f, PowerCost: 10, EffectDamage: 15,
            EffectTarget: SpellEffectTargets.SingleEnemy,
            ExtraEffects: [Extra(1420, SpellEffectTargets.Self) with { AuraAttributeId = AttributeIds.Strength, AuraAttributeAmount = 5 }]);
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1420] = spell });

        var cast = await CastAsync(session, 1420);

        Assert.Contains(cast, m => Is(m, "spell.outcome") && m.Contains("\"targetId\":\"Creature:1\""));
        Assert.Contains(cast, m => Is(m, "aura.applied") && m.Contains("\"unitId\":\"player:char-a\"")
            && m.Contains("\"harmful\":0"));
        Assert.DoesNotContain(cast, m => Is(m, "aura.applied") && m.Contains("\"unitId\":\"Creature:1\""));
    }

    [Fact]
    public async Task AMissedSpellLeavesItsLastingEffectsOffTheTarget()
    {
        var spell = new SpellDefinition(1430, "spell-1430", 0, 1000, 30f, PowerCost: 10, EffectDamage: 15,
            ExtraEffects: [Extra(1430, SpellEffectTargets.SingleEnemy) with { AuraSlowPercent = 50 }]);
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1430] = spell },
            spellRoll: () => SpellService.ResistChance + SpellService.MissChance / 2);

        var cast = await CastAsync(session, 1430);

        Assert.Contains(cast, m => Is(m, "spell.outcome") && m.Contains("\"result\":\"miss\""));
        Assert.DoesNotContain(cast, m => Is(m, "aura.applied"));
    }

    [Fact]
    public async Task ASpellWhoseLeadThisServerDoesNotApplyStillDoesTheRest()
    {
        var spell = new SpellDefinition(1440, "spell-1440", 0, 1000, 30f, PowerCost: 10,
            AuraSpellModifierType: 3, AuraSpellModifierAmount: 5, EffectTarget: SpellEffectTargets.SingleEnemy,
            ExtraEffects: [Extra(1440, SpellEffectTargets.SingleEnemy) with { AuraStun = true }]);
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1440] = spell });

        var cast = await CastAsync(session, 1440);

        Assert.Single(cast, m => Is(m, "spell.released"));
        Assert.Contains(cast, m => Is(m, "aura.applied") && m.Contains("\"unitId\":\"Creature:1\""));
        Assert.Contains(cast, m => Is(m, "spell.cooldownStarted"));
    }

    [Fact]
    public async Task ASpellWhoseRowNamesTheCasterLandsOnTheCasterWithACreatureSelected()
    {
        var spell = new SpellDefinition(1450, "spell-1450", 0, 1000, 0f, PowerCost: 10, DurationMs: 8000,
            AuraSpeedupPercent: 30, EffectTarget: SpellEffectTargets.Self);
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1450] = spell });

        var cast = await CastAsync(session, 1450);

        Assert.Contains(cast, m => Is(m, "aura.applied") && m.Contains("\"unitId\":\"player:char-a\""));
        Assert.DoesNotContain(cast, m => Is(m, "aura.applied") && m.Contains("\"unitId\":\"Creature:1\""));
    }

    [Fact]
    public async Task AnInstantTriggerFiresTheSpellItNames()
    {
        var stun = new SpellDefinition(1470, "spell-1470", 0, 0, 30f, DurationMs: 3000, AuraStun: true,
            EffectTarget: SpellEffectTargets.SingleEnemy);
        var spell = new SpellDefinition(1460, "spell-1460", 0, 1000, 30f, PowerCost: 10, EffectDamage: 12,
            EffectTriggerSpellId: 1470, EffectTriggerChancePercent: 100);
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1460] = spell, [1470] = stun },
            spellRoll: () => 0.5);

        var cast = await CastAsync(session, 1460);

        Assert.Contains(cast, m => Is(m, "spell.outcome") && m.Contains("\"spellId\":1460"));
        Assert.Contains(cast, m => Is(m, "aura.applied") && m.Contains("\"spellId\":1470")
            && m.Contains("\"unitId\":\"Creature:1\""));
    }

    [Fact]
    public async Task ACooldownOnlySpellWithNoPowerCostCasts()
    {
        var spell = new SpellDefinition(1480, "spell-1480", 0, 25000, 0f,
            EffectMeleeWeaponHit: true, EffectMeleeWeaponDamagePercent: 100);
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1480] = spell });

        var cast = await CastAsync(session, 1480);

        Assert.Contains(cast, m => Is(m, "spell.outcome") && m.Contains("\"spellId\":1480"));
    }

    [Fact]
    public async Task ARangedShotThatSlowsSlowsTheCreatureWhenTheArrowLands()
    {
        var spell = new SpellDefinition(1490, "spell-1490", 0, 1000, 20f, PowerCost: 10,
            EffectRangedWeaponHit: true, EffectRangedWeaponDamagePercent: 100,
            ExtraEffects: [Extra(1490, SpellEffectTargets.SingleEnemy) with { AuraSlowPercent = 40 }]);
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1490] = spell });

        var cast = await CastAsync(session, 1490);
        Assert.Contains(cast, m => Is(m, "attack.shotFired"));
        Assert.DoesNotContain(cast, m => Is(m, "aura.applied"));

        var landed = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));
        Assert.Contains(landed, m => Is(m, "attack.shotResult"));
        Assert.Contains(landed, m => Is(m, "aura.applied") && m.Contains("\"spellId\":1490")
            && m.Contains("\"unitId\":\"Creature:1\""));
    }

    [Fact]
    public async Task APotionOfTheCastersOwnPowerGivesItBack()
    {
        var spend = new SpellDefinition(1500, "spell-1500", 0, 0, 30f, PowerCost: 60, EffectDamage: 5);
        var potion = new SpellDefinition(1510, "spell-1510", 0, 0, 0f, EffectPowerRestore: 280, EffectPowerKind: PowerKinds.Vigor,
            EffectTarget: SpellEffectTargets.Self);
        var items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
        {
            [100] = GameContent.Development.Items[100] with { OnUseSpellId = 1510 },
        };
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1500] = spend, [1510] = potion }, items: items);
        await CastAsync(session, 1500);
        session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));

        var used = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 6, new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));
        var flushed = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        Assert.Contains(used, m => Is(m, "spell.released") && m.Contains("\"spellId\":1510"));
        Assert.Contains(flushed, m => m.Contains($"\"{WireCodes.UnitValue.Power}\":{SpellService.PlayerMaxPower}"));
    }

    [Fact]
    public async Task APotionOfAnotherClasssPowerGivesNothing()
    {
        var potion = new SpellDefinition(1520, "spell-1520", 0, 0, 0f, EffectPowerRestore: 280, EffectPowerKind: PowerKinds.Mana,
            EffectTarget: SpellEffectTargets.Self);
        var spend = new SpellDefinition(1500, "spell-1500", 0, 0, 30f, PowerCost: 60, EffectDamage: 5);
        var items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
        {
            [100] = GameContent.Development.Items[100] with { OnUseSpellId = 1520 },
        };
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1500] = spend, [1520] = potion }, items: items);
        await CastAsync(session, 1500);
        session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 6, new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));
        var flushed = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(flushed, m => m.Contains($"\"{WireCodes.UnitValue.Power}\":{SpellService.PlayerMaxPower}"));
    }

    [Fact]
    public async Task ABuffItemLandsOnItsUserWithNothingSelected()
    {
        var buff = new SpellDefinition(1530, "spell-1530", 0, 0, 0f, DurationMs: 60000,
            AuraAttributeId: AttributeIds.Strength, AuraAttributeAmount: 8, EffectTarget: SpellEffectTargets.Self);
        var items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
        {
            [100] = GameContent.Development.Items[100] with { OnUseSpellId = 1530 },
        };
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1530] = buff }, items: items);

        var used = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 6, new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

        Assert.Contains(used, m => Is(m, "aura.applied") && m.Contains("\"unitId\":\"player:char-a\""));
    }

    [Fact]
    public async Task ARangedAttackSpeedBuffIsHeldOnTheCaster()
    {
        var spell = new SpellDefinition(1540, "spell-1540", 0, 1000, 0f, PowerCost: 10, DurationMs: 6000,
            AuraRangeAttackSpeedPercent: 20, EffectTarget: SpellEffectTargets.Self);
        var session = await SessionAsync(new Dictionary<uint, SpellDefinition> { [1540] = spell });

        var cast = await CastAsync(session, 1540);
        var ended = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(7));

        Assert.Contains(cast, m => Is(m, "aura.applied") && m.Contains("\"unitId\":\"player:char-a\"") && m.Contains("\"harmful\":0"));
        Assert.Contains(ended, m => Is(m, "aura.ended") && m.Contains("\"spellId\":1540"));
    }
}
