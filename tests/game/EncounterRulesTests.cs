using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class EncounterRulesTests
{
    [Fact]
    public void NoticeRange_GrowsFivePercentForEachLevelTheCreatureHoldsOverThePlayer()
    {
        Assert.Equal(8, EncounterRules.NoticeRange(8, 5, 5, CreatureRank.Ordinary, LevelingService.SeenMaxLevel));
        Assert.Equal(12, EncounterRules.NoticeRange(8, 15, 5, CreatureRank.Ordinary, LevelingService.SeenMaxLevel));
        Assert.Equal(4, EncounterRules.NoticeRange(8, 5, 15, CreatureRank.Ordinary, LevelingService.SeenMaxLevel));
    }

    [Fact]
    public void NoticeRange_CountsACreatureAboveTheLevelCapAsAtTheCap()
    {
        Assert.Equal(
            EncounterRules.NoticeRange(8, LevelingService.SeenMaxLevel, 50, CreatureRank.Ordinary, LevelingService.SeenMaxLevel),
            EncounterRules.NoticeRange(8, LevelingService.SeenMaxLevel + 20, 50, CreatureRank.Ordinary, LevelingService.SeenMaxLevel));
    }

    [Theory]
    [InlineData(CreatureRank.Ordinary, 10)]
    [InlineData(CreatureRank.Rare, 12)]
    [InlineData(CreatureRank.Elite, 12)]
    [InlineData(CreatureRank.Boss, 13)]
    [InlineData(CreatureRank.WorldBoss, 13)]
    public void NoticeRange_ScalesByRank(CreatureRank rank, int expected)
    {
        Assert.Equal(expected, EncounterRules.NoticeRange(10, 5, 5, rank, LevelingService.SeenMaxLevel));
    }

    [Fact]
    public void NoticeRange_WidensTenPercentForACreatureThatFightsAtRange()
    {
        Assert.Equal(11, EncounterRules.NoticeRange(10, 5, 5, CreatureRank.Ordinary, LevelingService.SeenMaxLevel, fightsAtRange: true));
    }

    [Fact]
    public void NoticeRange_NeverReachesAcrossARoomAndNeverGoesBlind()
    {
        Assert.Equal((int)EncounterRules.MaxNoticeRange, EncounterRules.NoticeRange(10, 30, 5, CreatureRank.Boss, LevelingService.SeenMaxLevel));
        Assert.Equal((int)EncounterRules.MinNoticeRange, EncounterRules.NoticeRange(10, 1, 60, CreatureRank.Ordinary, LevelingService.SeenMaxLevel));
    }

    [Fact]
    public void NoticeRange_ReadsRankAndRangedStraightOffTheTemplate()
    {
        var archer = new CreatureDefinition(1, "archer", 5, 100, RangedAttackIntervalMs: 2000);
        var lieutenant = new CreatureDefinition(2, "lieutenant", 5, 100, Rank: CreatureRank.Elite);

        Assert.Equal(11, EncounterRules.NoticeRange(10, archer, playerLevel: 5, LevelingService.SeenMaxLevel));
        Assert.Equal(12, EncounterRules.NoticeRange(10, lieutenant, playerLevel: 5, LevelingService.SeenMaxLevel));
        Assert.Equal(10, EncounterRules.NoticeRange(10, creature: null, playerLevel: 5, LevelingService.SeenMaxLevel));
    }

    [Fact]
    public void FightsAtRange_IsWhetherTheRowCarriesARangedSwingSpeed()
    {
        Assert.False(new CreatureDefinition(1, "wolf", 5, 100).FightsAtRange);
        Assert.False(new CreatureDefinition(1, "wolf", 5, 100, RangedAttackIntervalMs: 0).FightsAtRange);
        Assert.True(new CreatureDefinition(1, "mage", 5, 100, RangedAttackIntervalMs: 2000).FightsAtRange);
    }
}
