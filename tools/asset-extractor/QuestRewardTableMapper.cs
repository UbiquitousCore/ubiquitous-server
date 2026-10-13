using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record QuestRewardTableColumns(
    int QuestId, int Money, int FixedItemStart, int FixedCountStart, int ChoiceItemStart, int ChoiceCountStart,
    int Xp);

public static class QuestRewardTableMapper
{
    private const int FixedSlots = 4;
    private const int ChoiceSlots = 6;

    public static IReadOnlyList<QuestRewardDefinition> MapRewards(
        FTableDocument document,
        QuestRewardTableColumns columns,
        IReadOnlySet<uint> questIds,
        Func<uint, (uint NameTextId, int Quality)?> resolveItem)
    {
        var rewards = new List<QuestRewardDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint questId = RequireUInt(row, columns.QuestId, i, "quest id");
            if (!seen.Add(questId))
            {
                throw new FTableFormatException($"Duplicate quest reward for quest {questId}.");
            }

            if (!questIds.Contains(questId))
            {
                throw new FTableFormatException($"Quest reward references missing quest {questId}.");
            }

            int money = RequireInt(row, columns.Money, i, "money");
            int xp = RequireInt(row, columns.Xp, i, "xp");
            var fixedItems = ReadSlots(row, columns.FixedItemStart, columns.FixedCountStart, FixedSlots, resolveItem);
            var choiceItems = ReadSlots(row, columns.ChoiceItemStart, columns.ChoiceCountStart, ChoiceSlots, resolveItem);

            rewards.Add(new QuestRewardDefinition(questId, money, fixedItems, choiceItems, xp));
        }

        return rewards;
    }

    private static IReadOnlyList<QuestRewardItem> ReadSlots(
        IReadOnlyList<object> row, int itemStart, int countStart, int slots,
        Func<uint, (uint NameTextId, int Quality)?> resolveItem)
    {
        var items = new List<QuestRewardItem>();
        for (int slot = 0; slot < slots; slot++)
        {
            if (row[itemStart + slot] is not uint entry || entry == 0) continue;
            if (row[countStart + slot] is not uint count || count == 0) continue;
            var meta = resolveItem(entry);
            if (meta is null) continue;

            items.Add(new QuestRewardItem(entry, (int)count, meta.Value.NameTextId, meta.Value.Quality));
        }

        return items;
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable quest reward {field}.");
        }

        return value;
    }

    private static int RequireInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not int and not uint)
        {
            throw new FTableFormatException($"Row {index} has no usable quest reward {field}.");
        }

        return row[column] is uint u ? unchecked((int)u) : (int)row[column];
    }
}
