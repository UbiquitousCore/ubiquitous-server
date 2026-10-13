using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record ZoneTableColumns(
    int Id, int MapId, int ParentZoneId, int Name, int MinX, int MinZ, int MaxX, int MaxZ);

public static class ZoneTableMapper
{
    public static IReadOnlyList<ZoneDefinition> MapZones(
        FTableDocument document,
        ZoneTableColumns columns,
        IReadOnlySet<ushort> mapIds,
        Func<uint, string>? resolveName = null)
    {
        if (columns.Name < 0 && resolveName is null)
        {
            throw new FTableFormatException(
                "This table has no name column; a resolveName delegate is required.");
        }

        var zones = new List<ZoneDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint id = RequireId(row, columns.Id, i, "id");
            if (!seen.Add(id))
            {
                throw new FTableFormatException($"Duplicate zone id {id}.");
            }

            ushort mapId = RequireMapId(row, columns.MapId, i);
            int rawParentZoneId = RequireSignedOrUnsignedInt(row, columns.ParentZoneId, i, "parent zone");
            uint parentZoneId = rawParentZoneId < 0 ? 0u : (uint)rawParentZoneId;
            string name = columns.Name >= 0 ? RequireString(row, columns.Name, i, "name") : resolveName!(id);
            float minX = RequireFloat(row, columns.MinX, i, "minX");
            float minZ = RequireFloat(row, columns.MinZ, i, "minZ");
            float maxX = RequireFloat(row, columns.MaxX, i, "maxX");
            float maxZ = RequireFloat(row, columns.MaxZ, i, "maxZ");

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new FTableFormatException($"Zone {id} has a blank name.");
            }

            if (!mapIds.Contains(mapId))
            {
                throw new FTableFormatException($"Zone {id} references missing map {mapId}.");
            }

            if (minX > maxX || minZ > maxZ)
            {
                throw new FTableFormatException($"Zone {id} has inverted bounds.");
            }

            zones.Add(new ZoneDefinition(id, mapId, parentZoneId, name, minX, minZ, maxX, maxZ));
        }

        return zones;
    }

    private static uint RequireId(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count) throwInvalid();
        return row[column] switch
        {
            uint u => u,
            int i when i >= 0 => (uint)i,
            _ => throwInvalid(),
        };

        uint throwInvalid() => throw new FTableFormatException($"Row {index} has no usable zone {field}.");
    }

    private static int RequireSignedOrUnsignedInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not int and not uint)
        {
            throw new FTableFormatException($"Row {index} has no usable zone {field}.");
        }

        return row[column] is uint u ? unchecked((int)u) : (int)row[column];
    }

    private static ushort RequireMapId(IReadOnlyList<object> row, int column, int index)
    {
        uint value = RequireId(row, column, index, "map");
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
            throw new FTableFormatException($"Row {index} has no usable zone {field}.");
        }

        return value;
    }

    private static string RequireString(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not string value)
        {
            throw new FTableFormatException($"Row {index} has no usable zone {field}.");
        }

        return value;
    }
}
