namespace Ubiquitous.Game.Tests;

public sealed class LevelingServiceTests
{
    [Theory]
    [InlineData(10, 10, 100)]
    [InlineData(10, 11, 105)]
    [InlineData(10, 19, 145)]
    [InlineData(10, 20, 150)]
    [InlineData(10, 30, 150)]
    [InlineData(1, 11, 83)]
    [InlineData(10, 9, 90)]
    [InlineData(10, 8, 80)]
    [InlineData(10, 7, 40)]
    [InlineData(30, 26, 67)]
    [InlineData(60, 50, 58)]
    [InlineData(10, 5, 0)]
    [InlineData(10, 1, 0)]
    [InlineData(60, 49, 29)]
    [InlineData(60, 48, 0)]
    [InlineData(8, 11, 104)]
    public void KillXp_ScalesTheBaseByTheLevelGap(int playerLevel, int creatureLevel, int expected)
    {
        Assert.Equal(expected, LevelingService.KillXp(playerLevel, creatureLevel));
    }

    [Theory]
    [InlineData(1, 250)]
    [InlineData(2, 600)]
    [InlineData(9, 5850)]
    [InlineData(10, 7000)]
    [InlineData(20, 20800)]
    [InlineData(25, 27950)]
    [InlineData(29, 33050)]
    [InlineData(30, 34200)]
    [InlineData(31, 41300)]
    [InlineData(40, 117200)]
    [InlineData(59, 346500)]
    public void XpToNextLevel_ReturnsTheBarTotalForTheLevel(int level, int expected)
    {
        Assert.Equal(expected, LevelingService.XpToNextLevel(level));
    }

    [Theory]
    [InlineData(LevelingService.MaxLevel)]
    [InlineData(LevelingService.MaxLevel + 1)]
    [InlineData(0)]
    public void XpToNextLevel_IsZeroWhereThereIsNoNextLevel(int level)
    {
        Assert.Equal(0, LevelingService.XpToNextLevel(level));
    }

    [Fact]
    public void XpToNextLevel_RisesEveryLevelInWholeFifties()
    {
        for (var level = 1; level < LevelingService.MaxLevel; level++)
        {
            Assert.Equal(0, LevelingService.XpToNextLevel(level) % 50);
            if (level > 1) Assert.True(LevelingService.XpToNextLevel(level) > LevelingService.XpToNextLevel(level - 1));
        }
    }

    [Theory]
    [InlineData(1000, 20, 10, false, 1200)]
    [InlineData(1000, 16, 10, false, 1200)]
    [InlineData(1000, 13, 10, false, 1100)]
    [InlineData(1000, 15, 10, false, 1100)]
    [InlineData(1000, 10, 10, false, 1000)]
    [InlineData(1000, 12, 10, false, 1000)]
    [InlineData(1000, 9, 10, false, 1000)]
    [InlineData(1000, 8, 10, false, 800)]
    [InlineData(1000, 6, 10, false, 800)]
    [InlineData(1000, 5, 10, false, 500)]
    [InlineData(1000, 2, 10, false, 500)]
    [InlineData(1000, 1, 10, false, 100)]
    [InlineData(1000, 1, 60, false, 100)]
    [InlineData(999, 20, 10, false, 1198)]
    [InlineData(19, 1, 10, false, 1)]
    [InlineData(1000, 1, 60, true, 1000)]
    [InlineData(1000, 60, 1, true, 1000)]
    [InlineData(0, 20, 10, false, 0)]
    public void QuestXp_ScalesTheListedRewardByTheLevelGap(
        int rewardXp, int questOwnLevel, int playerLevel, bool isRepeatable, int expected)
    {
        Assert.Equal(expected, LevelingService.QuestXp(rewardXp, questOwnLevel, playerLevel, isRepeatable));
    }

    [Fact]
    public void ApplyXp_AccumulatesWithoutLevelingBelowTheThreshold()
    {
        var result = LevelingService.ApplyXp(level: 1, xp: 0, gained: 100);
        Assert.Equal(1, result.Level);
        Assert.Equal(100, result.Xp);
        Assert.Equal(0, result.LevelsGained);
    }

    [Fact]
    public void ApplyXp_LevelsUpOnceOnceTheThresholdIsCrossed()
    {
        var result = LevelingService.ApplyXp(level: 1, xp: 200, gained: 100);
        Assert.Equal(2, result.Level);
        Assert.Equal(50, result.Xp);
        Assert.Equal(1, result.LevelsGained);
    }

    [Fact]
    public void ApplyXp_CanCarryAcrossSeveralLevelsInOneGrant()
    {
        var result = LevelingService.ApplyXp(level: 1, xp: 0, gained: 250 + 600 + 100);
        Assert.Equal(3, result.Level);
        Assert.Equal(100, result.Xp);
        Assert.Equal(2, result.LevelsGained);
    }

    [Fact]
    public void ApplyXp_AtMaxLevel_GrantsNothingFurther()
    {
        var result = LevelingService.ApplyXp(level: LevelingService.MaxLevel, xp: 0, gained: 999_999);
        Assert.Equal(LevelingService.MaxLevel, result.Level);
        Assert.Equal(0, result.Xp);
        Assert.Equal(0, result.LevelsGained);
    }

    [Fact]
    public void ApplyXp_StopsExactlyAtMaxLevel_EvenWithXpLeftOver()
    {
        var justBelowCap = LevelingService.MaxLevel - 1;
        var needed = LevelingService.XpToNextLevel(justBelowCap);
        var result = LevelingService.ApplyXp(level: justBelowCap, xp: 0, gained: needed + 10_000);
        Assert.Equal(LevelingService.MaxLevel, result.Level);
        Assert.Equal(0, result.Xp);
        Assert.Equal(1, result.LevelsGained);
    }

    [Fact]
    public void ApplyXp_AtMaxLevel_ZeroesXpAlreadyHeld()
    {
        var result = LevelingService.ApplyXp(level: LevelingService.MaxLevel, xp: 12_345, gained: 100);
        Assert.Equal(0, result.Xp);
        Assert.Equal(0, result.LevelsGained);
    }

    [Theory]
    [InlineData(LevelingService.MaxLevel, 12_345, 0)]
    [InlineData(LevelingService.MaxLevel + 1, 50, 0)]
    [InlineData(10, 400, 400)]
    [InlineData(10, -5, 0)]
    public void HeldXp_IsZeroAtTheCapAndNeverNegative(int level, int stored, int expected)
    {
        Assert.Equal(expected, LevelingService.HeldXp(level, stored));
    }
}
