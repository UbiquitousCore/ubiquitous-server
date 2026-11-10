namespace Ubiquitous.AssetExtractor;

public static class StaticObjectProtoColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/staticobject_proto.tbl",
        "uuuuuuufuuuuuuuuuuuu",
        [
            LayoutColumn.Must(nameof(StaticObjectProtoTableColumns.Entry), 0, "uk"),
            LayoutColumn.Must(nameof(StaticObjectProtoTableColumns.Type), 1, "u"),
            LayoutColumn.May(nameof(StaticObjectProtoTableColumns.Display), 3, "u"),
            LayoutColumn.May(nameof(StaticObjectProtoTableColumns.Festival), 19, "u"),
        ],
        // 1.0.0 to 1.0.3
        new LayoutRevision("uuuuuuufuuuuuuuuuu", new LayoutEdit.Lacks(18, 2)),
        // 1.1.0 and 1.1.1: the festival is there, the column ahead of it is not
        new LayoutRevision("uuuuuuufuuuuuuuuuuu", new LayoutEdit.Lacks(18)));

    public static StaticObjectProtoTableColumns Reference => Layout.Reference.As<StaticObjectProtoTableColumns>();
}
