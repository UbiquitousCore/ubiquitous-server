namespace Ubiquitous.AssetExtractor;

public sealed record GossipMenuTableColumns(
    int Id, int String, IReadOnlyList<int> Scripts, IReadOnlyList<int> SubMenus);

public static class GossipMenuColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/chat_menu.tbl",
        "uuuuuuuuuuuuuu",
        [
            LayoutColumn.Must(nameof(GossipMenuTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(GossipMenuTableColumns.String), 1, "u"),
            LayoutColumn.MustRun(nameof(GossipMenuTableColumns.Scripts), "u", 2, 3, 4, 5, 6, 7),
            LayoutColumn.MustRun(nameof(GossipMenuTableColumns.SubMenus), "u", 8, 9, 10, 11, 12, 13),
        ]);

    public static GossipMenuTableColumns Reference => Layout.Reference.As<GossipMenuTableColumns>();
}
