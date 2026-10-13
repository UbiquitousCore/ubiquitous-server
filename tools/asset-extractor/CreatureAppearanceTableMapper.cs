namespace Ubiquitous.AssetExtractor;

public sealed record CreatureAppearanceTableColumns(int Entry, int NameTextId);

public static class CreatureAppearanceTableMapper
{
    public static IReadOnlyDictionary<uint, uint> MapNameStringIds(
        FTableDocument document, CreatureAppearanceTableColumns columns)
    {
        var nameStringIdsByEntry = new Dictionary<uint, uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint entry = RequireUInt(row, columns.Entry, i, "entry");
            uint nameTextId = RequireUInt(row, columns.NameTextId, i, "name string id");
            nameStringIdsByEntry.TryAdd(entry, nameTextId);
        }

        return nameStringIdsByEntry;
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable {field}.");
        }

        return value;
    }
}
