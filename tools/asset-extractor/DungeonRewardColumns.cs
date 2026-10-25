namespace Ubiquitous.AssetExtractor;

public static class DungeonRewardColumns
{
    private const string Table = "tables/dungeon_reward.tbl";

    // The 2.2.0 completion reward: which dungeon and difficulty grant
    // which item, with two amounts whose meaning is not settled (a per-run
    // money figure and a second, sometimes zero, figure). The name column
    // points into an empty pool, so names are not read.
    public static readonly DungeonRewardTableColumns Build220 = new(
        Id: 0,
        MapId: 1,
        Difficulty: 2,
        ItemId: 3,
        AmountA: 4,
        AmountB: 5);

    public static DungeonRewardTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "2.2.0")) return Build220;
        TableShape.Expect(Table, format,
            (Build220.Id, "uk"),
            (Build220.MapId, "u"),
            (Build220.Difficulty, "u"),
            (Build220.ItemId, "u"),
            (Build220.AmountA, "u"),
            (Build220.AmountB, "u"));
        return Build220;
    }
}
