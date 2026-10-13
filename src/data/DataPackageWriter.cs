using System.Text.Json;

namespace Ubiquitous.Data;

public static class DataPackageWriter
{
    public static void WriteSyntheticPackage(string directory)
    {
        Directory.CreateDirectory(directory);

        var manifest = new DataManifest(
            ExtractorVersion: "0.0.0-synthetic",
            GameVersion: "synthetic",
            SourceHash: "synthetic",
            GeneratedAt: DateTimeOffset.UtcNow,
            Datasets: new Dictionary<string, string>
            {
                ["maps"] = "maps.json",
                ["spells"] = "spells.json",
                ["creatures"] = "creatures.json",
            });
        DataManifestJson.Save(manifest, Path.Combine(directory, DataManifest.FileName));

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        File.WriteAllText(
            Path.Combine(directory, "maps.json"),
            JsonSerializer.Serialize(new[] { new MapDefinition(7, "synthetic-starter") }, options));
        File.WriteAllText(
            Path.Combine(directory, "spells.json"),
            JsonSerializer.Serialize(
                new[] { new SpellDefinition(1, "synthetic-bolt", 1000, 2000, 30f) }, options));
        File.WriteAllText(
            Path.Combine(directory, "creatures.json"),
            JsonSerializer.Serialize(
                new[] { new CreatureDefinition(12, "synthetic-wolf", 3, 80) }, options));
    }

    // One dataset written into an existing package and listed in its manifest.
    public static void WriteDataset<T>(string directory, string dataset, IReadOnlyList<T> values)
    {
        var manifestPath = Path.Combine(directory, DataManifest.FileName);
        var manifest = DataManifestJson.Load(manifestPath);
        var file = $"{dataset}.json";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        File.WriteAllText(Path.Combine(directory, file), JsonSerializer.Serialize(values, options));
        manifest.Datasets[dataset] = file;
        DataManifestJson.Save(manifest, manifestPath);
    }

    public static DataValidationResult ValidatePackage(string directory) =>
        DataCatalog.LoadFromDirectory(directory).Validate();
}
