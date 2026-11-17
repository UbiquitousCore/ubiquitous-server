using Ubiquitous.Data;

namespace Ubiquitous.Game;

// The full spellbook a character may hold without having learned it in play: 1.1.4's level-60
// books as seen there, or for a later build every rank its own lessons teach up to the level.
public static class ClassSpellbook
{
    // Whether a character of this level starts each session knowing the whole book up to its level:
    // at 1.1.4's 60, at a later build's cap, and always on a development login of a later build.
    public static bool HoldsWholeBook(GameContent content, int level, bool developmentLogin) =>
        content.PlaysAsSeen
            ? level >= LevelingService.SeenMaxLevel
            : developmentLogin || level >= content.LevelCap;

    // The class's own ranks and the level each comes at. A 1.1.4 spell no lesson teaches comes with the class.
    public static IEnumerable<(uint Id, int Level)> ClassRanks(
        GameContent content, int playerClass, IReadOnlyDictionary<uint, int> lessonLevels) =>
        !content.PlaysAsSeen && content.Spellbook is { } book
            ? book.ClassRanks.TryGetValue(playerClass, out var ranks)
                ? ranks.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value))
                : []
            : PlaytestSpellbook.AtMaxLevel(playerClass).Select(id => (id, SpellLearning.LevelOf(lessonLevels, id, 1)));

    // A held talent ability's higher ranks. In a later build they are the ranks of its chain that a
    // lesson the class may use teaches; in 1.1.4 a rank none teaches counts as level 60.
    public static IEnumerable<(uint Id, int Level)> TalentAbilityRanks(
        GameContent content, uint talentSpellId, IReadOnlyDictionary<uint, int> lessonLevels)
    {
        if (content.PlaysAsSeen)
            return PlaytestSpellbook.TalentAbilityRanksAtMaxLevel(talentSpellId)
                .Select(id => (id, SpellLearning.LevelOf(lessonLevels, id, LevelingService.SeenMaxLevel)));
        if (!content.Spells.TryGetValue(talentSpellId, out var ability) || ability.Rank <= 0) return [];
        var chain = ability.ChainFirstSpellId ?? talentSpellId;
        return lessonLevels
            .Where(pair => pair.Key != talentSpellId && content.Spells.TryGetValue(pair.Key, out var spell)
                && (spell.ChainFirstSpellId ?? spell.Id) == chain && spell.Rank > ability.Rank)
            .OrderBy(pair => pair.Value).ThenBy(pair => pair.Key)
            .Select(pair => (pair.Key, pair.Value));
    }

    // What a session starts with besides its starting spells: 1.1.4's whole level-60 book, or a later
    // build's ranks up to the character's level.
    public static IReadOnlyList<uint> WholeBookAt(GameContent content, int playerClass, int level) =>
        content.PlaysAsSeen
            ? PlaytestSpellbook.AtMaxLevel(playerClass)
            : [.. ClassRanks(content, playerClass, new Dictionary<uint, int>())
                .Where(rank => rank.Level <= level).Select(rank => rank.Id)];

    public static IReadOnlyList<uint> TalentRanksAt(
        GameContent content, IEnumerable<uint> talentAbilities, int level, IReadOnlyDictionary<uint, int> lessonLevels) =>
        content.PlaysAsSeen
            ? [.. talentAbilities.SelectMany(PlaytestSpellbook.TalentAbilityRanksAtMaxLevel)]
            : [.. talentAbilities.SelectMany(id => TalentAbilityRanks(content, id, lessonLevels))
                .Where(rank => rank.Level <= level).Select(rank => rank.Id)];
}
