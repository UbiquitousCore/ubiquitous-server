using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record StaticObjectProtoTableColumns(
    int Entry,
    int Type,
    int Festival = -1);

public static class StaticObjectProtoTableMapper
{
    public static IReadOnlyList<StaticObjectTypeDefinition> MapTypes(
        FTableDocument document, StaticObjectProtoTableColumns columns)
    {
        var types = new List<StaticObjectTypeDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint entry = RequireUInt(row, columns.Entry, i, "entry");
            if (!seen.Add(entry))
            {
                throw new FTableFormatException($"Duplicate static-object entry {entry}.");
            }

            uint type = RequireUInt(row, columns.Type, i, "type");
            uint festival = columns.Festival >= 0 ? OptionalUInt(row, columns.Festival) : 0;
            types.Add(new StaticObjectTypeDefinition(entry, type, festival));
        }

        return types;
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
}
