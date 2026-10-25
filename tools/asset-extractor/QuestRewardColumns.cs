namespace Ubiquitous.AssetExtractor;

public static class QuestRewardColumns
{
    private const string Table = "tables/quests.tbl";

    // Rewards ride on the quests table, so the quests layout decides.
    public static QuestRewardTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.FormatFor("tables/quests.tbl", "1.1.4") is { } known
            && (format == known || format.StartsWith(known, StringComparison.Ordinal)))
        {
            return Build114;
        }

        TableShape.Expect(Table, format,
            (Build114.QuestId, "uk"),
            (Build114.Money, "iu"),
            (Build114.FixedItemStart, "u"),
            (Build114.FixedCountStart, "u"),
            (Build114.ChoiceItemStart, "u"),
            (Build114.ChoiceCountStart, "u"),
            (Build114.Xp, "iu"));
        return Build114;
    }

    public static readonly QuestRewardTableColumns Build114 = new(
        QuestId: 0,
        Money: 108,
        FixedItemStart: 87,
        FixedCountStart: 91,
        ChoiceItemStart: 75,
        ChoiceCountStart: 81,
        Xp: 109);
}
