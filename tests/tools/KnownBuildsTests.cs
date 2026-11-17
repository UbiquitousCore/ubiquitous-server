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

    [Fact]
    public void TwoBuildsWithTheSameShapes_ResolveTo114_ElseTheEarlierByVersion()
    {
        var shapes = new TableFingerprint(null, [new TableFingerprintEntry("tables/a.tbl", null, 2, "us")], DateTimeOffset.UnixEpoch);

        Assert.Equal("1.1.4", KnownBuilds.Identify(shapes, [("2.2.0", shapes), ("1.1.4", shapes)]));
        Assert.Equal("1.1.4", KnownBuilds.Identify(shapes, [("1.1.10", shapes), ("1.1.4", shapes)]));
        Assert.Equal("1.1.5", KnownBuilds.Identify(shapes, [("1.1.10", shapes), ("1.1.5", shapes)]));
    }

    // Read from another table, never from a column: a name from the build's
    // string table, a quest's giver from the tables of who starts which quest.
    private static readonly HashSet<string> ReadFromOtherTables = ["Name", "GiverEntryId"];

    // Each `*Columns` class pairs a layout with the record it is read into:
    // `Layout` with `Reference`, `ClassLayout` with `ClassReference`, and so on.
    public static TheoryData<string> Pairings => new(LayoutRecordPairs().Select(pair => pair.Name));

    [Theory]
    [MemberData(nameof(Pairings))]
    public void EveryColumnARecordNeedsIsNamedByItsLayout(string pairing)
    {
        var (_, layout, record) = LayoutRecordPairs().Single(pair => pair.Name == pairing);
        var named = layout.Columns.Select(column => column.Name.Split('[')[0]).ToHashSet(StringComparer.Ordinal);
        var missing = record.GetConstructors().Single().GetParameters()
            .Where(parameter => !parameter.HasDefaultValue && !named.Contains(parameter.Name!)
                && !ReadFromOtherTables.Contains(parameter.Name!))
            .Select(parameter => parameter.Name)
            .ToList();
        Assert.True(missing.Count == 0, $"{pairing}: no column for {string.Join(", ", missing)}");
    }

    private static IEnumerable<(string Name, ColumnLayout Layout, Type Record)> LayoutRecordPairs() =>
        typeof(ColumnLayout).Assembly.GetTypes()
            .Where(type => type.IsAbstract && type.IsSealed && type.Name.EndsWith("Columns", StringComparison.Ordinal))
            .SelectMany(holder => holder.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(property => property.Name.EndsWith("Reference", StringComparison.Ordinal))
                .Select(property => (
                    Name: $"{holder.Name}.{property.Name}",
                    Layout: holder.GetField(property.Name[..^"Reference".Length] + "Layout")?.GetValue(null) as ColumnLayout,
                    Record: property.PropertyType))
                .Where(pair => pair.Layout is not null)
                .Select(pair => (pair.Name, pair.Layout!, pair.Record)));
}
