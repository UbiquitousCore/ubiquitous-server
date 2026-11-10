namespace Ubiquitous.AssetExtractor;

public static class TalentColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/spell_talent.tbl",
        "uuuuuuuuuuuuuusu",
        [
            LayoutColumn.Must(nameof(TalentTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(TalentTableColumns.Tier), 1, "u"),
            LayoutColumn.Must(nameof(TalentTableColumns.Column), 2, "u"),
            LayoutColumn.Must(nameof(TalentTableColumns.Tree), 3, "u"),
            LayoutColumn.Must(nameof(TalentTableColumns.PointsInTreeRequired), 4, "u"),
            LayoutColumn.MustRun(nameof(TalentTableColumns.Prerequisites), "u", 5, 6),
            LayoutColumn.Must(nameof(TalentTableColumns.RankCount), 8, "u"),
            LayoutColumn.MustRun(nameof(TalentTableColumns.RankSpells), "u", 9, 10, 11, 12, 13),
        ],
        // 1.1.5: ranks spread as in 2.2.0, with a shorter tail
        new LayoutRevision(
            "uuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuusuuu",
            new LayoutEdit.Adds(10, 4), new LayoutEdit.Adds(11, 4), new LayoutEdit.Adds(12, 4),
            new LayoutEdit.Adds(13, 4), new LayoutEdit.Adds(14, 4), new LayoutEdit.Adds(16, 2)),
        // 2.2.0 on: each rank's spell is followed by four columns of its own
        new LayoutRevision(
            "uuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuusuuuuuuu",
            new LayoutEdit.Adds(10, 4), new LayoutEdit.Adds(11, 4), new LayoutEdit.Adds(12, 4),
            new LayoutEdit.Adds(13, 4), new LayoutEdit.Adds(14, 4), new LayoutEdit.Adds(16, 6)));

    public static readonly ColumnLayout ClassLayout = new(
        "tables/character_class.tbl",
        "uuuuuuuu",
        [
            LayoutColumn.Must(nameof(ClassTreeColumns.ClassId), 0, "uk"),
            LayoutColumn.MustRun(nameof(ClassTreeColumns.Trees), "u", 3, 4, 5),
        ]);

    public static TalentTableColumns Reference => Layout.Reference.As<TalentTableColumns>();

    public static ClassTreeColumns ClassReference => ClassLayout.Reference.As<ClassTreeColumns>();
}
