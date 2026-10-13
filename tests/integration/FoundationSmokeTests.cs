using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

public class FoundationSmokeTests
{
    [Fact]
    public void DataPipeline_ManifestContract_EndsInValidatedCatalog()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ubiquitous-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            DataManifestJson.Save(new DataManifest(
                ExtractorVersion: "0.1.0",
                GameVersion: "test",
                SourceHash: "synthetic",
                GeneratedAt: DateTimeOffset.UtcNow,
                Datasets: new Dictionary<string, string>
                {
                    ["maps"] = "maps.json",
                    ["spells"] = "spells.json",
                    ["creatures"] = "creatures.json",
                }), Path.Combine(dir, DataManifest.FileName));
            File.WriteAllText(Path.Combine(dir, "maps.json"), "[]");
            File.WriteAllText(Path.Combine(dir, "spells.json"), "[]");
            File.WriteAllText(Path.Combine(dir, "creatures.json"), "[]");

            var catalog = DataCatalog.LoadFromDirectory(dir);

            Assert.True(catalog.Validate().IsValid);
            Assert.Equal("test", catalog.Manifest.GameVersion);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
