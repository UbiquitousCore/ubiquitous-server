using System.Text.RegularExpressions;
using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

// Which creatures a quest names as the ones carrying an item it asks for, for a build whose tables
// carry no loot. Own reading of the user's files: no 4.2.2 table links a quest item to a creature,
// but the quest's own words name one. The tiers below, first hit wins, are this project's choice.
public static partial class QuestItemSources
{
    // Own choice: a named creature counts only within this many levels of the quest.
    public const int LevelSlack = 5;

    // Own choice: a word of a creature's name this long or longer can name it on its own.
    public const int ShortestNamingWord = 4;

    public static IReadOnlyList<QuestItemSourceDefinition> Find(
        IReadOnlyList<QuestDefinition> quests,
        IReadOnlyDictionary<uint, ItemDefinition> items,
        IReadOnlyList<CreatureDefinition> creatures,
        IReadOnlyList<CreatureSpawnDefinition> spawns,
        IReadOnlyDictionary<uint, string> questStrings,
        IReadOnlyList<StaticObjectItemBindingDefinition> staticObjectItems)
    {
        var mapsOf = spawns.GroupBy(spawn => spawn.CreatureEntryId)
            .ToDictionary(group => group.Key, group => group.Select(spawn => spawn.MapId).ToHashSet());
        // A quest item lies in the world when a static object holds it for that quest.
        var lying = staticObjectItems.Select(binding => (binding.QuestId, binding.ItemEntry)).ToHashSet();
        var carriers = creatures
            .Where(creature => mapsOf.ContainsKey(creature.EntryId) && CanCarry(creature))
            .ToList();
        var names = new CreatureNames(carriers);
        var sources = new List<QuestItemSourceDefinition>();

        foreach (var quest in quests)
        {
            var kills = quest.Objectives.Where(objective => objective.Kind == QuestObjectiveKind.Killed)
                .Select(objective => objective.ObjectiveCreature).Where(entry => entry != 0).Distinct().ToList();
            var asked = Text(questStrings, quest.Objectives.Select(objective => objective.ObjectiveStringId));
            var told = Text(questStrings, [quest.TitleStringId, quest.DetailsStringId]);
            var giverMaps = quest.GiverEntryId is uint giver && mapsOf.TryGetValue(giver, out var maps) ? maps : [];
            bool Near(CreatureDefinition creature) => quest.Level <= 0 || Math.Abs(creature.Level - quest.Level) <= LevelSlack;

            foreach (var objective in quest.Objectives.Where(objective => objective.Kind == QuestObjectiveKind.Held))
            {
                if (lying.Contains((quest.Id, objective.ObjectiveItem))
                    || !items.TryGetValue(objective.ObjectiveItem, out var item)) continue;
                IReadOnlyList<uint> found = kills.Count > 0
                    ? kills
                    : FirstNamed(
                        () => names.Named(item.NameKey, Near),
                        () => names.Named(asked, Near),
                        () => names.Named(told, Near),
                        () => names.NamedByWord(item.NameKey,
                            creature => Near(creature) && (giverMaps.Count == 0 || mapsOf[creature.EntryId].Overlaps(giverMaps))));
                if (found.Count > 0)
                    sources.Add(new QuestItemSourceDefinition(quest.Id, objective.ObjectiveItem, found));
            }
        }

        return sources;
    }

    // A townsfolk creature is not fought, so it carries nothing to loot.
    private static bool CanCarry(CreatureDefinition creature) =>
        creature.FactionType != UnitFactionTypes.Townsfolk && !creature.NameKey.StartsWith("creature-", StringComparison.Ordinal);

    private static IReadOnlyList<uint> FirstNamed(params Func<IReadOnlyList<uint>>[] tiers)
    {
        foreach (var tier in tiers)
        {
            var found = tier();
            if (found.Count > 0) return found;
        }

        return [];
    }

    private static string Text(IReadOnlyDictionary<uint, string> strings, IEnumerable<uint> ids) =>
        string.Join(" ", ids.Where(id => id != 0).Distinct()
            .Select(id => strings.TryGetValue(id, out var text) ? Markup().Replace(text, " ") : ""));

    // The quest strings open a tag with byte 0x12 and close it with 0x13.
    [GeneratedRegex("\u0012[^\u0013]*\u0013")]
    private static partial Regex Markup();

    [GeneratedRegex("[A-Za-z']+")]
    private static partial Regex Words();

    private sealed class CreatureNames
    {
        private readonly IReadOnlyList<(string Name, Regex Pattern, IReadOnlyList<CreatureDefinition> Creatures)> _byName;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<CreatureDefinition>> _byWord;

        public CreatureNames(IReadOnlyList<CreatureDefinition> creatures)
        {
            // Longest first, so "Goblin Slasher" claims its words before "Goblin" can.
            _byName = [.. creatures.GroupBy(creature => creature.NameKey, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Key.Length).ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => (group.Key,
                    new Regex(@"(?<![\w'])" + Plural(group.Key) + @"(?![\w])",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                    (IReadOnlyList<CreatureDefinition>)[.. group]))];
            _byWord = creatures
                .SelectMany(creature => Words().Matches(creature.NameKey).Select(word => (Word: Stem(word.Value), creature)))
                .Where(pair => pair.Word.Length >= ShortestNamingWord)
                .GroupBy(pair => pair.Word, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<CreatureDefinition>)[.. group.Select(pair => pair.creature).Distinct()],
                    StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<uint> Named(string text, Func<CreatureDefinition, bool> fits)
        {
            if (text.Length == 0) return [];
            var claimed = new List<(int Start, int End)>();
            var found = new List<uint>();
            foreach (var (_, pattern, creatures) in _byName)
            {
                foreach (Match match in pattern.Matches(text))
                {
                    if (claimed.Any(span => span.Start <= match.Index && match.Index + match.Length <= span.End)) continue;
                    claimed.Add((match.Index, match.Index + match.Length));
                    found.AddRange(creatures.Where(fits).Select(creature => creature.EntryId));
                }
            }

            return [.. found.Distinct().Order()];
        }

        public IReadOnlyList<uint> NamedByWord(string text, Func<CreatureDefinition, bool> fits) =>
            [.. Words().Matches(text).Select(word => Stem(word.Value))
                .Where(word => word.Length >= ShortestNamingWord)
                .SelectMany(word => _byWord.TryGetValue(word, out var creatures) ? creatures : [])
                .Where(fits).Select(creature => creature.EntryId).Distinct().Order()];

        // A name as one creature or as many: "Gray Wolf" or "Gray Wolves", "Harpy" or "Harpies".
        private static string Plural(string name)
        {
            if (name.EndsWith("fe", StringComparison.OrdinalIgnoreCase))
                return Regex.Escape(name[..^2]) + "(?:fes?|ves)";
            if (name.EndsWith('f') || name.EndsWith('F')) return Regex.Escape(name[..^1]) + "(?:fs?|ves)";
            if (name.EndsWith('y') || name.EndsWith('Y')) return Regex.Escape(name[..^1]) + "(?:ys?|ies)";
            return Regex.Escape(name) + "(?:e?s)?";
        }

        // "Krun's" names the Krun; a closing "s" or "'s" is let go.
        private static string Stem(string word)
        {
            var bare = word.EndsWith("'s", StringComparison.OrdinalIgnoreCase) ? word[..^2] : word.TrimEnd('\'');
            return bare.Length > ShortestNamingWord && bare.EndsWith('s') ? bare[..^1] : bare;
        }
    }
}
