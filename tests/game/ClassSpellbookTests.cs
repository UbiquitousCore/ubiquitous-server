using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class ClassSpellbookTests
{
    private static GameContent LaterBuild()
    {
        var book = StartingSpellbook.Read(
            StartingSpellbookTests.Spells, StartingSpellbookTests.Items, StartingSpellbookTests.Types, StartingSpellbookTests.Levels);
        return GameContent.Development with
        {
            Spells = StartingSpellbookTests.Spells,
            Items = StartingSpellbookTests.Items,
            Loadouts = StartingLoadouts.FromSpellbook(book, StartingSpellbookTests.Items),
            Spellbook = book,
            LevelCap = 75,
        };
    }

    [Fact]
    public void ALaterBuildsWholeBook_IsHeldAtItsOwnCapOrOnADevelopmentLogin()
    {
        var content = LaterBuild();

        Assert.False(ClassSpellbook.HoldsWholeBook(content, 74, developmentLogin: false));
        Assert.True(ClassSpellbook.HoldsWholeBook(content, 75, developmentLogin: false));
        Assert.True(ClassSpellbook.HoldsWholeBook(content, 5, developmentLogin: true));
    }

    [Fact]
    public void The114Book_IsHeldFromLevel60AndIsTheOneSeenThere()
    {
        var content = GameContent.Development with { Loadouts = StartingLoadouts.All };

        Assert.False(ClassSpellbook.HoldsWholeBook(content, 59, developmentLogin: true));
        Assert.True(ClassSpellbook.HoldsWholeBook(content, 60, developmentLogin: false));
        Assert.Equal(PlaytestSpellbook.AtMaxLevel(PlayerClassIds.Mage),
            ClassSpellbook.WholeBookAt(content, PlayerClassIds.Mage, 60));
    }

    [Fact]
    public void ALaterBuildsWholeBook_StopsAtTheCharactersLevel()
    {
        var content = LaterBuild();

        Assert.Equal([100u, 110u, 101u, 111u], ClassSpellbook.WholeBookAt(content, PlayerClassIds.Mage, 5));
        Assert.Equal([100u, 110u, 101u, 111u, 120u, 121u], ClassSpellbook.WholeBookAt(content, PlayerClassIds.Mage, 75));
    }

    [Fact]
    public void ALaterBuildsTalentAbility_RisesThroughTheRanksItsChainsLessonsTeach()
    {
        var content = LaterBuild();
        var levels = SpellLearning.LevelsFor(content.Items.Values, content.Spells, PlayerClassIds.Mage);

        Assert.Equal([(101u, 2)], ClassSpellbook.TalentAbilityRanks(content, 100, levels));
        Assert.Empty(ClassSpellbook.TalentAbilityRanks(content, 101, levels));
        Assert.Equal([121u], ClassSpellbook.TalentRanksAt(content, [100, 120], 9, levels).Where(id => id != 101));
        Assert.Equal([101u], ClassSpellbook.TalentRanksAt(content, [100, 120], 8, levels));
    }
}
