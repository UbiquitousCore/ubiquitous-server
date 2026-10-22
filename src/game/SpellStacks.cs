using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed class SpellStacks(IReadOnlyDictionary<uint, int> limits)
{
    private readonly Dictionary<uint, (int Count, DateTimeOffset Until)> _held = [];

    public int Land(uint spellId, SpellDefinition effect, bool adds, DateTimeOffset now)
    {
        if (!limits.TryGetValue(spellId, out var limit) || limit <= 1) return 1;
        var held = _held.TryGetValue(spellId, out var stack) && now < stack.Until ? stack.Count : 0;
        if (!adds) return Math.Max(1, held);
        var lasts = effect.DurationMs > 0 ? TimeSpan.FromMilliseconds(effect.DurationMs) : AuraService.Duration;
        var count = Math.Min(limit, held + 1);
        _held[spellId] = (count, now + lasts);
        return count;
    }

    public static SpellDefinition Scaled(SpellDefinition effect, int stacks)
    {
        if (stacks <= 1) return effect;
        int? Times(int? value) => value * stacks;
        return effect with
        {
            AuraSlowPercent = effect.AuraSlowPercent is int slow ? Math.Min(100, slow * stacks) : null,
            AuraMeleeAttackSpeedPercent = Times(effect.AuraMeleeAttackSpeedPercent),
            AuraRangeAttackSpeedPercent = Times(effect.AuraRangeAttackSpeedPercent),
            AuraSpeedupPercent = Times(effect.AuraSpeedupPercent),
            AuraAttributeAmount = Times(effect.AuraAttributeAmount),
            AuraSpellModifierAmount = Times(effect.AuraSpellModifierAmount),
            AuraPeriodicDamage = Times(effect.AuraPeriodicDamage),
        };
    }
}
