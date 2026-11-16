using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

[Collection(nameof(BasicSpellsTests))]
public sealed class BuildRulesTests : IDisposable
{
    private static Dictionary<uint, SpellDefinition> Spells(params uint[] ids) =>
        ids.ToDictionary(id => id, id => new SpellDefinition(id, $"spell-{id}", 0, 0, 0f));

    public void Dispose()
    {
        LevelingService.UseCapOf("1.1.4");
        StartingLoadouts.UseClassesOf(Spells());
        BasicSpells.UseNumberingOf(Spells(108));
    }

    [Theory]
    [InlineData("1.1.4", 60)]
    [InlineData("2.2.0", 70)]
    [InlineData("2.5.0", 70)]
    [InlineData("4.2.21", 75)]
    [InlineData("", 60)]
    public void EachBuildLevelsToItsOwnCap(string build, int cap)
    {
        LevelingService.UseCapOf(build);

        Assert.Equal(cap, LevelingService.MaxLevel);
        Assert.True(LevelingService.XpToNextLevel(cap - 1) > LevelingService.XpToNextLevel(cap - 2));
        Assert.Equal(0, LevelingService.XpToNextLevel(cap));
        Assert.Equal(cap, LevelingService.ApplyXp(cap - 1, 0, int.MaxValue / 2).Level);
    }

    [Fact]
    public void AFlameKnightCanBeMadeOnlyWhereTheBuildHasItsSpells()
    {
        StartingLoadouts.UseClassesOf(Spells());
        Assert.Null(StartingLoadouts.For(PlayerRaceIds.Human, PlayerClassIds.FlameKnight));

        StartingLoadouts.UseClassesOf(Spells(15427, 15428, 15429, 15430));
        var knight = StartingLoadouts.For(PlayerRaceIds.Human, PlayerClassIds.FlameKnight);

        Assert.NotNull(knight);
        Assert.Contains(15427u, knight.Spells);
        Assert.Equal(PowerKinds.FlameKnightPower, PowerKinds.ForClass(PlayerClassIds.FlameKnight));
        Assert.True(PlayerStatCurves.BaseMaxPower(PlayerClassIds.FlameKnight, 1, 0) > 0);
    }

    [Fact]
    public void ACharacterPastTheLevelsReadInPlayKeepsTheLastLevelsGains()
    {
        LevelingService.UseCapOf("4.2.21");
        var levelOne = new StartingAttributes(10, 10, 10, 10);

        Assert.Equal(
            PlayerStatCurves.AttributesAt(PlayerClassIds.Warrior, 60, levelOne),
            PlayerStatCurves.AttributesAt(PlayerClassIds.Warrior, 75, levelOne));
    }
}
