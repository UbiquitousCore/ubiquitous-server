namespace Ubiquitous.AssetExtractor;

public static class SpellEffectColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/spell_effect.tbl",
        "uuuuufuuiiiuuuuuuuuuusss",
        [
            LayoutColumn.Must(nameof(SpellEffectTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(SpellEffectTableColumns.Target), 4, "iu"),
            LayoutColumn.Must(nameof(SpellEffectTableColumns.Type), 6, "iu"),
            LayoutColumn.Must(nameof(SpellEffectTableColumns.SubType), 7, "iu"),
            LayoutColumn.Must(nameof(SpellEffectTableColumns.School), 8, "iu"),
            LayoutColumn.Must(nameof(SpellEffectTableColumns.BaseValue), 9, "iu"),
            LayoutColumn.Must(nameof(SpellEffectTableColumns.Spread), 10, "iu"),
            LayoutColumn.Must(nameof(SpellEffectTableColumns.EffectArgument), 11, "u"),
            LayoutColumn.Must(nameof(SpellEffectTableColumns.TargetCondition), 16, "u"),
            LayoutColumn.Must(nameof(SpellEffectTableColumns.CasterCondition), 17, "u"),
        ],
        // 4.2.21
        new LayoutRevision("uuuuufuuiiiuuuuuuuuuusssuuuu", new LayoutEdit.Adds(24, 4)));

    public static SpellEffectTableColumns Reference => Layout.Reference.As<SpellEffectTableColumns>();

    public const int PowerRestoreSubType = 6;

    public const int DirectDamageType = 0;
    public const int DirectDamageSubType = 1;

    public const int HealType = 0;
    public const int HealSubType = 3;

    public const int AuraType = 1;

    public const int AbsorbSubType = 3;

    public const int PeriodicDamageSubType = 1;
    public const int PeriodicHealSubType = 2;

    public const int StunSubType = 10;
    public const int SleepSubType = 13;

    public const int SilenceSubType = 31;

    public const int MeleeAttackSpeedModifierSubType = 26;

    public const int RangeAttackSpeedModifierSubType = 27;

    public const int PeriodicTriggerSpellSubType = 37;

    public const int EatSubType = 5;
    public const int DrinkSubType = 6;

    public const int AttributeModifierFlatSubType = 4;
    public const int AttributeModifierPercentSubType = 14;

    public const int SlowSubType = 7;

    public const int ChillSubType = 11;

    public const int TriggerSpellSubType = 15;

    public const int ModifierSpellFlatSubType = 8;

    public const int ModifierSpellPercentSubType = 9;

    public const int SpeedupSubType = 16;

    public const int RangedWeaponHitSubType = 28;

    public const int MainHandWeaponHitSubType = 23;
    public const int OffHandWeaponHitSubType = 27;

    public const int StealthSubType = 23;

    public const int InstantTriggerSpellSubType = 9;

    public const int LeapToTargetSubType = 14;

    public const int LearnSpellSubType = 10;

    // Names the creature a pet spell calls to its owner's side.
    public const int SummonSubType = 8;

    // The aura that turns its wearer into another creature's display (a boss shape, a cookie, a spirit).
    public const int AvatarSubType = 17;

    // The effect applies only while the caster is in sneak mode.
    public static bool NeedsCasterHidden(uint casterCondition) => casterCondition is 8 or 9;

    public const int MountSubType = 50;
    public const int MountSpeedupSubType = 52;
}
