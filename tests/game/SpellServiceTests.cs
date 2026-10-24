using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class SpellServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<uint, SpellDefinition> Spells = new Dictionary<uint, SpellDefinition>
    {
        [100] = new(100, "spell-100", CastTimeMs: 0, CooldownMs: 3000, MaxRange: 30f, PowerCost: 10, EffectDamage: 20),
        [101] = new(101, "spell-101", CastTimeMs: 1500, CooldownMs: 5000, MaxRange: 30f, PowerCost: 15, EffectDamage: 30),
        [102] = new(102, "spell-102", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f),
        [103] = new(103, "spell-103", CastTimeMs: 0, CooldownMs: 1000, MaxRange: 30f, PowerCost: 5, EffectHeal: 40),
        [104] = new(104, "spell-104", CastTimeMs: 500, CooldownMs: 3000, MaxRange: 25f, PowerCost: 30,
            EffectRangedWeaponHit: true, EffectRangedWeaponDamageBonus: 15, EffectRangedWeaponDamagePercent: 100,
            EffectTriggerSpellId: 5342, EffectTriggerChancePercent: 100),
        [105] = new(105, "spell-105", CastTimeMs: 500, CooldownMs: 3000, MaxRange: 25f, PowerCost: 30,
            EffectTriggerSpellId: 5342, EffectTriggerChancePercent: 100),
        [106] = new(106, "spell-106", CastTimeMs: 0, CooldownMs: 1500, MaxRange: 0f, PowerCost: 25,
            EffectMeleeWeaponHit: true, EffectMeleeWeaponSlot: EquipmentSlots.MainHand,
            EffectMeleeWeaponDamageBonus: 10, EffectMeleeWeaponDamagePercent: 110,
            EffectTriggerSpellId: 5811, EffectTriggerChancePercent: 100),
        [107] = new(107, "spell-107", CastTimeMs: 0, CooldownMs: 1500, MaxRange: 0f, PowerCost: 25,
            EffectMeleeWeaponHit: true, EffectMeleeWeaponSlot: EquipmentSlots.OffHand,
            EffectMeleeWeaponDamageBonus: 0, EffectMeleeWeaponDamagePercent: 150),
        [108] = new(108, "spell-108", CastTimeMs: 0, CooldownMs: 25000, MaxRange: 0f, PowerCost: 15,
            DurationMs: 14000, AuraStealth: true),
    };

    [Fact]
    public void TryBeginCast_AcceptsAHealOnlySpell()
    {
        var spells = new SpellService([103]);

        var started = spells.TryBeginCast(103, Spells, "Creature:1", Now, out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(started);
        Assert.Equal(SpellService.PlayerMaxPower - 5, spells.Power);
    }

    [Fact]
    public void TryBeginCast_RefusesAnUnknownSpell()
    {
        var spells = new SpellService(knownSpells: []);

        var started = spells.TryBeginCast(100, Spells, "Creature:1", Now, out var refusal);

        Assert.Null(started);
        Assert.Equal(SpellRefusalReason.UnknownSpell, refusal!.Error);
    }

    [Fact]
    public void TryBeginCast_RefusesASpellWithNoEffectData()
    {
        var spells = new SpellService([102]);

        var started = spells.TryBeginCast(102, Spells, "Creature:1", Now, out var refusal);

        Assert.Null(started);
        Assert.Equal(SpellRefusalReason.NoEffectData, refusal!.Error);
    }

    [Fact]
    public void TryBeginCast_AcceptsARangedWeaponDamageSpell()
    {
        var spells = new SpellService([104]);

        var started = spells.TryBeginCast(104, Spells, "Creature:1", Now, out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(started);
        Assert.False(started.IsInstant);
        Assert.Equal(SpellService.PlayerMaxPower, spells.Power);
    }

    [Fact]
    public void TryBeginCast_AcceptsAnInstantTriggerOnlySpell()
    {
        var spells = new SpellService([105]);

        var started = spells.TryBeginCast(105, Spells, "Creature:1", Now, out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(started);
    }

    [Fact]
    public void TryBeginCast_AcceptsASpellWhoseOnlyAppliedEffectIsAFurtherOne()
    {
        var spell = new SpellDefinition(1200, "spell-1200", 0, 5000, 20f, PowerCost: 10,
            AuraSpellModifierType: 3, AuraSpellModifierAmount: 5,
            ExtraEffects: [new SpellDefinition(1200, "spell-1200", 0, 0, 20f, AuraStun: true, EffectTarget: SpellEffectTargets.SingleEnemy)]);
        var spells = new SpellService([1200]);

        var started = spells.TryBeginCast(1200, new Dictionary<uint, SpellDefinition> { [1200] = spell }, "Creature:1", Now, out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(started);
        Assert.False(SpellService.HasResolvableEffect(spell with { ExtraEffects = null }));
    }

    [Fact]
    public void TryBeginCast_CastsASpellWithACooldownAndNoPowerCostForFree()
    {
        var spell = new SpellDefinition(1210, "spell-1210", 0, 25000, 0f, EffectMeleeWeaponHit: true);
        var spells = new SpellService([1210]);

        var started = spells.TryBeginCast(1210, new Dictionary<uint, SpellDefinition> { [1210] = spell }, "Creature:1", Now, out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(started);
        Assert.Equal(SpellService.PlayerMaxPower, spells.Power);
    }

    [Fact]
    public void TryBeginCast_RefusesAPassiveSpell()
    {
        var spell = new SpellDefinition(1220, "spell-1220", 0, 0, 0f, AuraAttributeId: AttributeIds.Strength, AuraAttributeAmount: 3);
        var spells = new SpellService([1220]);

        var started = spells.TryBeginCast(1220, new Dictionary<uint, SpellDefinition> { [1220] = spell }, "Creature:1", Now, out var refusal);

        Assert.Null(started);
        Assert.Equal(SpellRefusalReason.NoEffectData, refusal!.Error);
    }

    [Fact]
    public void RestorePower_GivesBackOnlyTheKindTheClassSpends()
    {
        var spells = new SpellService([100]);
        spells.EnterAsCharacter(PlayerClassIds.Warrior, 1);
        spells.TryBeginCast(100, Spells, "Creature:1", Now, out _);
        var spent = spells.Power;

        Assert.Null(spells.RestorePower(PowerKinds.Mana, 50));
        Assert.Equal(spent, spells.Power);
        Assert.Equal(SpellService.PlayerMaxPower - spent, spells.RestorePower(PowerKinds.Morale, 500));
        Assert.Equal(SpellService.PlayerMaxPower, spells.Power);
    }

    [Fact]
    public void TryBeginCast_AcceptsAMainhandWeaponDamageSpell()
    {
        var spells = new SpellService([106]);

        var started = spells.TryBeginCast(106, Spells, "Creature:1", Now, out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(started);
        Assert.True(started.IsInstant);
        Assert.Equal(SpellService.PlayerMaxPower - 25, spells.Power);
    }

    [Fact]
    public void TryBeginCast_AcceptsAnOffhandWeaponDamageSpellWithoutKnowingWhatIsWorn()
    {
        var spells = new SpellService([107]);

        var started = spells.TryBeginCast(107, Spells, "Creature:1", Now, out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(started);
    }

    [Fact]
    public void TryBeginCast_AcceptsAStealthSpell()
    {
        var spells = new SpellService([108]);

        var started = spells.TryBeginCast(108, Spells, targetId: null, Now, out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(started);
        Assert.True(started.IsInstant);
        Assert.Equal(SpellService.PlayerMaxPower - 15, spells.Power);
    }

    [Fact]
    public void TryBeginCast_RefusesInsufficientPower()
    {
        var costly = new Dictionary<uint, SpellDefinition>
        {
            [200] = new(200, "spell-200", 0, 0, 30f, PowerCost: SpellService.PlayerMaxPower + 1, EffectDamage: 1),
        };
        var spells = new SpellService([200]);

        var started = spells.TryBeginCast(200, costly, "Creature:1", Now, out var refusal);

        Assert.Null(started);
        Assert.Equal(SpellRefusalReason.InsufficientPower, refusal!.Error);
    }

    [Fact]
    public void TryBeginCast_ResolvesInstantlyForAZeroCastTimeSpell()
    {
        var spells = new SpellService([100]);

        var started = spells.TryBeginCast(100, Spells, "Creature:1", Now, out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(started);
        Assert.True(started.IsInstant);
        Assert.Equal(SpellService.PlayerMaxPower - 10, spells.Power);
        Assert.Null(spells.TakeFinished(Now));
    }

    [Fact]
    public void TryBeginCast_RefusesAnotherSpellWhileACastIsUnderWay_AndKeepsTheFirst()
    {
        var spells = new SpellService([100, 101]);

        Assert.NotNull(spells.TryBeginCast(101, Spells, "Creature:1", Now, out _));
        var second = spells.TryBeginCast(100, Spells, "Creature:1", Now.AddMilliseconds(500), out var refusal);

        Assert.Null(second);
        Assert.Equal(SpellRefusalReason.AlreadyCasting, refusal!.Error);
        var ready = spells.TakeFinished(Now.AddMilliseconds(1500));
        Assert.NotNull(ready);
        Assert.Equal(101u, ready.SpellId);
    }

    [Fact]
    public void TryBeginCast_DefersACastTimeSpellUntilItElapses_WithoutChargingIt()
    {
        var spells = new SpellService([101]);

        var started = spells.TryBeginCast(101, Spells, "Creature:1", Now, out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(started);
        Assert.False(started.IsInstant);
        Assert.Equal(SpellService.PlayerMaxPower, spells.Power);
        Assert.Null(spells.TakeFinished(Now.AddMilliseconds(1000)));

        var ready = spells.TakeFinished(Now.AddMilliseconds(1500));

        Assert.NotNull(ready);
        Assert.Equal(101u, ready.SpellId);
        Assert.Equal("Creature:1", ready.TargetId);
        Assert.Equal(SpellService.PlayerMaxPower, spells.Power);
    }

    [Fact]
    public void Spend_ChargesAFinishedCastsPowerAndStartsItsCooldown()
    {
        var spells = new SpellService([101]);
        spells.TryBeginCast(101, Spells, "Creature:1", Now, out _);
        var finished = spells.TakeFinished(Now.AddMilliseconds(1500))!;

        spells.Spend(finished, Now.AddMilliseconds(1500));

        Assert.Equal(SpellService.PlayerMaxPower - 15, spells.Power);
        spells.TryBeginCast(101, Spells, "Creature:1", Now.AddMilliseconds(2000), out var refusal);
        Assert.Equal(SpellRefusalReason.OnCooldown, refusal!.Error);
    }

    [Fact]
    public void TakeFinished_LeavesAnUnspentCastFreeToCastAgain()
    {
        var spells = new SpellService([101]);
        spells.TryBeginCast(101, Spells, "Creature:1", Now, out _);
        spells.TakeFinished(Now.AddMilliseconds(1500));

        var again = spells.TryBeginCast(101, Spells, "Creature:1", Now.AddMilliseconds(1600), out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(again);
    }

    [Fact]
    public void TryBeginCast_RefusesWhileOnCooldown()
    {
        var spells = new SpellService([100]);
        spells.TryBeginCast(100, Spells, "Creature:1", Now, out _);

        var started = spells.TryBeginCast(100, Spells, "Creature:1", Now.AddMilliseconds(500), out var refusal);

        Assert.Null(started);
        Assert.Equal(SpellRefusalReason.OnCooldown, refusal!.Error);
    }

    [Fact]
    public void TryBeginCast_AllowsAgainOnceTheCooldownElapses()
    {
        var spells = new SpellService([100]);
        spells.TryBeginCast(100, Spells, "Creature:1", Now, out _);

        var started = spells.TryBeginCast(100, Spells, "Creature:1", Now.AddMilliseconds(3000), out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(started);
    }

    [Fact]
    public void CancelPending_StopsAnInFlightCastWithoutSpendingItsCost()
    {
        var spells = new SpellService([101]);
        spells.TryBeginCast(101, Spells, "Creature:1", Now, out _);

        var cancelled = spells.CancelPending();

        Assert.True(cancelled);
        Assert.Equal(SpellService.PlayerMaxPower, spells.Power);
        Assert.Null(spells.TakeFinished(Now.AddMilliseconds(1500)));
    }

    [Fact]
    public void CancelPending_ReturnsFalseWhenNothingIsPending()
    {
        Assert.False(new SpellService([]).CancelPending());
    }

    [Fact]
    public void PowerRegen_RestoresOverTimeUpToTheCap()
    {
        var spells = new SpellService([100]);
        spells.TryBeginCast(100, Spells, "Creature:1", Now, out _);

        spells.PowerRegen(Now);
        var regen = spells.PowerRegen(Now + SpellService.PowerRegenInterval);

        Assert.Equal(SpellService.PlayerMaxPower, regen);
    }

    [Fact]
    public void PowerRegen_ReturnsNullOnceFull()
    {
        Assert.Null(new SpellService([]).PowerRegen(Now));
    }

    private static SpellService Spent(int classId, int level, int baseIntellect = 0)
    {
        var spells = new SpellService([300]);
        spells.EnterAsCharacter(classId, level, baseIntellect: baseIntellect);
        spells.PowerRegen(Now);
        var costly = new Dictionary<uint, SpellDefinition> { [300] = new(300, "spell-300", 0, 0, 30f, PowerCost: 300, EffectDamage: 1) };
        spells.TryBeginCast(300, costly, "Creature:1", Now, out _);
        return spells;
    }

    [Fact]
    public void PowerRegen_ManaTickFollowsTheMageFormulaAndHalvesInCombat()
    {
        var spells = Spent(PlayerClassIds.Mage, 1, baseIntellect: 25);
        var before = spells.Power;

        Assert.Equal(before + 27, spells.PowerRegen(Now + SpellService.PowerRegenInterval));
        Assert.Equal(before + 27 + 13, spells.PowerRegen(Now + SpellService.PowerRegenInterval * 2, inCombat: true));
    }

    [Fact]
    public void PowerRegen_ManaTickFollowsTheMonkFormula()
    {
        var spells = Spent(PlayerClassIds.Monk, 1, baseIntellect: 20);
        var before = spells.Power;

        Assert.Equal(before + 33, spells.PowerRegen(Now + SpellService.PowerRegenInterval));
    }

    [Fact]
    public void PowerRegen_ManaRegenRatingIncreasesTheAmountRestored()
    {
        var spells = Spent(PlayerClassIds.Mage, 1, baseIntellect: 25);
        var before = spells.Power;

        Assert.Equal(
            before + 27 + 10 * SpellService.PowerRegenPerRating,
            spells.PowerRegen(Now + SpellService.PowerRegenInterval, manaRegenRating: 10));
    }

    [Fact]
    public void PowerRegen_SpendingDoesNotDelayTheNextTick()
    {
        var spells = new SpellService([100]);
        spells.EnterAsCharacter(PlayerClassIds.Mage, 1, baseIntellect: 25);
        spells.PowerRegen(Now);
        spells.TryBeginCast(100, Spells, "Creature:1", Now + TimeSpan.FromSeconds(4), out _);

        Assert.NotNull(spells.PowerRegen(Now + SpellService.PowerRegenInterval));
    }

    [Fact]
    public void Morale_StartsAtRestAndDriftsTowardItOnlyOutOfCombat()
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(PlayerClassIds.Warrior, 10);
        Assert.Equal(SpellService.MoraleRest, spells.Power);

        spells.GainMoraleFromHitTaken(50 * 10 / 6 * 3);
        var raised = spells.Power;
        Assert.True(raised > SpellService.MoraleRest);

        Assert.Null(spells.PowerRegen(Now, inCombat: true));
        Assert.Null(spells.PowerRegen(Now + SpellService.FastPowerRegenInterval, inCombat: true));
        Assert.Equal(raised - 2, spells.PowerRegen(Now + SpellService.FastPowerRegenInterval * 2));
        Assert.Equal(raised, spells.Power + 2);
    }

    [Fact]
    public void Morale_DriftLandsExactlyOnRestFromAboveAndBelow()
    {
        var above = new SpellService([]);
        above.EnterAsCharacter(PlayerClassIds.Warrior, 1);
        above.RestorePower(PowerKinds.Morale, 53);
        var tick = Now;
        above.PowerRegen(tick);
        while (above.PowerRegen(tick += SpellService.FastPowerRegenInterval) is not null) { }
        Assert.Equal(SpellService.MoraleRest, above.Power);

        var below = new SpellService([]);
        below.EnterAsCharacter(PlayerClassIds.Warrior, 1);
        below.LoseVigorFromHitTaken(1);
        below.GainMoraleFromAvoid();
        Assert.Equal(24, below.Power);
        below.RestorePower(PowerKinds.Morale, 0);
        for (var i = 0; i <= 3; i++) below.PowerRegen(Now + SpellService.FastPowerRegenInterval * i);
        Assert.Equal(20, below.Power);
    }

    [Theory]
    [InlineData(50, 10, 6)]
    [InlineData(45, 10, 5)]
    [InlineData(12, 60, 1)]
    public void Morale_GainsFromHitsTakenByDamagePerLevel(int damage, int level, int gain)
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(PlayerClassIds.Warrior, level);

        spells.GainMoraleFromHitTaken(damage);

        Assert.Equal(SpellService.MoraleRest + gain, spells.Power);
    }

    [Theory]
    [InlineData(100, true, false, 17)]
    [InlineData(100, false, false, 13)]
    [InlineData(20, false, true, 5)]
    public void Morale_GainsFromHitsLandedByDamageAndSwingTime(int damage, bool critical, bool offHand, int gain)
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(PlayerClassIds.Warrior, 10);

        spells.GainMoraleFromHitDealt(damage, TimeSpan.FromSeconds(2), critical, offHand);

        Assert.Equal(SpellService.MoraleRest + gain, spells.Power);
    }

    [Fact]
    public void Morale_AnAvoidIsWorthFourWhateverTheLevel()
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(PlayerClassIds.Warrior, 60);

        spells.GainMoraleFromAvoid();

        Assert.Equal(SpellService.MoraleRest + 4, spells.Power);
    }

    [Fact]
    public void Morale_StopsAtTheMax()
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(PlayerClassIds.Warrior, 1);
        spells.RestorePower(PowerKinds.Morale, 78);

        spells.GainMoraleFromAvoid();

        Assert.Equal(100, spells.Power);
    }

    [Fact]
    public void Morale_ClassesThatDoNotUseItGainNothing()
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(PlayerClassIds.Mage, 10);
        var before = spells.Power;

        Assert.Null(spells.GainMoraleFromHitTaken(100));
        Assert.Null(spells.GainMoraleFromAvoid());
        Assert.Equal(before, spells.Power);
    }

    [Fact]
    public void Vigor_RegeneratesTenASecondInOrOutOfCombat()
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(PlayerClassIds.Ranger, 10);
        spells.LoseVigorFromHitTaken(10_000);
        Assert.Equal(SpellService.VigorFloor, spells.Power);

        spells.PowerRegen(Now);
        Assert.Equal(30, spells.PowerRegen(Now + SpellService.FastPowerRegenInterval, inCombat: true));
        Assert.Equal(40, spells.PowerRegen(Now + SpellService.FastPowerRegenInterval * 2));
    }

    [Theory]
    [InlineData(60, 10, 4)]
    [InlineData(45, 10, 3)]
    [InlineData(15, 60, 1)]
    public void Vigor_LosesByDamagePerLevelAndNeverBelowTheFloor(int damage, int level, int loss)
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(PlayerClassIds.Ranger, level);

        spells.LoseVigorFromHitTaken(damage);

        Assert.Equal(100 - loss, spells.Power);
    }

    [Fact]
    public void Vigor_AHitAtTheFloorChangesNothing()
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(PlayerClassIds.Ranger, 10);
        spells.LoseVigorFromHitTaken(100_000);

        Assert.Null(spells.LoseVigorFromHitTaken(500));
        Assert.Equal(SpellService.VigorFloor, spells.Power);
    }

    [Fact]
    public void Vigor_DealingAndAvoidingChangeNothing()
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(PlayerClassIds.Ranger, 10);

        Assert.Null(spells.GainMoraleFromHitDealt(100, TimeSpan.FromSeconds(2), true, false));
        Assert.Null(spells.GainMoraleFromAvoid());
        Assert.Equal(100, spells.Power);
    }

    [Fact]
    public void EffectiveMaxPower_IgnoresWisdomForTheFlatPool()
    {
        Assert.Equal(SpellService.PlayerMaxPower, new SpellService([]).EffectiveMaxPower(50));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void EffectiveMaxPower_IgnoresWisdomForTheMoraleAndEnergyClasses(int classId)
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(classId, level: 40, baseIntellect: 10);

        Assert.Equal(SpellService.PlayerMaxPower, spells.EffectiveMaxPower(50));
    }

    [Fact]
    public void EffectiveMaxPower_NeverDropsToZeroOrBelow()
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(classId: 2, level: 1);

        Assert.Equal(1, spells.EffectiveMaxPower(-100_000));
    }

    [Fact]
    public void EffectiveMaxPower_ZeroWisdomLeavesTheBaseMaxUnchanged()
    {
        Assert.Equal(SpellService.PlayerMaxPower, new SpellService([]).EffectiveMaxPower(0));
    }

    [Fact]
    public void EnterAsCharacter_UsesTheClassManaCurveForAManaUsingClass()
    {
        var spells = new SpellService([]);

        spells.EnterAsCharacter(classId: 2, level: 1);

        Assert.Equal(PlayerStatCurves.BaseMaxPower(2, 1, fallback: -1), spells.EffectiveMaxPower(0));
        Assert.NotEqual(SpellService.PlayerMaxPower, spells.EffectiveMaxPower(0));
    }

    [Fact]
    public void EffectiveMaxPower_GivesAManaClassFifteenManaPerPointOfWisdomFromAnySource()
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(classId: 2, level: 20, baseIntellect: 25);

        Assert.Equal(spells.EffectiveMaxPower(0) + 10 * SpellService.ManaPerWisdom, spells.EffectiveMaxPower(10));
    }

    [Theory]
    [InlineData(2, 20, 25, 1548)]
    [InlineData(2, 60, 25, 5818)]
    [InlineData(4, 13, 20, 926)]
    [InlineData(4, 52, 20, 4246)]
    public void EffectiveMaxPower_CountsTheWisdomACharacterGainsByLevelling(
        int classId, int level, int levelOneIntellect, int expected)
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(classId, level, baseIntellect: levelOneIntellect);

        Assert.Equal(expected, spells.EffectiveMaxPower(0));
    }

    [Fact]
    public void EnterAsCharacter_KeepsTheSyntheticFlatPoolForANonManaClass()
    {
        var spells = new SpellService([]);

        spells.EnterAsCharacter(classId: 1, level: 50);

        Assert.Equal(SpellService.PlayerMaxPower, spells.EffectiveMaxPower(0));
    }

    [Fact]
    public void EnterAsCharacter_FullyRestoresPowerToTheNewEffectiveMax()
    {
        var spells = new SpellService([100]);
        spells.TryBeginCast(100, Spells, "Creature:1", Now, out _);

        spells.EnterAsCharacter(classId: 2, level: 20);

        Assert.Equal(spells.EffectiveMaxPower(0), spells.Power);
    }

    [Fact]
    public void EnterAsCharacter_ReplacesTheKnownSpellsWhenGivenSome()
    {
        var spells = new SpellService([103]);

        spells.EnterAsCharacter(classId: 0, level: 1, knownSpells: [100]);

        Assert.NotNull(spells.TryBeginCast(100, Spells, "Creature:1", Now, out var acceptedRefusal));
        Assert.Null(acceptedRefusal);
        Assert.Null(spells.TryBeginCast(103, Spells, "Creature:1", Now, out var refusedRefusal));
        Assert.Equal(SpellRefusalReason.UnknownSpell, refusedRefusal!.Error);
    }

    [Fact]
    public void EnterAsCharacter_LeavesTheKnownSpellsAloneWhenNoneAreGiven()
    {
        var spells = new SpellService([103]);

        spells.EnterAsCharacter(classId: 4, level: 2);

        Assert.NotNull(spells.TryBeginCast(103, Spells, "Creature:1", Now, out var refusal));
        Assert.Null(refusal);
    }

    [Fact]
    public void RollDamageOutcome_PartitionsOneRollIntoResistedMissedOrHit()
    {
        var resisted = new SpellService([], rollUnit: () => 0.0);
        var missed = new SpellService([], rollUnit: () => SpellService.ResistChance);
        var hit = new SpellService([], rollUnit: () => SpellService.ResistChance + SpellService.MissChance);

        Assert.Equal(SpellCastOutcome.Resisted, resisted.RollDamageOutcome());
        Assert.Equal(SpellCastOutcome.Miss, missed.RollDamageOutcome());
        Assert.Equal(SpellCastOutcome.Hit, hit.RollDamageOutcome());
    }

    [Theory]
    [InlineData(0.0, 20, true)]
    [InlineData(0.19, 20, true)]
    [InlineData(0.20, 20, false)]
    [InlineData(0.99, 100, true)]
    public void RollTriggerChance_ComparesTheInjectedRollAgainstThePercent(double roll, int percent, bool expected)
    {
        var service = new SpellService([], rollUnit: () => roll);

        Assert.Equal(expected, service.RollTriggerChance(percent));
    }

    [Fact]
    public void RollSpellCrit_IsAFlatChancePerPointCappedAtMax()
    {
        var underCap = new SpellService([], rollUnit: () => 100 * SpellService.SpellCritChancePerRating - 0.001);
        Assert.True(underCap.RollSpellCrit(100));

        var atCap = new SpellService([], rollUnit: () => SpellService.MaxSpellCritChance - 0.001);
        Assert.True(atCap.RollSpellCrit(100_000));
    }

    [Fact]
    public void RollSpellCrit_ZeroRatingNeverCrits()
    {
        var service = new SpellService([], rollUnit: () => 0.0);

        Assert.False(service.RollSpellCrit(0));
    }

    [Fact]
    public void RollSpellCrit_NegativeRatingNeverGoesBelowZero()
    {
        var service = new SpellService([], rollUnit: () => 0.0);

        Assert.False(service.RollSpellCrit(-100_000));
    }

    [Fact]
    public void RestoreFullPower_SetsPowerBackToTheEffectiveMax()
    {
        var service = new SpellService([103]);
        service.TryBeginCast(103, Spells, "Creature:1", Now, out _);
        Assert.Equal(SpellService.PlayerMaxPower - 5, service.Power);

        service.RestoreFullPower();

        Assert.Equal(SpellService.PlayerMaxPower, service.Power);
    }

    [Fact]
    public void RestoreFullPower_HonoursAnActiveWisdomBonusTheSameWayEffectiveMaxPowerAlreadyDoes()
    {
        var service = new SpellService([]);

        service.RestoreFullPower(wisdomBonus: 50);

        Assert.Equal(service.EffectiveMaxPower(50), service.Power);
    }

    [Fact]
    public void ANewCaster_HasAPoolToCastFrom_BuiltOnTheirOwnIntellect()
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(classId: 2, level: 1, baseIntellect: 25);

        Assert.Equal(PlayerStatCurves.BaseMaxPower(2, 1, fallback: 0) + 25 * SpellService.ManaPerWisdom, spells.EffectiveMaxPower(0));
        Assert.Equal(spells.EffectiveMaxPower(0), spells.Power);
    }
}
