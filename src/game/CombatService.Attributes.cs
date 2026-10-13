using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed partial class CombatService
{
    public static int ApplyArmorMitigation(int rawDamage, int armor)
    {
        if (rawDamage <= 0) return rawDamage;
        double reduction = ArmorMitigation(armor);
        int mitigated = (int)Math.Round(rawDamage * (1.0 - reduction), MidpointRounding.AwayFromZero);
        return Math.Max(1, mitigated);
    }

    public static double ArmorMitigation(int armor) =>
        Math.Min(MaxArmorMitigation, armor * ArmorMitigationPerPoint);

    public static int ApplyStrengthBonus(int baseDamage, int strength) =>
        Math.Max(1, baseDamage + (int)Math.Round(strength * MeleeDamagePerStrength, MidpointRounding.AwayFromZero));

    public static int ApplyAttackPowerBonus(int damage, int attackPower) =>
        damage + (int)Math.Round(attackPower * MeleeDamagePerAttackPower, MidpointRounding.AwayFromZero);

    public static int ApplyHealingPowerBonus(int healAmount, int healingPower) =>
        Math.Max(0, healAmount + (int)Math.Round(healingPower * HealingPerHealingPower, MidpointRounding.AwayFromZero));

    public static int ApplySpellPowerBonus(int damageAmount, int spellPower) =>
        Math.Max(1, damageAmount + (int)Math.Round(spellPower * SpellDamagePerSpellPower, MidpointRounding.AwayFromZero));

    public StartingAttributes OwnAttributes =>
        PlayerStatCurves.AttributesAt(_playerClassId, _playerLevel, _levelOneAttributes);

    public int OwnAttackPower(int strengthBonus, int agilityBonus)
    {
        var own = OwnAttributes;
        return PlayerStatCurves.AttackPower(
            _playerClassId, _playerLevel, own.Strength + strengthBonus, own.Agility + agilityBonus);
    }

    public int EffectiveMaxHealth(int staminaBonus) =>
        Math.Max(1, PlayerStatCurves.BaseMaxHealth(_playerClassId, _playerLevel, PlayerMaxHealth)
            + (OwnAttributes.Stamina + staminaBonus) * HealthPerStamina);

    private static double LinearChance(int rating, double perPoint, double maxChance) =>
        Math.Clamp(Math.Max(0, rating) * perPoint, 0, maxChance);

    public static double DodgeChance(int agility, int dodgeRating = 0) =>
        Math.Clamp(
            Math.Max(0, agility) * DodgeChancePerAgility + Math.Max(0, dodgeRating) * DodgeChancePerDodgeRating,
            0, MaxDodgeChance);

    public static double CritChance(int critRating) =>
        LinearChance(critRating, CritChancePerRating, MaxCritChance);

    public static double ParryChance(int parryRating) =>
        LinearChance(parryRating, ParryChancePerRating, MaxParryChance);

    public static double BlockChance(int blockRating) =>
        LinearChance(blockRating, BlockChancePerRating, MaxBlockChance);

    public static double HitChance(int hitRating) => 1.0 - MissChance(hitRating);

    public static double MissChance(int hitRating) =>
        Math.Clamp(MeleeMissChance - Math.Max(0, hitRating) * MissChancePerHitRating, 0, MeleeMissChance);
}
