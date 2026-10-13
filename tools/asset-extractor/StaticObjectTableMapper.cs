using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record StaticObjectTableColumns(
    int Id, int MapId, int EntryId, int X, int Y, int Z, int Yaw,
    int State = -1, int Flags = -1);

public static class StaticObjectTableMapper
{
    public static IReadOnlyList<StaticObjectSpawnDefinition> MapStaticObjects(
        FTableDocument document,
        StaticObjectTableColumns columns,
        IReadOnlySet<ushort> mapIds)
    {
        var spawns = new List<StaticObjectSpawnDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint id = RequireUInt(row, columns.Id, i, "id");
            if (!seen.Add(id))
            {
                throw new FTableFormatException($"Duplicate static-object id {id}.");
            }

            ushort mapId = RequireMapId(row, columns.MapId, i);
            uint entryId = RequireUInt(row, columns.EntryId, i, "entry");
            float x = RequireFloat(row, columns.X, i, "x");
            float y = RequireFloat(row, columns.Y, i, "y");
            float z = RequireFloat(row, columns.Z, i, "z");
            float yaw = RequireFloat(row, columns.Yaw, i, "yaw");

            if (!mapIds.Contains(mapId))
            {
                throw new FTableFormatException($"Static object {id} references missing map {mapId}.");
            }

            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(yaw))
            {
                throw new FTableFormatException($"Static object {id} has non-finite position data.");
            }

            uint state = columns.State >= 0 ? OptionalUInt(row, columns.State) : 0;
            uint flags = columns.Flags >= 0 ? OptionalUInt(row, columns.Flags) : 0;

            spawns.Add(new StaticObjectSpawnDefinition(id, mapId, entryId, x, y, z, yaw, state, flags));
        }

        return spawns;
    }

    private static uint OptionalUInt(IReadOnlyList<object> row, int column) =>
        column >= 0 && column < row.Count && row[column] is uint value ? value : 0;

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable static-object {field}.");
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

    private static float RequireFloat(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not float value)
        {
            throw new FTableFormatException($"Row {index} has no usable static-object {field}.");
        }

        return value;
    }
}
