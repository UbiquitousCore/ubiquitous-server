namespace Ubiquitous.AssetExtractor;

public static class StaticObjectColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/staticobject_spawns.tbl",
        "uuuffffffuufffuf",
        [
            LayoutColumn.Must(nameof(StaticObjectTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(StaticObjectTableColumns.EntryId), 1, "u"),
            LayoutColumn.Must(nameof(StaticObjectTableColumns.MapId), 2, "u"),
            LayoutColumn.Must(nameof(StaticObjectTableColumns.X), 3, "f"),
            LayoutColumn.Must(nameof(StaticObjectTableColumns.Y), 4, "f"),
            LayoutColumn.Must(nameof(StaticObjectTableColumns.Z), 5, "f"),
            LayoutColumn.Must(nameof(StaticObjectTableColumns.Yaw), 7, "f"),
            LayoutColumn.May(nameof(StaticObjectTableColumns.State), 9, "u"),
            LayoutColumn.May(nameof(StaticObjectTableColumns.Flags), 10, "u"),
            LayoutColumn.May(nameof(StaticObjectTableColumns.Scale), 11, "f"),
        ],
        // 1.0.0 to 1.0.3: one scale, where later builds carry one per axis
        new LayoutRevision("uuuffffffuufuf", new LayoutEdit.Lacks(12, 2)));

    public static StaticObjectTableColumns Reference => Layout.Reference.As<StaticObjectTableColumns>();
}
