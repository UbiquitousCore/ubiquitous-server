namespace Ubiquitous.AssetExtractor;

public static class StaticObjectColumns
{
    private const string Table = "tables/staticobject_spawns.tbl";

    public static StaticObjectTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "1.1.4")) return Build114;
        TableShape.Expect(Table, format,
            (Build114.Id, "uk"),
            (Build114.MapId, "u"),
            (Build114.EntryId, "u"),
            (Build114.X, "f"),
            (Build114.Y, "f"),
            (Build114.Z, "f"),
            (Build114.Yaw, "f"));
        return Build114 with
        {
            State = TableShape.Optional(Table, format, Build114.State, "u"),
            Flags = TableShape.Optional(Table, format, Build114.Flags, "u"),
        };
    }

    public static readonly StaticObjectTableColumns Build114 = new(
        Id: 0,
        MapId: 2,
        EntryId: 1,
        X: 3,
        Y: 4,
        Z: 5,
        Yaw: 7,
        State: 9,
        Flags: 10);
}
