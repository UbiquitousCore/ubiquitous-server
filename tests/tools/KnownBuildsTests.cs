using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class KnownBuildsTests
{
    public static TheoryData<string> Builds => new(KnownBuilds.All.Select(known => known.Build));

    [Theory]
    [MemberData(nameof(Builds))]
    public void ACapturedBuildIsNamedAsItself(string build)
    {
        var shapes = KnownBuilds.All.Single(known => known.Build == build).Shapes;

        Assert.Equal(build, KnownBuilds.Identify(shapes));
    }

    [Theory]
    [MemberData(nameof(Builds))]
    public void EveryLayoutKnowsEveryCapturedBuildsFormat(string build)
    {
        var shapes = KnownBuilds.All.Single(known => known.Build == build).Shapes;
        var formats = shapes.Tables.ToDictionary(t => t.Name, t => t.Format, StringComparer.Ordinal);

        foreach (var layout in TableLayouts.All)
        {
            if (!formats.TryGetValue(layout.Table, out var format)) continue;

            Assert.Equal(LayoutBasis.Known, layout.Resolve(format).Basis);
        }
    }

    [Fact]
    public void ATableOfAnotherShapeLeavesTheBuildUnknown()
    {
        var shapes = KnownBuilds.All.Single(known => known.Build == "1.1.4").Shapes;
        var moved = shapes with
        {
            Tables = [.. shapes.Tables.Select(t => t.Name == "tables/spell_new.tbl" ? t with { Format = t.Format + "f" } : t)],
        };

        Assert.Equal(KnownBuilds.Unknown, KnownBuilds.Identify(moved));
    }

    [Fact]
    public void APartOfABuildIsStillNamed()
    {
        var shapes = KnownBuilds.All.Single(known => known.Build == "2.2.0").Shapes;
        var part = shapes with
        {
            Tables = [.. shapes.Tables.Where(t => t.Name is "tables/spell_talent.tbl" or "tables/items.tbl")],
        };

        Assert.Equal("2.2.0", KnownBuilds.Identify(part));
    }
}
