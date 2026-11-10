namespace Ubiquitous.AssetExtractor;

public static class TeleportStopColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/teleport_spot.tbl",
        "usuffffu",
        [
            LayoutColumn.Must(nameof(TeleportStopTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(TeleportStopTableColumns.MapId), 2, "u"),
            LayoutColumn.Must(nameof(TeleportStopTableColumns.X), 3, "f"),
            LayoutColumn.Must(nameof(TeleportStopTableColumns.Y), 4, "f"),
            LayoutColumn.Must(nameof(TeleportStopTableColumns.Z), 5, "f"),
            LayoutColumn.Must(nameof(TeleportStopTableColumns.Yaw), 6, "f"),
            LayoutColumn.Must(nameof(TeleportStopTableColumns.Type), 7, "u"),
        ]);

    public static TeleportStopTableColumns Reference => Layout.Reference.As<TeleportStopTableColumns>();
}
