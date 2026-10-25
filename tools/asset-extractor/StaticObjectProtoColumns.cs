using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public static class StaticObjectProtoColumns
{
    private const string Table = "tables/staticobject_proto.tbl";

    public static StaticObjectProtoTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "1.1.4")) return Build114;
        TableShape.Expect(Table, format,
            (Build114.Entry, "uk"),
            (Build114.Type, "u"));
        return Build114 with
        {
            Festival = TableShape.Optional(Table, format, Build114.Festival, "u"),
        };
    }

    public static readonly StaticObjectProtoTableColumns Build114 = new(
        Entry: 0,
        Type: 1,
        Festival: 19);
}
