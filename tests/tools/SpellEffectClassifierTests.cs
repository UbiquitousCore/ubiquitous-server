using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

public sealed class SpellEffectClassifierTests
{
    private static readonly SpellDefinition Bare = new(7, "spell-7", 0, 3000, 20f, PowerCost: 20, DurationMs: 9000);

    private static SpellEffectDefinition Effect(
        uint id, int type, int subType, int baseValue = 0, int spread = 0, uint argument = 0, int target = 2,
        uint targetCondition = 0, uint casterCondition = 0) =>
        new(id, type, subType, School: 0, baseValue, spread, argument, target, targetCondition, casterCondition);

    private static SpellDefinition Classify(params (int Column, SpellEffectDefinition? Effect)[] slots) =>
        SpellEffectClassifier.ApplySlots(Bare, slots);

    [Fact]
    public void ALeapAheadOfDamageAndAStunKeepsTheLeapOnTheLeadAndTheRestAsFurtherEffects()
    {
        var spell = Classify(
            (33, Effect(1, 0, SpellEffectColumns.LeapToTargetSubType, argument: 1)),
            (34, Effect(2, 0, SpellEffectColumns.DirectDamageSubType, baseValue: 50)),
            (35, Effect(3, 1, SpellEffectColumns.StunSubType)));

        Assert.True(spell.EffectLeapToTarget);
        Assert.Null(spell.EffectDamage);
        Assert.Contains(spell.ExtraEffects!, extra => extra.EffectDamage == 50);
        Assert.Contains(spell.ExtraEffects!, extra => extra.AuraStun);
        Assert.DoesNotContain(spell.ExtraEffects!, extra => extra.EffectLeapToTarget);
    }

    [Fact]
    public void AMountKeepsItsModelSeatAndSpeedOnTheLead()
    {
        var spell = Classify(
            (33, Effect(1, 1, SpellEffectColumns.MountSubType, baseValue: 30, argument: 3060, target: 1)),
            (34, Effect(2, 1, SpellEffectColumns.MountSpeedupSubType, baseValue: 20, target: 1)));

        Assert.Equal(3060u, spell.AuraMountId);
        Assert.Equal(30, spell.AuraMountKeepSeatPercent);
        Assert.Equal(20, spell.AuraMountSpeedupPercent);
        Assert.Null(spell.ExtraEffects);
    }

    [Fact]
    public void ALessonTeachesEachOfItsSpells()
    {
        var spell = Classify(
            (33, Effect(1, 0, SpellEffectColumns.LearnSpellSubType, argument: 6952, target: 1)),
            (34, Effect(2, 0, SpellEffectColumns.LearnSpellSubType, baseValue: 1, argument: 6953, target: 1)));

        Assert.Equal(6952u, spell.EffectLearnSpellId);
        Assert.Equal(6953u, Assert.Single(spell.ExtraEffects!).EffectLearnSpellId);
    }

    [Fact]
    public void AWeaponBlowKeepsTheStunInItsSecondSlot()
    {
        var spell = Classify(
            (33, Effect(1, 0, SpellEffectColumns.MainHandWeaponHitSubType, baseValue: 10, argument: 110)),
            (34, Effect(2, 1, SpellEffectColumns.StunSubType)));

        Assert.True(spell.EffectMeleeWeaponHit);
        Assert.False(spell.AuraStun);
        var stun = Assert.Single(spell.ExtraEffects!);
        Assert.True(stun.AuraStun);
        Assert.Equal(9000, stun.DurationMs);
        Assert.Equal(0, stun.CooldownMs);
        Assert.Null(stun.PowerCost);
    }

    [Fact]
    public void ABlowWithBothHandsKeepsTheOffHandBlowAsAFurtherEffect()
    {
        var spell = Classify(
            (33, Effect(1, 0, SpellEffectColumns.MainHandWeaponHitSubType, argument: 100, target: 7)),
            (37, Effect(2, 0, SpellEffectColumns.OffHandWeaponHitSubType, argument: 120, target: 7)));

        Assert.Equal(EquipmentSlots.MainHand, spell.EffectMeleeWeaponSlot);
        var offHand = Assert.Single(spell.ExtraEffects!);
        Assert.True(offHand.EffectMeleeWeaponHit);
        Assert.Equal(EquipmentSlots.OffHand, offHand.EffectMeleeWeaponSlot);
        Assert.Equal(120, offHand.EffectMeleeWeaponDamagePercent);
    }

    [Fact]
    public void AChillAndASlowBehindDamageJoinTheLeadAndAreNotAlsoFurtherEffects()
    {
        var spell = Classify(
            (33, Effect(1, 0, SpellEffectColumns.DirectDamageSubType, baseValue: 20, spread: 5)),
            (34, Effect(2, 1, SpellEffectColumns.SlowSubType, baseValue: 30)),
            (35, Effect(3, 1, SpellEffectColumns.ChillSubType)));

        Assert.Equal(20, spell.EffectDamage);
        Assert.True(spell.AuraChill);
        Assert.Equal(30, spell.AuraSlowPercent);
        Assert.Null(spell.ExtraEffects);
    }

    [Fact]
    public void ASlowBehindAWeaponBlowIsAFurtherEffect()
    {
        var spell = Classify(
            (33, Effect(1, 0, SpellEffectColumns.RangedWeaponHitSubType, argument: 100)),
            (34, Effect(2, 1, SpellEffectColumns.SlowSubType, baseValue: 30)));

        Assert.Null(spell.AuraSlowPercent);
        Assert.Equal(30, Assert.Single(spell.ExtraEffects!).AuraSlowPercent);
    }

    [Fact]
    public void AChillInTheLeadSlotIsImported()
    {
        var spell = Classify((33, Effect(1, 1, SpellEffectColumns.ChillSubType)));

        Assert.True(spell.AuraChill);
        Assert.Equal(SpellEffectTargets.SingleEnemy, spell.EffectTarget);
    }

    [Fact]
    public void AnEffectThatNeedsATargetOrCasterStateIsLeftOut()
    {
        var spell = Classify(
            (33, Effect(1, 0, SpellEffectColumns.RangedWeaponHitSubType, baseValue: 15, argument: 100)),
            (34, Effect(2, 0, SpellEffectColumns.InstantTriggerSpellSubType, baseValue: 100, argument: 900, casterCondition: 8)),
            (35, Effect(3, 0, SpellEffectColumns.DirectDamageSubType, baseValue: 4, targetCondition: 4)));

        Assert.True(spell.EffectRangedWeaponHit);
        Assert.Null(spell.EffectTriggerSpellId);
        Assert.Null(spell.ExtraEffects);
    }

    [Fact]
    public void AnUnconditionedInstantTriggerIsKept()
    {
        var spell = Classify(
            (33, Effect(1, 0, SpellEffectColumns.HealSubType, baseValue: 40, target: 6)),
            (34, Effect(2, 0, SpellEffectColumns.InstantTriggerSpellSubType, baseValue: 30, argument: 900, target: 1)));

        Assert.Equal(40, spell.EffectHeal);
        Assert.Equal(900u, spell.EffectTriggerSpellId);
        Assert.Equal(30, spell.EffectTriggerChancePercent);
        Assert.Null(spell.ExtraEffects);
    }

    [Fact]
    public void APowerRestoreIsImportedWithItsKindAndSpread()
    {
        var spell = Classify((33, Effect(1, 0, SpellEffectColumns.PowerRestoreSubType, baseValue: 280, spread: 80,
            argument: PowerKinds.Mana, target: SpellEffectTargets.Self)));

        Assert.Equal(280, spell.EffectPowerRestore);
        Assert.Equal(80, spell.EffectPowerRestoreRandom);
        Assert.Equal(PowerKinds.Mana, spell.EffectPowerKind);
        Assert.Equal(SpellEffectTargets.Self, spell.EffectTarget);
    }

    [Fact]
    public void AShieldThatMakesItsBearerTakeMoreDamageIsNotAShield()
    {
        var spell = Classify(
            (33, Effect(1, 1, SpellEffectColumns.AttributeModifierPercentSubType, baseValue: 10, argument: 70, target: 1)),
            (36, Effect(2, 1, SpellEffectColumns.AbsorbSubType, baseValue: 999_999_999, argument: unchecked((uint)-10), target: 1)));

        Assert.Null(spell.AuraAbsorbAmount);
        Assert.Null(spell.ExtraEffects);
    }

    [Fact]
    public void AKindThisServerHasNoFieldForAddsNothing()
    {
        var spell = Classify(
            (33, Effect(1, 0, SpellEffectColumns.DirectDamageSubType, baseValue: 20)),
            (34, Effect(2, 1, 12)),
            (35, Effect(3, 0, 20)));

        Assert.Equal(20, spell.EffectDamage);
        Assert.Null(spell.ExtraEffects);
    }
}
