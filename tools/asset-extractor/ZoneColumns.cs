namespace Ubiquitous.AssetExtractor;

public static class ZoneColumns
{
    private const string Table = "tables/zones.tbl";

    public static ZoneTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "1.1.4")) return Build114;
        TableShape.Expect(Table, format,
            (Build114.Id, "iu"),
            (Build114.MapId, "u"),
            (Build114.ParentZoneId, "iu"),
            (Build114.MinX, "f"),
            (Build114.MinZ, "f"),
            (Build114.MaxX, "f"),
            (Build114.MaxZ, "f"));
        return Build114;
    }

    public static readonly ZoneTableColumns Build114 = new(
        Id: 0,
        MapId: 3,
        ParentZoneId: 2,
        Name: -1,
        MinX: 4,
        MinZ: 6,
        MaxX: 8,
        MaxZ: 10);
}
