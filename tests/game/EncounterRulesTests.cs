using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class EncounterRulesTests
{
    [Fact]
    public void NoticeRange_AddsHalfAUnitPerLevelTheCreatureHoldsOverThePlayer()
    {
        Assert.Equal(10, EncounterRules.NoticeRange(10, 5, 5, CreatureRank.Ordinary));
        Assert.Equal(12, EncounterRules.NoticeRange(10, 9, 5, CreatureRank.Ordinary));
        Assert.Equal(8, EncounterRules.NoticeRange(10, 5, 9, CreatureRank.Ordinary));
        Assert.Equal(11, EncounterRules.NoticeRange(10, 6, 5, CreatureRank.Ordinary));
    }

    [Fact]
    public void NoticeRange_CountsACreatureAboveTheLevelCapAsAtTheCap()
    {
        Assert.Equal(
            EncounterRules.NoticeRange(8, LevelingService.MaxLevel, 58, CreatureRank.Ordinary),
            EncounterRules.NoticeRange(8, LevelingService.MaxLevel + 20, 58, CreatureRank.Ordinary));
    }

    [Theory]
    [InlineData(CreatureRank.Ordinary, 8)]
    [InlineData(CreatureRank.Rare, 10)]
    [InlineData(CreatureRank.Elite, 11)]
    [InlineData(CreatureRank.Boss, 12)]
    [InlineData(CreatureRank.WorldBoss, 12)]
    public void NoticeRange_AddsTheRankBonus(CreatureRank rank, int expected)
    {
        Assert.Equal(expected, EncounterRules.NoticeRange(8, 5, 5, rank));
    }

    [Fact]
    public void NoticeRange_WidensForACreatureThatFightsAtRange()
    {
        Assert.Equal(10, EncounterRules.NoticeRange(8, 5, 5, CreatureRank.Ordinary, fightsAtRange: true));
    }

    [Fact]
    public void NoticeRange_AddsTheRankAndRangedBonusesTogether()
    {
        Assert.Equal(12, EncounterRules.NoticeRange(6, 5, 5, CreatureRank.Boss, fightsAtRange: true));
    }

    [Fact]
    public void NoticeRange_NeverReachesAcrossARoomAndNeverGoesBlind()
    {
        Assert.Equal((int)EncounterRules.MaxNoticeRange, EncounterRules.NoticeRange(10, 30, 5, CreatureRank.Boss));
        Assert.Equal((int)EncounterRules.MinNoticeRange, EncounterRules.NoticeRange(10, 1, 60, CreatureRank.Ordinary));
    }

    [Fact]
    public void NoticeRange_ReadsRankAndRangedStraightOffTheTemplate()
    {
        var archer = new CreatureDefinition(1, "archer", 5, 100, RangedAttackIntervalMs: 2000);
        var lieutenant = new CreatureDefinition(2, "lieutenant", 5, 100, Rank: CreatureRank.Elite);

        Assert.Equal(10, EncounterRules.NoticeRange(8, archer, playerLevel: 5));
        Assert.Equal(11, EncounterRules.NoticeRange(8, lieutenant, playerLevel: 5));
        Assert.Equal(8, EncounterRules.NoticeRange(8, creature: null, playerLevel: 5));
    }

    [Fact]
    public void FightsAtRange_IsWhetherTheRowCarriesARangedSwingSpeed()
    {
        Assert.False(new CreatureDefinition(1, "wolf", 5, 100).FightsAtRange);
        Assert.False(new CreatureDefinition(1, "wolf", 5, 100, RangedAttackIntervalMs: 0).FightsAtRange);
        Assert.True(new CreatureDefinition(1, "mage", 5, 100, RangedAttackIntervalMs: 2000).FightsAtRange);
    }
}
