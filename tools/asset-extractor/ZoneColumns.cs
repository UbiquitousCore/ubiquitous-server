namespace Ubiquitous.AssetExtractor;

public static class ZoneColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/zones.tbl",
        "iiiuffffffffuuffuuuussssssfffffusiii",
        [
            LayoutColumn.Must(nameof(ZoneTableColumns.Id), 0, "iu"),
            LayoutColumn.Must(nameof(ZoneTableColumns.ParentZoneId), 2, "iu"),
            LayoutColumn.Must(nameof(ZoneTableColumns.MapId), 3, "u"),
            LayoutColumn.Must(nameof(ZoneTableColumns.MinX), 4, "f"),
            LayoutColumn.Must(nameof(ZoneTableColumns.MinZ), 6, "f"),
            LayoutColumn.Must(nameof(ZoneTableColumns.MaxX), 8, "f"),
            LayoutColumn.Must(nameof(ZoneTableColumns.MaxZ), 10, "f"),
        ],
        // 1.0.0 to 1.0.3
        new LayoutRevision("iiiuffffffffuuffuuuusssiii", new LayoutEdit.Lacks(22, 10)),
        // 2.2.0 on
        new LayoutRevision("iiiuffffffffuuffffuuuussssssfffffusiiiu", new LayoutEdit.Adds(16, 2), new LayoutEdit.Adds(36)));

    public static ZoneTableColumns Reference => Layout.Reference.As<ZoneTableColumns>();
}
