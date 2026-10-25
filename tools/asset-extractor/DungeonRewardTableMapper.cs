using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record DungeonRewardTableColumns(
    int Id, int MapId, int Difficulty, int ItemId, int AmountA, int AmountB);

public static class DungeonRewardTableMapper
{
    public static IReadOnlyList<DungeonRewardDefinition> MapRewards(
        FTableDocument document,
        DungeonRewardTableColumns columns,
        IReadOnlySet<ushort> mapIds,
        IReadOnlySet<uint> itemIds)
    {
        var rewards = new List<DungeonRewardDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint id = RequireUInt(row, columns.Id, i, "id");
            if (!seen.Add(id))
            {
                throw new FTableFormatException($"Duplicate dungeon reward id {id}.");
            }

            ushort mapId = RequireMapId(row, columns.MapId, i);
            if (!mapIds.Contains(mapId))
            {
                throw new FTableFormatException($"Dungeon reward {id} references missing map {mapId}.");
            }

            uint itemId = RequireUInt(row, columns.ItemId, i, "item");
            if (!itemIds.Contains(itemId))
            {
                throw new FTableFormatException($"Dungeon reward {id} references missing item {itemId}.");
            }

            rewards.Add(new DungeonRewardDefinition(
                id,
                mapId,
                RequireUInt(row, columns.Difficulty, i, "difficulty"),
                itemId,
                RequireUInt(row, columns.AmountA, i, "first amount"),
                RequireUInt(row, columns.AmountB, i, "second amount")));
        }

        return rewards;
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable dungeon reward {field}.");
        }

        return value;
    }

    private static ushort RequireMapId(IReadOnlyList<object> row, int column, int index)
    {
        uint value = RequireUInt(row, column, index, "map");
        if (value > ushort.MaxValue)
        {
            throw new FTableFormatException($"Row {index} has a map id outside u16 range.");
        }

        return (ushort)value;
    }
}
