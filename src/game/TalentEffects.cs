using Ubiquitous.Data;

namespace Ubiquitous.Game;

public static class TalentEffects
{
    public static bool IsCastable(SpellDefinition spell) =>
        spell.PowerCost is > 0 || spell.CooldownMs > 0 || spell.CastTimeMs > 0;

    public static IReadOnlyList<uint> Castable(IEnumerable<uint> spellIds, IReadOnlyDictionary<uint, SpellDefinition> spells) =>
        [.. spellIds.Where(id => spells.TryGetValue(id, out var spell) && IsCastable(spell))];

    public static IReadOnlyDictionary<int, int> PassiveAttributes(
        IEnumerable<uint> spellIds, IReadOnlyDictionary<uint, SpellDefinition> spells)
    {
        var totals = new Dictionary<int, int>();
        foreach (var id in spellIds)
        {
            if (!spells.TryGetValue(id, out var spell) || IsCastable(spell)) continue;
            foreach (var effect in (IEnumerable<SpellDefinition>)[spell, .. spell.ExtraEffects ?? []])
            {
                if (effect.AuraAttributeId is not int attribute || effect.AuraAttributeAmount is not int amount) continue;
                if (effect.AuraAttributeIsPercent) continue;
                totals[attribute] = totals.GetValueOrDefault(attribute) + amount;
            }
        }

        return totals;
    }
}
