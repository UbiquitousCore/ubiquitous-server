namespace Ubiquitous.AssetExtractor;

public static class MapInfoColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/map_info.tbl",
        "uuuuufffussuuufuuu",
        [
            LayoutColumn.Must(nameof(MapTableColumns.Id), 0, "uk"),
            LayoutColumn.May(nameof(MapTableColumns.Kind), 3, "u"),
            LayoutColumn.May(nameof(MapTableColumns.MinLevel), 4, "u"),
            LayoutColumn.May(nameof(MapTableColumns.RecallX), 5, "f"),
            LayoutColumn.May(nameof(MapTableColumns.RecallY), 6, "f"),
            LayoutColumn.May(nameof(MapTableColumns.RecallZ), 7, "f"),
            LayoutColumn.May(nameof(MapTableColumns.RecallMapId), 8, "u"),
            LayoutColumn.Must(nameof(MapTableColumns.InternalName), 9, "s"),
            LayoutColumn.May(nameof(MapTableColumns.File), 10, "s"),
        ],
        // 1.0.0
        new LayoutRevision("uuuuufffussuuuf", new LayoutEdit.Lacks(15, 3)),
        // 2.2.0 on: three floats of its own ahead of the recall point
        new LayoutRevision("uuuuuffffffussuuufuuu", new LayoutEdit.Adds(5, 3)));

    public static MapTableColumns Reference => Layout.Reference.As<MapTableColumns>();
}
