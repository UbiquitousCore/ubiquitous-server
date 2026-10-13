using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public static class SpellEffectClassifier
{
    public static SpellDefinition Apply(SpellDefinition spell, SpellEffectDefinition effect)
    {
        int sub = effect.SubType;
        int? spread = effect.Spread > 0 ? effect.Spread : null;
        if (effect.Type == SpellEffectColumns.DirectDamageType)
        {
            return sub switch
            {
                SpellEffectColumns.DirectDamageSubType =>
                    spell with { EffectDamage = effect.BaseValue, EffectDamageRandom = spread },
                SpellEffectColumns.HealSubType =>
                    spell with { EffectHeal = effect.BaseValue, EffectHealRandom = spread },
                SpellEffectColumns.RangedWeaponHitSubType => spell with
                {
                    EffectRangedWeaponHit = true,
                    EffectRangedWeaponDamageBonus = effect.BaseValue,
                    EffectRangedWeaponDamagePercent = checked((int)effect.EffectArgument),
                },
                SpellEffectColumns.MainHandWeaponHitSubType or SpellEffectColumns.OffHandWeaponHitSubType => spell with
                {
                    EffectMeleeWeaponHit = true,
                    EffectMeleeWeaponSlot = sub == SpellEffectColumns.OffHandWeaponHitSubType
                        ? EquipmentSlots.OffHand : EquipmentSlots.MainHand,
                    EffectMeleeWeaponDamageBonus = effect.BaseValue,
                    EffectMeleeWeaponDamagePercent = checked((int)effect.EffectArgument),
                },
                SpellEffectColumns.InstantTriggerSpellSubType when effect.EffectArgument != 0 => spell with
                {
                    EffectTriggerSpellId = effect.EffectArgument,
                    EffectTriggerChancePercent = effect.BaseValue,
                },
                SpellEffectColumns.LeapToTargetSubType => spell with { EffectLeapToTarget = true },
                SpellEffectColumns.LearnSpellSubType when effect.EffectArgument != 0 =>
                    spell with { EffectLearnSpellId = effect.EffectArgument },
                SpellEffectColumns.PowerRestoreSubType => spell with
                {
                    EffectPowerRestore = effect.BaseValue,
                    EffectPowerRestoreRandom = spread,
                    EffectPowerKind = checked((int)effect.EffectArgument),
                },
                _ => spell,
            };
        }

        if (effect.Type != SpellEffectColumns.AuraType) return spell;
        return sub switch
        {
            SpellEffectColumns.PeriodicDamageSubType => spell with { AuraPeriodicDamage = effect.BaseValue },
            SpellEffectColumns.PeriodicHealSubType => spell with { AuraPeriodicHeal = effect.BaseValue },
            SpellEffectColumns.StunSubType => spell with { AuraStun = true },
            SpellEffectColumns.SleepSubType => spell with { AuraSleep = true },
            SpellEffectColumns.SilenceSubType => spell with { AuraSilence = true },
            SpellEffectColumns.MeleeAttackSpeedModifierSubType => spell with { AuraMeleeAttackSpeedPercent = effect.BaseValue },
            SpellEffectColumns.RangeAttackSpeedModifierSubType => spell with { AuraRangeAttackSpeedPercent = effect.BaseValue },
            SpellEffectColumns.PeriodicTriggerSpellSubType => spell with
            {
                AuraPeriodicTriggerSpellId = effect.EffectArgument,
                AuraPeriodicTriggerChancePercent = effect.BaseValue,
            },
            SpellEffectColumns.AttributeModifierFlatSubType or SpellEffectColumns.AttributeModifierPercentSubType => spell with
            {
                AuraAttributeId = checked((int)effect.EffectArgument),
                AuraAttributeAmount = effect.BaseValue,
                AuraAttributeIsPercent = sub == SpellEffectColumns.AttributeModifierPercentSubType,
            },
            SpellEffectColumns.SlowSubType => spell with { AuraSlowPercent = effect.BaseValue },
            SpellEffectColumns.SpeedupSubType => spell with { AuraSpeedupPercent = effect.BaseValue },
            SpellEffectColumns.ModifierSpellFlatSubType or SpellEffectColumns.ModifierSpellPercentSubType => spell with
            {
                AuraSpellModifierType = checked((int)effect.EffectArgument),
                AuraSpellModifierIsPercent = sub == SpellEffectColumns.ModifierSpellPercentSubType,
                AuraSpellModifierAmount = effect.BaseValue,
            },
            SpellEffectColumns.ChillSubType when effect.EffectArgument == 0 => spell with { AuraChill = true },
            SpellEffectColumns.TriggerSpellSubType when effect.EffectArgument != 0 => spell with
            {
                AuraTriggerSpellId = effect.EffectArgument,
                AuraTriggerChancePercent = effect.BaseValue,
            },
            SpellEffectColumns.AbsorbSubType when unchecked((int)effect.EffectArgument) >= 0 => spell with
            {
                AuraAbsorbAmount = effect.BaseValue,
                AuraAbsorbPercent = checked((int)effect.EffectArgument),
            },
            SpellEffectColumns.StealthSubType => spell with { AuraStealth = true },
            SpellEffectColumns.MountSubType when effect.EffectArgument != 0 => spell with
            {
                AuraMountId = effect.EffectArgument,
                AuraMountKeepSeatPercent = effect.BaseValue,
            },
            SpellEffectColumns.MountSpeedupSubType => spell with { AuraMountSpeedupPercent = effect.BaseValue },
            _ => spell,
        };
    }

    public static SpellDefinition ApplySlots(SpellDefinition spell, IReadOnlyList<(int Column, SpellEffectDefinition? Effect)> slots)
    {
        SpellEffectDefinition? At(int column) => slots.FirstOrDefault(slot => slot.Column == column).Effect;
        static bool Unconditioned(SpellEffectDefinition effect) => effect.TargetCondition == 0 && effect.CasterCondition == 0;

        var used = new HashSet<int> { 33 };
        if (At(33) is { } lead) spell = Apply(spell with { EffectTarget = lead.Target }, lead);

        if (At(35) is { } second && Unconditioned(second))
        {
            var withSecond = Apply(Blank(spell, second), second);
            if (spell.AuraTriggerSpellId is null && withSecond.AuraTriggerSpellId is not null)
            {
                spell = spell with { AuraTriggerSpellId = withSecond.AuraTriggerSpellId, AuraTriggerChancePercent = withSecond.AuraTriggerChancePercent };
                used.Add(35);
            }
            else if (!spell.AuraChill && withSecond.AuraChill)
            {
                spell = spell with { AuraChill = true };
                used.Add(35);
            }
        }

        if (spell.EffectDamage is not null && spell.AuraSlowPercent is null)
        {
            foreach (var column in new[] { 34, 35 })
            {
                if (used.Contains(column) || At(column) is not { } candidate || !Unconditioned(candidate)) continue;
                var withSlow = Apply(Blank(spell, candidate), candidate);
                if (withSlow.AuraSlowPercent is null) continue;
                spell = spell with { AuraSlowPercent = withSlow.AuraSlowPercent };
                used.Add(column);
                break;
            }
        }

        if (spell.AuraMountId is not null)
        {
            foreach (var column in new[] { 34, 35 })
            {
                if (used.Contains(column) || At(column) is not { } candidate || !Unconditioned(candidate)) continue;
                var withSpeed = Apply(Blank(spell, candidate), candidate);
                if (withSpeed.AuraMountSpeedupPercent is null) continue;
                spell = spell with { AuraMountSpeedupPercent = withSpeed.AuraMountSpeedupPercent };
                used.Add(column);
                break;
            }
        }

        foreach (var column in new[] { 34, 35 })
        {
            if (spell.EffectTriggerSpellId is not null) break;
            if (used.Contains(column)) continue;
            if (At(column) is not { } candidate || !Unconditioned(candidate)) continue;
            var withTrigger = Apply(Blank(spell, candidate), candidate);
            if (withTrigger.EffectTriggerSpellId is null) continue;
            spell = spell with { EffectTriggerSpellId = withTrigger.EffectTriggerSpellId, EffectTriggerChancePercent = withTrigger.EffectTriggerChancePercent };
            used.Add(column);
        }

        var extras = new List<SpellDefinition>();
        foreach (var (column, effect) in slots)
        {
            if (effect is null || used.Contains(column) || !Unconditioned(effect)) continue;
            var blank = Blank(spell, effect);
            var extra = Apply(blank, effect);
            if (extra != blank) extras.Add(extra);
        }

        return extras.Count > 0 ? spell with { ExtraEffects = extras } : spell;
    }

    private static SpellDefinition Blank(SpellDefinition spell, SpellEffectDefinition effect) =>
        new(spell.Id, spell.NameKey, 0, 0, spell.MaxRange, DurationMs: spell.DurationMs, EffectTarget: effect.Target);
}
