using Ubiquitous.Data;

namespace Ubiquitous.Game;

public static class SpellLearning
{
    // A spell is learned from a lesson item, and the item's required level is the level it can be learned at:
    // the lowest such level among the items a class may use. A spell no item teaches comes with the class.
    public static IReadOnlyDictionary<uint, int> LevelsFor(
        IEnumerable<ItemDefinition> items, IReadOnlyDictionary<uint, SpellDefinition> spells, int playerClass)
    {
        var levels = new Dictionary<uint, int>();
        foreach (var item in items)
        {
            if (item.OnUseSpellId is not uint lessonId || !spells.TryGetValue(lessonId, out var lesson)) continue;
            if (!ItemClassMask.Allows(item.AllowableClassMask, playerClass)) continue;
            foreach (var taught in SpellLessons.Taught(lesson))
            {
                var level = checked((int)item.RequiredLevel);
                levels[taught] = levels.TryGetValue(taught, out var known) ? Math.Min(known, level) : level;
            }
        }

        return levels;
    }

    public static int LevelOf(IReadOnlyDictionary<uint, int> levels, uint spellId, int whenNoItemTeachesIt) =>
        levels.TryGetValue(spellId, out var level) ? level : whenNoItemTeachesIt;
}
