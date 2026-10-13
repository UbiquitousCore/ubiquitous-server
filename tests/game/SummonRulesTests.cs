using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.Game;

namespace Ubiquitous.Game.Tests;

public sealed class SummonRulesTests
{
    private static readonly MapDefinition[] Maps =
    [
        new(1, "fields"),
        new(15, "swamp", RecallMapId: 1, RecallX: -883, RecallY: -6, RecallZ: -1916, MinLevel: 20, IsDungeon: true),
        new(16, "swamp legend", RecallMapId: 1, RecallX: -883, RecallY: -6, RecallZ: -1916, MinLevel: 60, IsDungeon: true),
        new(14, "jail", RecallMapId: 1, RecallX: -2677, RecallY: 39, RecallZ: -582, MinLevel: 10, IsDungeon: true),
        new(13, "city", RecallMapId: 1, RecallX: -880, RecallY: 0, RecallZ: -1910, MinLevel: 1),
    ];

    [Fact]
    public void AStone_AsksForTheNormalLevelOfTheDungeonBesideIt()
    {
        Assert.Equal(20, SummonRules.RequiredLevel(Maps, 1, new Position(-877, -7, -1917)));
        Assert.Equal(10, SummonRules.RequiredLevel(Maps, 1, new Position(-2655, 39, -600)));
    }

    [Fact]
    public void AStone_FarFromEveryDungeon_AsksForNoLevel()
    {
        Assert.Equal(0, SummonRules.RequiredLevel(Maps, 1, new Position(0, 0, 0)));
        Assert.Equal(0, SummonRules.RequiredLevel(Maps, 4, new Position(-877, -7, -1917)));
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(45, true)]
    public void OnlyAPlayerAtTheDungeonsLevel_CanBeSummoned(int level, bool allowed) =>
        Assert.Equal(allowed, SummonRules.CanBeSummoned(level, 20));
}
