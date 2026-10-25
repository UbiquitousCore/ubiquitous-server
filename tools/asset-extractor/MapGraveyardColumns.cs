namespace Ubiquitous.AssetExtractor;

public static class MapGraveyardColumns
{
    private const string Table = "tables/map_graveyard.tbl";

    public static MapGraveyardTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "1.1.4")) return Build114;
        TableShape.Expect(Table, format,
            (Build114.Id, "uk"),
            (Build114.X, "f"),
            (Build114.Y, "f"),
            (Build114.Z, "f"),
            (Build114.Yaw, "f"),
            (Build114.ZoneId, "u"),
            (Build114.MapId, "u"),
            (Build114.Faction, "u"));
        return Build114;
    }

    public static readonly MapGraveyardTableColumns Build114 = new(
        Id: 0,
        X: 1,
        Y: 2,
        Z: 3,
        Yaw: 4,
        ZoneId: 5,
        MapId: 6,
        Faction: 7);
}
