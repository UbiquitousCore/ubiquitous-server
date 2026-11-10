namespace Ubiquitous.AssetExtractor;

public sealed record StaticObjectDisplayTableColumns(int Id, int Model);

public static class StaticObjectDisplayColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/staticobject_displayinfo.tbl",
        "usuuuuussssssssssss",
        [
            LayoutColumn.Must(nameof(StaticObjectDisplayTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(StaticObjectDisplayTableColumns.Model), 1, "s"),
        ]);
}
