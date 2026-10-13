namespace Ubiquitous.AssetExtractor;

public static class StringTableMapper
{
    public static IReadOnlyDictionary<uint, string> MapStrings(FTableDocument document)
    {
        var strings = new Dictionary<uint, string>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint id = RequireUInt(row, 0, i);
            string text = RequireString(row, 1, i);
            strings.TryAdd(id, text);
        }

        return strings;
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable string id.");
        }

        return value;
    }

    private static string RequireString(IReadOnlyList<object> row, int column, int index)
    {
        if (column < 0 || column >= row.Count || row[column] is not string value)
        {
            throw new FTableFormatException($"Row {index} has no usable string text.");
        }

        return value;
    }
}
