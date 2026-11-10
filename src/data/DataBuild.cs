using System.Text.Json;

namespace Ubiquitous.Data;

public sealed record TableShape(string Name, int Rows, string Format);

// Which client build a package was imported from, and the shape of each of
// that build's tables, so a player's own files can be told from another build's.
public sealed record DataBuild(string GameVersion, IReadOnlyDictionary<string, TableShape> Tables)
{
    public const string FileName = "fingerprint.json";

    public static DataBuild Unknown { get; } = new(string.Empty, new Dictionary<string, TableShape>());

    // The tables among theirs that this build holds differently or not at
    // all. Nothing is known to differ where this build's shapes were not kept.
    public IReadOnlyList<string> Differences(IEnumerable<TableShape> theirs)
    {
        if (Tables.Count == 0) return [];
        return [.. theirs
            .Where(table => !Tables.TryGetValue(table.Name, out var ours)
                || ours.Rows != table.Rows || ours.Format != table.Format)
            .Select(table => table.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    public static DataBuild Load(string gameVersion, string directory)
    {
        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return new DataBuild(gameVersion, Unknown.Tables);

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var tables = new Dictionary<string, TableShape>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.GetProperty("tables").EnumerateArray())
        {
            if (!entry.TryGetProperty("format", out var format) || format.GetString() is not { Length: > 0 } letters)
            {
                continue;
            }

            string name = ShortName(entry.GetProperty("name").GetString() ?? string.Empty);
            tables[name] = new TableShape(name, entry.GetProperty("rows").GetInt32(), letters);
        }

        return new DataBuild(gameVersion, tables);
    }

    // "tables/items.tbl" and "items" name the same table.
    public static string ShortName(string name) => Path.GetFileNameWithoutExtension(name);
}
