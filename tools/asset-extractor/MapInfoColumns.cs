namespace Ubiquitous.AssetExtractor;

public static class MapInfoColumns
{
    private const string Table = "tables/map_info.tbl";

    // The name and .wld path travel together as the table's adjacent
    // string pair (the client's rule); everything else is validated
    // where it sits and degraded to absent when retyped.
    public static MapTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "1.1.4")) return Build114;
        TableShape.Expect(Table, format, (Build114.Id, "uk"));
        int name = TableShape.AdjacentPair(format, 's');
        if (name < 0)
        {
            throw new FTableFormatException(
                $"{Table}: no adjacent pair of string columns for the map name and .wld path " +
                $"(format \"{format}\") — no layout in {nameof(TableSchemas)} matches; " +
                "fingerprint the build and add one.");
        }

        return Build114 with
        {
            InternalName = name,
            File = name + 1,
            RecallX = TableShape.Optional(Table, format, Build114.RecallX, "f"),
            RecallY = TableShape.Optional(Table, format, Build114.RecallY, "f"),
            RecallZ = TableShape.Optional(Table, format, Build114.RecallZ, "f"),
            RecallMapId = TableShape.Optional(Table, format, Build114.RecallMapId, "u"),
            Kind = TableShape.Optional(Table, format, Build114.Kind, "u"),
            MinLevel = TableShape.Optional(Table, format, Build114.MinLevel, "u"),
        };
    }

    public static readonly MapTableColumns Build114 = new(
        Id: 0,
        InternalName: 9,
        File: 10,
        RecallX: 5, RecallY: 6, RecallZ: 7, RecallMapId: 8,
        Kind: 3, MinLevel: 4);
}
