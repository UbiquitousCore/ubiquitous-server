using System.Text.RegularExpressions;
using Ubiquitous.Data;

namespace Ubiquitous.Game;

// Spells only a GM may cast: this server's own operator spells, and the
// build's test and GM rows, which carry it in their names ("Test Stun",
// "GM:Offhand Expert", "God Attribute1", "teleprot test").
public static partial class GmOnlySpells
{
    public static bool Covers(uint spellId, SpellDefinition? spell) =>
        OperatorSpells.All.Contains(spellId) || (spell is not null && Named.IsMatch(spell.NameKey));

    // "God" alone also names real gear ("Robe of Healer of God"), so only the tool row's own name counts.
    [GeneratedRegex(@"\btest\b|\bsandbox\b|\bdebug\b|^\s*gm\s*:|^god attribute", RegexOptions.IgnoreCase)]
    private static partial Regex Named { get; }
}
