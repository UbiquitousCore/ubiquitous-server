namespace Ubiquitous.AssetExtractor;

public sealed record CreatureAppearanceTableColumns(int Entry, int NameTextId);

public static class CreatureAppearanceTableMapper
{
    private const string Table = "tables/creature_appearance.tbl";

    public static CreatureAppearanceTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "1.1.4")) return new(Entry: 0, NameTextId: 1);
        TableShape.Expect(Table, format, (0, "uk"), (1, "uk"));
        return new(Entry: 0, NameTextId: 1);
    }

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
