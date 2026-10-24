namespace Ubiquitous.AssetExtractor;

public static class SpellEffectColumns
{
    public static readonly SpellEffectTableColumns Build114 = new(
        Id: 0,
        Type: 6,
        SubType: 7,
        School: 8,
        BaseValue: 9,
        Spread: 10,
        EffectArgument: 11,
        Target: 4,
        TargetCondition: 16,
        CasterCondition: 17);

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

    // The aura that turns its wearer into another creature's display (a boss shape, a cookie, a spirit).
    public const int AvatarSubType = 17;

    public const int MountSubType = 50;
    public const int MountSpeedupSubType = 52;
}
