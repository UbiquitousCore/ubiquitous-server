namespace Ubiquitous.AssetExtractor;

public static class TeleportStopColumns
{
    private const string Table = "tables/teleport_spot.tbl";

    public static TeleportStopTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "1.1.4")) return Build114;
        TableShape.Expect(Table, format,
            (Build114.Id, "uk"),
            (Build114.MapId, "u"),
            (Build114.X, "f"),
            (Build114.Y, "f"),
            (Build114.Z, "f"),
            (Build114.Yaw, "f"),
            (Build114.Type, "u"));
        return Build114;
    }

    public static readonly TeleportStopTableColumns Build114 = new(
        Id: 0,
        MapId: 2,
        X: 3,
        Y: 4,
        Z: 5,
        Yaw: 6,
        Type: 7);
}
