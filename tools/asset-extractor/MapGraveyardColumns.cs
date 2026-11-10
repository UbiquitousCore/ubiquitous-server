namespace Ubiquitous.AssetExtractor;

public static class MapGraveyardColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/map_graveyard.tbl",
        "uffffuuu",
        [
            LayoutColumn.Must(nameof(MapGraveyardTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(MapGraveyardTableColumns.X), 1, "f"),
            LayoutColumn.Must(nameof(MapGraveyardTableColumns.Y), 2, "f"),
            LayoutColumn.Must(nameof(MapGraveyardTableColumns.Z), 3, "f"),
            LayoutColumn.Must(nameof(MapGraveyardTableColumns.Yaw), 4, "f"),
            LayoutColumn.Must(nameof(MapGraveyardTableColumns.ZoneId), 5, "u"),
            LayoutColumn.Must(nameof(MapGraveyardTableColumns.MapId), 6, "u"),
            LayoutColumn.Must(nameof(MapGraveyardTableColumns.Faction), 7, "u"),
        ]);

    public static MapGraveyardTableColumns Reference => Layout.Reference.As<MapGraveyardTableColumns>();
}
