using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class TalentBookTests
{
    private const int Warrior = PlayerClassIds.Warrior;

    private static readonly IReadOnlyList<TalentTreeDefinition> Trees =
    [
        new(1, Warrior,
        [
            new(10, 1, 1, 0, [], [100, 101, 102, 103, 104]),
            new(11, 1, 2, 0, [], [110]),
            new(12, 2, 1, 5, [10], [120, 121]),
        ]),
        new(2, PlayerClassIds.Mage, [new(20, 1, 1, 0, [], [200])]),
    ];

    [Theory]
    [InlineData(1, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(LevelingService.MaxLevel, 51)]
    [InlineData(LevelingService.MaxLevel + 5, 51)]
    public void PointsForLevel_EarnsOneALevelFromTen(int level, int expected)
    {
        Assert.Equal(expected, TalentBook.PointsForLevel(level));
    }

    [Fact]
    public void Learn_BuysOneRankAndSpendsOnePoint()
    {
        var book = new TalentBook(Trees);

        Assert.Null(book.Learn(10, Warrior, level: 12));

        Assert.Equal(1, book.Ranks[10]);
        Assert.Equal(2, book.Unspent(12));
        Assert.Equal([100u], book.CurrentSpells);
    }

    [Fact]
    public void Learn_AHigherRankReplacesTheLowerRanksSpell()
    {
        var book = new TalentBook(Trees);
        book.Learn(10, Warrior, 20);
        book.Learn(10, Warrior, 20);

        Assert.Equal([101u], book.CurrentSpells);
    }

    [Fact]
    public void Learn_RefusesWithNoPointsLeft()
    {
        var book = new TalentBook(Trees);

        Assert.Equal(TalentRefusal.NoPoints, book.Learn(10, Warrior, level: 9));
        Assert.Null(book.Learn(10, Warrior, level: 10));
        Assert.Equal(TalentRefusal.NoPoints, book.Learn(11, Warrior, level: 10));
    }

    [Fact]
    public void Learn_RefusesPastTheLastRank()
    {
        var book = new TalentBook(Trees);
        Assert.Null(book.Learn(11, Warrior, 30));

        Assert.Equal(TalentRefusal.MaxRank, book.Learn(11, Warrior, 30));
    }

    [Fact]
    public void Learn_RefusesAnotherClassesTreeAndAnUnknownTalent()
    {
        var book = new TalentBook(Trees);

        Assert.Equal(TalentRefusal.WrongClass, book.Learn(20, Warrior, 30));
        Assert.Equal(TalentRefusal.UnknownTalent, book.Learn(999, Warrior, 30));
    }

    [Fact]
    public void Learn_ATierNeedsEnoughPointsInItsOwnTree()
    {
        var book = new TalentBook(Trees);
        for (var i = 0; i < 4; i++) book.Learn(10, Warrior, 30);
        book.Learn(11, Warrior, 30);

        Assert.Equal(TalentRefusal.Prerequisite, book.Learn(12, Warrior, 30));
    }

    [Fact]
    public void Learn_ATierIsLockedUntilItsPointsAreSpent()
    {
        var trees = new List<TalentTreeDefinition>
        {
            new(1, Warrior, [new(10, 1, 1, 0, [], [100, 101]), new(12, 2, 1, 5, [], [120])]),
        };
        var book = new TalentBook(trees);
        book.Learn(10, Warrior, 30);
        book.Learn(10, Warrior, 30);

        Assert.Equal(TalentRefusal.TierLocked, book.Learn(12, Warrior, 30));
    }

    [Fact]
    public void Learn_OpensTheNextTierWithThePrerequisiteAtFullRank()
    {
        var book = new TalentBook(Trees);
        for (var i = 0; i < 5; i++) Assert.Null(book.Learn(10, Warrior, 30));

        Assert.Null(book.Learn(12, Warrior, 30));
    }

    [Fact]
    public void Reset_GivesEveryPointBack()
    {
        var book = new TalentBook(Trees);
        book.Learn(10, Warrior, 12);
        book.Learn(11, Warrior, 12);

        book.Reset();

        Assert.Empty(book.Ranks);
        Assert.Equal(3, book.Unspent(12));
        Assert.Empty(book.CurrentSpells);
    }

    [Fact]
    public void Restore_BuysAPrerequisiteThatSitsLaterInTheSameTierFirst()
    {
        var book = new TalentBook(
        [
            new(1, Warrior,
            [
                new(30, 1, 1, 0, [31], [300, 301]),
                new(31, 1, 2, 0, [], [310]),
            ]),
        ]);

        book.Restore(new Dictionary<uint, int> { [30] = 2, [31] = 1 }, Warrior, level: 12);

        Assert.Equal(2, book.Ranks[30]);
        Assert.Equal(1, book.Ranks[31]);
    }

    [Fact]
    public void Restore_KeepsOnlyWhatTheRulesAllowNow()
    {
        var book = new TalentBook(Trees);

        book.Restore(
            new Dictionary<uint, int> { [12] = 1, [10] = 5, [11] = 1, [999] = 1 }, Warrior, level: 12);

        Assert.Equal(3, book.Ranks[10]);
        Assert.False(book.Ranks.ContainsKey(11));
        Assert.False(book.Ranks.ContainsKey(12));
        Assert.Equal(0, book.Unspent(12));
    }
}
