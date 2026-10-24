using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class SpellRanksTests
{
    private static readonly IReadOnlyDictionary<uint, SpellDefinition> Spells = new Dictionary<uint, SpellDefinition>
    {
        [5000] = new(5000, "Fireball", 2000, 0, 25, Rank: 1),
        [5006] = new(5006, "Fireball", 2000, 0, 25, Rank: 2),
        [5007] = new(5007, "Fireball", 2000, 0, 25, Rank: 3),
        [1] = new(1, "Fireball", 2000, 0, 25),
        [5233] = new(5233, "Swift Strike", 0, 0, 5, Rank: 1),
        [108] = new(108, "Auto Attack", 0, 0, 5),
    };

    [Fact]
    public void HighestKnown_PicksTheHighestRankOfTheSameSpellTheCharacterKnows()
    {
        Assert.Equal(5006u, SpellRanks.HighestKnown(5000, [5000, 5006, 1], Spells));
    }

    [Fact]
    public void HighestKnown_IgnoresRanksTheCharacterHasNotLearned()
    {
        Assert.Equal(5000u, SpellRanks.HighestKnown(5000, [5000], Spells));
    }

    [Fact]
    public void HighestKnown_LeavesASpellWithoutARankAlone()
    {
        Assert.Equal(108u, SpellRanks.HighestKnown(108, [108, 5007], Spells));
        Assert.Equal(1u, SpellRanks.HighestKnown(1, [1, 5007], Spells));
    }

    [Fact]
    public void HighestKnown_NeverCrossesToAnotherSpell()
    {
        Assert.Equal(5233u, SpellRanks.HighestKnown(5233, [5233, 5007], Spells));
    }

    [Fact]
    public void HighestKnownOfChain_FallsBackToTheHighestRankStillKnown()
    {
        Assert.Equal(5006u, SpellRanks.HighestKnownOfChain(5007, [5000, 5006, 5233], Spells));
        Assert.Equal(5000u, SpellRanks.HighestKnownOfChain(5007, [5000, 5233], Spells));
    }

    [Fact]
    public void HighestKnownOfChain_IsNothingWhenNoRankOfItIsKnown()
    {
        Assert.Null(SpellRanks.HighestKnownOfChain(5007, [5233, 108], Spells));
        Assert.Null(SpellRanks.HighestKnownOfChain(999_999, [5000], Spells));
    }
}
