using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record MapGraveyardTableColumns(
    int Id, int X, int Y, int Z, int Yaw, int ZoneId, int MapId, int Faction);

public static class MapGraveyardTableMapper
{
    public static IReadOnlyList<MapGraveyardDefinition> MapGraveyards(
        FTableDocument document,
        MapGraveyardTableColumns columns,
        IReadOnlySet<ushort> mapIds)
    {
        var graveyards = new List<MapGraveyardDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint id = RequireUInt(row, columns.Id, i, "id");
            if (!seen.Add(id))
            {
                throw new FTableFormatException($"Duplicate map graveyard id {id}.");
            }

            float x = RequireFloat(row, columns.X, i, "x");
            float y = RequireFloat(row, columns.Y, i, "y");
            float z = RequireFloat(row, columns.Z, i, "z");
            float yaw = RequireFloat(row, columns.Yaw, i, "yaw");
            ushort zoneId = RequireUInt16(row, columns.ZoneId, i, "zone");
            ushort mapId = RequireUInt16(row, columns.MapId, i, "map");
            uint faction = RequireUInt(row, columns.Faction, i, "faction");

            if (!mapIds.Contains(mapId))
            {
                throw new FTableFormatException($"Map graveyard {id} references missing map {mapId}.");
            }

            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(yaw))
            {
                throw new FTableFormatException($"Map graveyard {id} has non-finite position data.");
            }

            graveyards.Add(new MapGraveyardDefinition(id, mapId, x, y, z, yaw, zoneId, faction));
        }

        return graveyards;
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable map graveyard {field}.");
        }

        return value;
    }

    private static ushort RequireUInt16(IReadOnlyList<object> row, int column, int index, string field)
    {
        uint value = RequireUInt(row, column, index, field);
        if (value > ushort.MaxValue)
        {
            throw new FTableFormatException($"Row {index} has a {field} id outside u16 range.");
        }

        return (ushort)value;
    }

    private static float RequireFloat(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not float value)
        {
            throw new FTableFormatException($"Row {index} has no usable map graveyard {field}.");
        }

        return value;
    }
}
