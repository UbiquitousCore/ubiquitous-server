namespace Ubiquitous.AssetExtractor;

// The table arrives with 2.2.0, so its reference is that build's shape.
public static class DungeonRewardColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/dungeon_reward.tbl",
        "uuuuuus",
        [
            LayoutColumn.Must(nameof(DungeonRewardTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(DungeonRewardTableColumns.MapId), 1, "u"),
            LayoutColumn.Must(nameof(DungeonRewardTableColumns.Difficulty), 2, "u"),
            LayoutColumn.Must(nameof(DungeonRewardTableColumns.ItemId), 3, "u"),
            LayoutColumn.Must(nameof(DungeonRewardTableColumns.AmountA), 4, "u"),
            LayoutColumn.Must(nameof(DungeonRewardTableColumns.AmountB), 5, "u"),
        ]);

    public static DungeonRewardTableColumns Reference => Layout.Reference.As<DungeonRewardTableColumns>();
}
