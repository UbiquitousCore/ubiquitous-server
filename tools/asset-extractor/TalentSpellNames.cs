namespace Ubiquitous.AssetExtractor;

public sealed record TalentNaming(uint TalentId, uint NameTextId, IReadOnlyList<uint> RankSpellIds);

// A talent's rank spells often carry no name of their own in `String_Spell`, or one
// string several talents share (4.2.2 points eleven mage talents at one sentence of a
// description). Those take the talent's own name; a rank spell with a name of its own keeps it.
public static class TalentSpellNames
{
    public static IReadOnlyDictionary<uint, string> Renamed(
        IReadOnlyDictionary<uint, uint> spellNameTextIds,
        IReadOnlyDictionary<uint, string> spellStrings,
        IEnumerable<TalentNaming> talents,
        IReadOnlyDictionary<uint, string> talentStrings)
    {
        var ranks = talents
            .SelectMany(talent => talent.RankSpellIds.Select(spell => (Spell: spell, Talent: talent)))
            .Where(rank => spellNameTextIds.ContainsKey(rank.Spell))
            .ToList();
        var talentsPerName = ranks
            .GroupBy(rank => spellNameTextIds[rank.Spell])
            .ToDictionary(group => group.Key, group => group.Select(rank => rank.Talent.TalentId).Distinct().Count());

        var renamed = new Dictionary<uint, string>();
        foreach (var (spell, talent) in ranks)
        {
            if (!talentStrings.TryGetValue(talent.NameTextId, out var talentName) || string.IsNullOrWhiteSpace(talentName))
                continue;
            var nameId = spellNameTextIds[spell];
            var ownName = spellStrings.TryGetValue(nameId, out var text) ? text : null;
            if (!string.IsNullOrWhiteSpace(ownName) && talentsPerName[nameId] == 1) continue;
            renamed.TryAdd(spell, talentName.Trim());
        }

        return renamed;
    }
}
