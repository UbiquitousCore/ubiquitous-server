using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class PlayerStatCurvesTests
{
    [Theory]
    [InlineData(1, 1, 11)]
    [InlineData(1, 13, 239)]
    [InlineData(1, 14, 259)]
    [InlineData(1, 35, 1141)]
    [InlineData(1, 36, 1183)]
    [InlineData(1, 60, 3391)]
    [InlineData(2, 20, 275)]
    [InlineData(3, 4, 62)]
    [InlineData(4, 60, 2641)]
    public void BaseMaxHealth_ReadsTheClassAndLevel(int classId, int level, int expected)
    {
        Assert.Equal(expected, PlayerStatCurves.BaseMaxHealth(classId, level, fallback: -1));
    }

    [Fact]
    public void BaseMaxHealth_RisesEveryLevelForEveryClass()
    {
        for (var classId = 1; classId <= 4; classId++)
        {
            for (var level = 2; level <= LevelingService.MaxLevel; level++)
            {
                Assert.True(
                    PlayerStatCurves.BaseMaxHealth(classId, level, fallback: -1)
                        > PlayerStatCurves.BaseMaxHealth(classId, level - 1, fallback: -1),
                    $"class {classId} level {level}");
            }
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(LevelingService.MaxLevel + 5, LevelingService.MaxLevel)]
    public void BaseMaxHealth_ReadsTheNearestLevelOutsideTheTable(int level, int nearest)
    {
        Assert.Equal(
            PlayerStatCurves.BaseMaxHealth(1, nearest, fallback: -1),
            PlayerStatCurves.BaseMaxHealth(1, level, fallback: -1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void BaseMaxHealth_ReturnsTheFallbackForAnUnknownClassId(int classId)
    {
        Assert.Equal(999, PlayerStatCurves.BaseMaxHealth(classId, level: 10, fallback: 999));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 50)]
    [InlineData(3, 1)]
    [InlineData(3, 50)]
    public void BaseMaxPower_IsZeroForTheTwoClassesWithoutAPowerPool(int classId, int level)
    {
        Assert.Equal(0, PlayerStatCurves.BaseMaxPower(classId, level, fallback: -1));
    }

    [Theory]
    [InlineData(2, 1, 101)]
    [InlineData(2, 14, 534)]
    [InlineData(2, 60, 3898)]
    [InlineData(4, 1, 101)]
    [InlineData(4, 36, 1683)]
    [InlineData(4, 60, 3627)]
    public void BaseMaxPower_ReadsTheClassAndLevelForThePowerClasses(int classId, int level, int expected)
    {
        Assert.Equal(expected, PlayerStatCurves.BaseMaxPower(classId, level, fallback: -1));
    }

    [Fact]
    public void BaseMaxPower_ReturnsTheFallbackForAnUnknownClassId()
    {
        Assert.Equal(999, PlayerStatCurves.BaseMaxPower(classId: 0, level: 10, fallback: 999));
    }

    [Fact]
    public void AttackPower_ClassOneCountsLevelAndStrength()
    {
        Assert.Equal(15, PlayerStatCurves.AttackPower(classId: 1, level: 5, strength: 10, agility: 999));
    }

    [Theory]
    [InlineData(1, 10, 10, 7)]
    [InlineData(10, 10, 11, 34)]
    [InlineData(20, 11, 13, 65)]
    [InlineData(40, 15, 20, 129)]
    [InlineData(60, 20, 28, 194)]
    public void AttackPower_ClassTwoCountsLevelAndStrength(int level, int strength, int agility, int expected)
    {
        Assert.Equal(expected, PlayerStatCurves.AttackPower(classId: 2, level, strength, agility));
    }

    [Theory]
    [InlineData(1, 14, 11, 10)]
    [InlineData(10, 18, 14, 36)]
    [InlineData(20, 27, 21, 74)]
    [InlineData(40, 49, 42, 158)]
    [InlineData(60, 85, 73, 270)]
    public void AttackPower_ClassFourCountsLevelAndDoubleStrength(int level, int strength, int agility, int expected)
    {
        Assert.Equal(expected, PlayerStatCurves.AttackPower(classId: 4, level, strength, agility));
    }

    [Fact]
    public void AttackPower_ClassThreeAlsoCountsAgility()
    {
        Assert.Equal(138, PlayerStatCurves.AttackPower(classId: 3, level: 4, strength: 100, agility: 50));
    }

    [Fact]
    public void AttackPower_FloorsAtZeroRatherThanGoingNegative()
    {
        Assert.Equal(0, PlayerStatCurves.AttackPower(classId: 4, level: 1, strength: 5, agility: 0));
    }

    [Fact]
    public void AttackPower_IsZeroForAnUnknownClassId()
    {
        Assert.Equal(0, PlayerStatCurves.AttackPower(classId: 0, level: 10, strength: 1000, agility: 1000));
    }

    [Theory]
    [InlineData(1, 1, 21, 12, 18, 9)]
    [InlineData(1, 14, 36, 20, 31, 12)]
    [InlineData(1, 60, 121, 72, 107, 35)]
    [InlineData(2, 36, 14, 18, 30, 77)]
    [InlineData(2, 60, 20, 28, 44, 128)]
    [InlineData(3, 60, 64, 124, 89, 44)]
    [InlineData(4, 60, 85, 73, 85, 109)]
    public void AttributesAt_GrowsAHumansStartingStatsWithLevel(
        int classId, int level, int strength, int agility, int stamina, int wisdom)
    {
        var levelOne = StartingLoadouts.For(PlayerRaceIds.Human, classId)!.Attributes;

        Assert.Equal(
            new StartingAttributes(strength, agility, stamina, wisdom),
            PlayerStatCurves.AttributesAt(classId, level, levelOne));
    }

    [Fact]
    public void AttributesAt_AddsTheSameClassGainWhateverTheRace()
    {
        var orc = StartingLoadouts.For(PlayerRaceIds.Orc, 1)!.Attributes;

        Assert.Equal(new StartingAttributes(123, 71, 107, 34), PlayerStatCurves.AttributesAt(1, 60, orc));
    }

    [Fact]
    public void AttributesAt_NeverLowersAStatOnLevelUp()
    {
        var zero = new StartingAttributes(0, 0, 0, 0);
        for (var classId = 1; classId <= 4; classId++)
        {
            for (var level = 2; level <= LevelingService.MaxLevel; level++)
            {
                var before = PlayerStatCurves.AttributesAt(classId, level - 1, zero);
                var after = PlayerStatCurves.AttributesAt(classId, level, zero);
                Assert.True(
                    after.Strength >= before.Strength && after.Agility >= before.Agility
                        && after.Stamina >= before.Stamina && after.Intellect >= before.Intellect,
                    $"class {classId} level {level}");
            }
        }
    }

    [Fact]
    public void AttributesAt_LeavesAnUnknownClassAtItsStartingStats()
    {
        var levelOne = new StartingAttributes(5, 6, 7, 8);

        Assert.Equal(levelOne, PlayerStatCurves.AttributesAt(classId: 0, level: 60, levelOne));
    }
}
