using Ubiquitous.Data;

namespace Ubiquitous.Game;

public static class SpellLessons
{
    public static IReadOnlyList<uint> Taught(SpellDefinition spell) =>
        [.. new[] { spell }.Concat(spell.ExtraEffects ?? [])
            .Select(effect => effect.EffectLearnSpellId)
            .OfType<uint>()
            .Distinct()];

    // No vendor in 1.1.4 sells the riding skills a Bond asks for, so a skill
    // counts as known by whoever could use the item that teaches it.
    public static bool KnowsRequiredSpell(
        ItemDefinition item, IReadOnlySet<uint> known,
        IEnumerable<ItemDefinition> items, IReadOnlyDictionary<uint, SpellDefinition> spells,
        Func<ItemDefinition, bool> meetsItemRequirements)
    {
        if (item.RequiredSpellId is not uint required || known.Contains(required)) return true;
        return items.Any(teacher => teacher.Class == ItemClasses.Riding
            && teacher.OnUseSpellId is uint lesson
            && spells.TryGetValue(lesson, out var spell)
            && Taught(spell).Contains(required)
            && teacher.RequiredSpellId is uint permit && known.Contains(permit)
            && meetsItemRequirements(teacher));
    }
}
