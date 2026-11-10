using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

// Where columns the server reads sit in builds other than the reference.
public class BuildLayoutTests
{
    private static string Format(string build, string table) =>
        KnownBuilds.All.Single(known => known.Build == build).Shapes.Tables.Single(t => t.Name == table).Format;

    private static T Columns<T>(ColumnLayout layout, string build) =>
        layout.Resolve(Format(build, layout.Table)).As<T>();

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.1.1")]
    public void BeforeTheMountColumnASpellsLaterColumnsSitOneEarlier(string build)
    {
        var columns = Columns<SpellTableColumns>(SpellNewColumns.Layout, build);

        Assert.Equal(21, columns.MaxRange);
        Assert.Equal(30, columns.DurationMs);
        Assert.Equal([32, 33, 34, 35, 36], columns.EffectSlots);
        Assert.Equal(40, columns.PowerCost);
        Assert.Equal([43, 44, 45], columns.GroupMaskColumns);
    }

    [Theory]
    [InlineData("2.2.0")]
    [InlineData("4.2.21")]
    public void LaterBuildsKeepASpellsReadColumnsInPlace(string build)
    {
        Assert.Equal(SpellNewColumns.Reference with { EffectSlots = null, GroupMaskColumns = null },
            Columns<SpellTableColumns>(SpellNewColumns.Layout, build) with { EffectSlots = null, GroupMaskColumns = null });
    }

    [Fact]
    public void AnOlderItemRowIsOneColumnShortFromItsStackSizeOn()
    {
        var columns = Columns<ItemTableColumns>(ItemColumns.Layout, "1.1.0");

        Assert.Equal(19, columns.RequiredSpellId);
        Assert.Equal(24, columns.MaxStack);
        Assert.Equal(27, columns.FirstStat);
        Assert.Equal(74, columns.QuestId);
    }

    [Theory]
    [InlineData("1.0.3")]
    [InlineData("1.1.0")]
    public void AnOlderVendorNamesItsOneListAsANumber(string build)
    {
        string format = Format(build, CreatureProtoColumns.Layout.Table);
        var columns = CreatureProtoColumns.Layout.Resolve(format).As<CreatureTableColumns>();

        Assert.Equal(55, columns.SellList);
        Assert.Equal('u', format[columns.SellList]);
        Assert.Equal(56, columns.GreetingMenu);
    }

    [Fact]
    public void TheOldestCreaturesHaveNoFestivalLeashOrNoticeRange()
    {
        var resolved = CreatureProtoColumns.Layout.Resolve(Format("1.0.0", CreatureProtoColumns.Layout.Table));
        var columns = resolved.As<CreatureTableColumns>();

        Assert.Equal((-1, -1, -1), (columns.Festival, columns.LeashRange, columns.NoticeDistance));
        Assert.Equal(3, resolved.Notes.Count);
    }

    [Fact]
    public void FromTwoTwoATalentsRankSpellsAreFiveColumnsApart()
    {
        Assert.Equal([9, 14, 19, 24, 29], Columns<TalentTableColumns>(TalentColumns.Layout, "2.2.0").RankSpells);
    }

    [Fact]
    public void FromTwoTwoAMapsRecallPointFollowsThreeNewFloats()
    {
        var columns = Columns<MapTableColumns>(MapInfoColumns.Layout, "2.2.0");

        Assert.Equal((8, 9, 10, 11), (columns.RecallX, columns.RecallY, columns.RecallZ, columns.RecallMapId));
        Assert.Equal((12, 13), (columns.InternalName, columns.File));
    }

    [Fact]
    public void FromTwoFiveAStorageUpgradesNumbersFollowANewColumn()
    {
        var columns = Columns<StorageUpgradeTableColumns>(StorageUpgradeColumns.Layout, "2.5.0");

        Assert.Equal((1, 3, 7), (columns.AreaColumn, columns.SlotCount, columns.FromSlotCount));
    }
}
