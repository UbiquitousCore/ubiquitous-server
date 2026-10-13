using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record TeleportStopTableColumns(
    int Id, int MapId, int X, int Y, int Z, int Yaw, int Type);

public static class TeleportStopTableMapper
{
    public static IReadOnlyList<TeleportStopDefinition> MapTeleportStops(
        FTableDocument document,
        TeleportStopTableColumns columns,
        IReadOnlySet<ushort> mapIds)
    {
        var stops = new List<TeleportStopDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint id = RequireUInt(row, columns.Id, i, "id");
            if (!seen.Add(id))
            {
                throw new FTableFormatException($"Duplicate teleport stop id {id}.");
            }

            ushort mapId = RequireMapId(row, columns.MapId, i);
            float x = RequireFloat(row, columns.X, i, "x");
            float y = RequireFloat(row, columns.Y, i, "y");
            float z = RequireFloat(row, columns.Z, i, "z");
            float yaw = RequireFloat(row, columns.Yaw, i, "yaw");
            uint type = RequireUInt(row, columns.Type, i, "type");

            if (!mapIds.Contains(mapId))
            {
                throw new FTableFormatException($"Teleport stop {id} references missing map {mapId}.");
            }

            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(yaw))
            {
                throw new FTableFormatException($"Teleport stop {id} has non-finite position data.");
            }

            if (!Enum.IsDefined(typeof(TeleportStopKind), (int)type))
            {
                throw new FTableFormatException($"Teleport stop {id} has an unrecognized type {type}.");
            }

            stops.Add(new TeleportStopDefinition(id, mapId, x, y, z, yaw, (TeleportStopKind)type));
        }

        return stops;
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable teleport stop {field}.");
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
            throw new FTableFormatException($"Row {index} has no usable teleport stop {field}.");
        }

        return value;
    }
}
