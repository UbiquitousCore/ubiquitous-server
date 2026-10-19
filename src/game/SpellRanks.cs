using Ubiquitous.Data;

namespace Ubiquitous.Game;

public static class SpellRanks
{
    // A rank chain is every spell sharing a name and carrying a rank; a spell
    // with rank 0 stands alone.
    public static uint HighestKnown(uint spellId, IEnumerable<uint> known, IReadOnlyDictionary<uint, SpellDefinition> spells)
    {
        if (!spells.TryGetValue(spellId, out var spell) || spell.Rank <= 0) return spellId;
        var best = spellId;
        var bestRank = spell.Rank;
        foreach (var id in known)
        {
            if (!spells.TryGetValue(id, out var other) || other.Rank <= bestRank) continue;
            if (!string.Equals(other.NameKey, spell.NameKey, StringComparison.Ordinal)) continue;
            best = id;
            bestRank = other.Rank;
        }

        return best;
    }
}
