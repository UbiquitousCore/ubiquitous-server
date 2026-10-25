using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ubiquitous.AssetExtractor;

// One row of a build fingerprint: the shape of a single table, not its
// contents. Format strings and row counts are schema metadata — the same
// kind of string the mapper tests already pin — so golden files may be
// committed. Never put cell data here.
public sealed record TableFingerprintEntry(
    string Name,
    int Rows,
    int Cols,
    string Format,
    string? Error = null);

public sealed record TableFingerprint(
    string SourceHash,
    IReadOnlyList<TableFingerprintEntry> Tables,
    DateTimeOffset CapturedAt)
{
    public const string FileName = "fingerprint.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Capture the shape of every tables/*.tbl entry. Unreadable tables are
    // recorded with an error, not skipped, so a golden compare notices them.
    public static TableFingerprint Capture(
        IReadOnlyDictionary<string, byte[]> entries, string sourceHash)
    {
        var tables = entries.Keys
            .Where(name => name.StartsWith("tables/", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".tbl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                try
                {
                    var header = TableLayoutReader.Read(entries[name]);
                    return new TableFingerprintEntry(name, header.Rows, header.Cols, header.ColumnTypes);
                }
                catch (FTableFormatException ex)
                {
                    return new TableFingerprintEntry(name, -1, -1, string.Empty, ex.Message);
                }
            })
            .ToList();
        return new TableFingerprint(sourceHash, tables, DateTimeOffset.UtcNow);
    }

    // Stable hash for an unpacked directory of plain .tbl files: sorted
    // "name + bytes", so the same tables always hash the same.
    public static string HashDirectory(IReadOnlyDictionary<string, byte[]> entries)
    {
        var names = entries.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
        using var sha = SHA256.Create();
        foreach (var name in names)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            var nameLen = BitConverter.GetBytes(nameBytes.Length);
            sha.TransformBlock(nameLen, 0, nameLen.Length, null, 0);
            sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);
            var bytes = entries[name];
            var dataLen = BitConverter.GetBytes(bytes.Length);
            sha.TransformBlock(dataLen, 0, dataLen.Length, null, 0);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash ?? []);
    }

    public void Save(string path)
    {
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, this, Options);
    }

    public static TableFingerprint Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<TableFingerprint>(stream, Options)
            ?? throw new InvalidDataException($"Fingerprint at '{path}' deserialized to null.");
    }

    // Compare a fresh capture against a golden file. Formats must match
    // exactly; row counts are informational (same build, same content) and
    // reported but never fail the compare.
    public static IReadOnlyList<string> Compare(TableFingerprint expected, TableFingerprint actual)
    {
        var problems = new List<string>();
        var actualByName = actual.Tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
        foreach (var want in expected.Tables)
        {
            if (!actualByName.TryGetValue(want.Name, out var got))
            {
                // An older build simply ships fewer languages (Portuguese and
                // Russian arrived in 1.1.2): a missing non-English string
                // table is content drift, not shape drift.
                bool stringVariant = want.Name.StartsWith("tables/String_", StringComparison.Ordinal)
                    && !want.Name.EndsWith("_en.tbl", StringComparison.Ordinal);
                problems.Add($"{(stringVariant ? "note " : "")}missing table '{want.Name}' " +
                    $"(golden: {want.Rows} rows, format \"{want.Format}\").");
                continue;
            }

            if (want.Error is not null || got.Error is not null)
            {
                if (want.Error != got.Error)
                    problems.Add($"'{want.Name}': readability changed (golden error: {want.Error ?? "none"}, actual: {got.Error ?? "none"}).");
                continue;
            }

            if (!string.Equals(want.Format, got.Format, StringComparison.Ordinal))
            {
                problems.Add($"'{want.Name}': format changed (golden \"{want.Format}\", actual \"{got.Format}\").");
            }
            else if (want.Rows != got.Rows)
            {
                problems.Add($"note '{want.Name}': {got.Rows} rows, golden had {want.Rows} (same shape, content drift).");
            }
        }

        foreach (var got in actual.Tables)
        {
            // A newer build's extra tables (languages, stickers, dungeon
            // rewards) are informational: the import only reads the tables
            // it knows, and missing ones are reported above when they
            // matter. A rename shows up as a missing table, which fails.
            if (expected.Tables.All(t => t.Name != got.Name))
                problems.Add($"note new table '{got.Name}' ({got.Rows} rows, format \"{got.Format}\") — not imported.");
        }

        return problems;
    }

    public static bool IsNote(string problem) => problem.StartsWith("note ", StringComparison.Ordinal);
}
