using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ubiquitous.Data;

public sealed record DataManifest(
    string ExtractorVersion,
    string GameVersion,
    string SourceHash,
    DateTimeOffset GeneratedAt,
    Dictionary<string, string> Datasets)
{
    public const string FileName = "manifest.json";

    public const string TargetGameVersion = "1.1.4";

    public static bool IsTargetGameVersion(string? gameVersion) =>
        string.Equals(gameVersion, TargetGameVersion, StringComparison.Ordinal);

    public static readonly IReadOnlyList<string> RequiredDatasets = new[]
    {
        "maps",
        "spells",
        "creatures",
    };
}

public static class DataManifestJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static DataManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<DataManifest>(stream, Options)
            ?? throw new InvalidDataException($"Manifest at '{path}' deserialized to null.");
    }

    public static void Save(DataManifest manifest, string path)
    {
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, manifest, Options);
    }
}
