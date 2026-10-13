using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record MapTableColumns(
    int Id, int InternalName, int File = -1,
    int RecallMapId = -1, int RecallX = -1, int RecallY = -1, int RecallZ = -1,
    int Kind = -1, int MinLevel = -1);

public static class MapTableMapper
{
    public const uint DungeonKind = 4;

    public static IReadOnlyList<MapDefinition> MapMaps(
        FTableDocument document, MapTableColumns columns)
    {
        var maps = new List<MapDefinition>();
        var seen = new HashSet<ushort>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            ushort id = RequireMapId(row, columns.Id, i);
            if (!seen.Add(id))
            {
                throw new FTableFormatException($"Duplicate map id {id}.");
            }

            string name = RequireString(row, columns.InternalName, i, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new FTableFormatException($"Map {id} has a blank name.");
            }

            string? file = columns.File >= 0 && columns.File < row.Count && row[columns.File] is string f
                ? f : null;

            ushort? recallMapId = columns.RecallMapId >= 0 && columns.RecallMapId < row.Count
                && row[columns.RecallMapId] is uint recallMap && recallMap > 0 && recallMap <= ushort.MaxValue
                ? (ushort)recallMap : null;
            float? recallX = columns.RecallX >= 0 && columns.RecallX < row.Count && row[columns.RecallX] is float px
                ? px : null;
            float? recallY = columns.RecallY >= 0 && columns.RecallY < row.Count && row[columns.RecallY] is float py
                ? py : null;
            float? recallZ = columns.RecallZ >= 0 && columns.RecallZ < row.Count && row[columns.RecallZ] is float pz
                ? pz : null;

            int minLevel = columns.MinLevel >= 0 && columns.MinLevel < row.Count && row[columns.MinLevel] is uint level
                ? (int)level : 0;
            bool isDungeon = columns.Kind >= 0 && columns.Kind < row.Count && row[columns.Kind] is uint kind
                && kind == DungeonKind;

            maps.Add(new MapDefinition(id, name, file, RecallMapId: recallMapId,
                RecallX: recallX, RecallY: recallY, RecallZ: recallZ, MinLevel: minLevel, IsDungeon: isDungeon));
        }

        return maps;
    }

    private static ushort RequireMapId(IReadOnlyList<object> row, int column, int index)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable map id.");
        }

        if (value > ushort.MaxValue)
        {
            throw new FTableFormatException($"Row {index} has a map id outside u16 range.");
        }

        return (ushort)value;
    }

    private static string RequireString(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not string value)
        {
            throw new FTableFormatException($"Row {index} has no usable map {field}.");
        }

        return value;
    }
}
