using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class ColumnLayoutTests
{
    private sealed record Columns(int Id, int Weight, IReadOnlyList<int> Slots, int Label = -1, int Limit = 7);

    private static readonly ColumnLayout Layout = new(
        "tables/example.tbl",
        "uufuuus",
        [
            LayoutColumn.Must(nameof(Columns.Id), 0, "uk"),
            LayoutColumn.May(nameof(Columns.Weight), 2, "f"),
            LayoutColumn.MayRun(nameof(Columns.Slots), "u", 3, 4, 5),
            LayoutColumn.May(nameof(Columns.Label), 6, "s"),
        ],
        new LayoutRevision("ufuuus", new LayoutEdit.Lacks(1)),
        new LayoutRevision("uuifuuus", new LayoutEdit.Adds(2)),
        new LayoutRevision("uufuuuu") { Own = [LayoutColumn.May(nameof(Columns.Label), 6, "u")] });

    [Fact]
    public void TheReferenceFormatReadsEveryColumnWhereItIsDeclared()
    {
        var columns = Layout.Reference.As<Columns>();

        Assert.Equal(new Columns(0, 2, [3, 4, 5], 6) with { Slots = columns.Slots }, columns);
        Assert.Equal([3, 4, 5], columns.Slots);
        Assert.Empty(Layout.Reference.Notes);
    }

    [Fact]
    public void AColumnAFormatLacksMovesEverythingAfterItDown()
    {
        var columns = Layout.Resolve("ufuuus").As<Columns>();

        Assert.Equal(1, columns.Weight);
        Assert.Equal([2, 3, 4], columns.Slots);
        Assert.Equal(5, columns.Label);
    }

    [Fact]
    public void AColumnAFormatAddsMovesEverythingAfterItUp()
    {
        var columns = Layout.Resolve("uuifuuus").As<Columns>();

        Assert.Equal(3, columns.Weight);
        Assert.Equal([4, 5, 6], columns.Slots);
    }

    [Fact]
    public void AFormatMayHoldAColumnAsAnotherType()
    {
        var resolved = Layout.Resolve("uufuuuu");

        Assert.Equal(6, resolved.As<Columns>().Label);
        Assert.Empty(resolved.Notes);
    }

    [Fact]
    public void ColumnsPastAKnownFormatAreLeftUnreadAndSaid()
    {
        var resolved = Layout.Resolve("uufuuusuu");

        Assert.Equal(LayoutBasis.Extended, resolved.Basis);
        Assert.Equal(6, resolved.As<Columns>().Label);
        Assert.Contains(resolved.Notes, note => note.Contains("2 column(s)"));
    }

    [Fact]
    public void AnUnknownFormatKeepsOnlyTheColumnsWhoseTypeStillAgrees()
    {
        var resolved = Layout.Resolve("uuuuuuu");
        var columns = resolved.As<Columns>();

        Assert.Equal(LayoutBasis.Unrecognised, resolved.Basis);
        Assert.Equal(-1, columns.Weight);
        Assert.Equal(-1, columns.Label);
        Assert.Equal([3, 4, 5], columns.Slots);
        Assert.Contains(resolved.Notes, note => note.Contains(nameof(Columns.Weight)));
    }

    [Fact]
    public void ARunIsReadWholeOrNotAtAll()
    {
        Assert.Empty(Layout.Resolve("uufuufs").As<Columns>().Slots);
    }

    [Fact]
    public void ARequiredColumnOfTheWrongTypeStopsTheImport()
    {
        var error = Assert.Throws<FTableFormatException>(() => Layout.Resolve("sufuuus"));

        Assert.Contains("tables/example.tbl", error.Message);
        Assert.Contains(nameof(Columns.Id), error.Message);
    }

    [Fact]
    public void AParameterNoColumnNamesKeepsItsDefault()
    {
        Assert.Equal(7, Layout.Reference.As<Columns>().Limit);
    }

    public static TheoryData<string, string> Revisions
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var layout in TableLayouts.All)
            {
                foreach (var revision in layout.Revisions) data.Add(layout.Table, revision.Format);
            }

            return data;
        }
    }

    // What a revision says it lacks and adds has to account for its own
    // format: the same length, and each column it keeps still a number, a
    // float or a string as it was.
    [Theory]
    [MemberData(nameof(Revisions))]
    public void ARevisionsEditsAccountForItsFormat(string table, string format)
    {
        var layout = TableLayouts.All.First(l => l.Table == table);
        var revision = layout.Revisions.Single(r => r.Format == format);
        string reference = layout.ReferenceFormat;
        static char Kind(char type) => type is 'f' or 's' ? type : 'n';

        int kept = 0;
        for (int column = 0; column < reference.Length; column++)
        {
            int place = revision.Locate(column);
            if (place < 0) continue;

            kept++;
            Assert.InRange(place, 0, format.Length - 1);
            Assert.Equal(Kind(reference[column]), Kind(format[place]));
        }

        int added = revision.Edits.OfType<LayoutEdit.Adds>().Sum(adds => adds.Count);
        Assert.Equal(format.Length, kept + added);
    }
}
