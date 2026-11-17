using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ubiquitous.Data;

public sealed record DataManifest(
    string ExtractorVersion,
    string GameVersion,
    string SourceHash,
    DateTimeOffset GeneratedAt,
    Dictionary<string, string> Datasets,
    IReadOnlyList<string>? ImportNotes = null,
    // The highest level the client's own achievements ask a player to reach.
    int? LevelCap = null)
{
    // The build whose spellbooks, starting kits and level cap were read in play.
    public const string TargetGameVersion = "1.1.4";

    public static bool IsTargetGameVersion(string? gameVersion) =>
        string.Equals(gameVersion, TargetGameVersion, StringComparison.Ordinal);

    // Whether a package plays by what 1.1.4 was seen to do: any 1.x build, and a
    // package whose build is not known, since a near-1.1.4 copy with one table
    // changed is far likelier than an unmeasured later build.
    public static bool PlaysAsSeen(string? gameVersion) =>
        string.IsNullOrEmpty(gameVersion) || gameVersion == UnknownGameVersion
        || gameVersion.StartsWith("1.", StringComparison.Ordinal);

    // What the importer records for tables that match no captured build.
    public const string UnknownGameVersion = "unknown";

    // The level cap this package plays to: the one 1.1.4 was seen with, unless a
    // later known build's achievements asked for another.
    public int PlayerLevelCap(int seenCap) => PlaysAsSeen(GameVersion) ? seenCap : LevelCap ?? seenCap;

    public const string FileName = "manifest.json";

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
