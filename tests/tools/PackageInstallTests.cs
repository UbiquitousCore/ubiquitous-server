using System.Text.Json.Nodes;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public sealed class PackageInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ubiquitous-package-install-" + Guid.NewGuid().ToString("N"));
    private string Generated => Path.Combine(_root, "generated");
    private string Staging => Path.Combine(_root, "staging");

    public PackageInstallTests()
    {
        Directory.CreateDirectory(Generated);
        Directory.CreateDirectory(Staging);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void Install_KeepsSavesAndMovesTheOldPackageAside()
    {
        Write(Path.Combine(Generated, "characters.json"), "[\"alice\"]");
        Write(Path.Combine(Generated, "mail", "1.json"), "{}");
        Write(Path.Combine(Generated, "items.json"), "old");
        Write(Path.Combine(Generated, "manifest.json"), "{\"Datasets\":{}}");
        Write(Path.Combine(Staging, "items.json"), "new");
        Write(Path.Combine(Staging, "manifest.json"), "{\"Datasets\":{}}");

        PackageInstall.Install(Generated, Staging, TextWriter.Null);

        Assert.Equal("[\"alice\"]", File.ReadAllText(Path.Combine(Generated, "characters.json")));
        Assert.True(File.Exists(Path.Combine(Generated, "mail", "1.json")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Generated, "items.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Generated + ".previous", "items.json")));
        Assert.False(Directory.Exists(Staging));
    }

    [Fact]
    public void Install_IntoAnEmptyDataFolder_JustMovesTheImportIn()
    {
        Directory.Delete(Generated);
        Write(Path.Combine(Staging, "manifest.json"), "{}");

        PackageInstall.Install(Generated, Staging, TextWriter.Null);

        Assert.True(File.Exists(Path.Combine(Generated, "manifest.json")));
    }

    [Fact]
    public void KeptDataset_KeepsItsManifestEntry_AndAMissingOneDoesNot()
    {
        Write(Path.Combine(Generated, "playerLooks.json"), "{}");
        Write(Path.Combine(Generated, "manifest.json"),
            "{\"Datasets\":{\"PlayerLooks\":\"playerLooks.json\",\"Gone\":\"gone.json\",\"Items\":\"old-items.json\"}}");
        Write(Path.Combine(Staging, "manifest.json"), "{\"GameVersion\":\"1.1.4\",\"Datasets\":{\"Items\":\"items.json\"}}");

        PackageInstall.KeepRuntimeState(Generated, Staging);

        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(Staging, "manifest.json")))!;
        var datasets = manifest["Datasets"]!.AsObject();
        Assert.Equal("playerLooks.json", datasets["PlayerLooks"]!.GetValue<string>());
        Assert.Equal("items.json", datasets["Items"]!.GetValue<string>());
        Assert.False(datasets.ContainsKey("Gone"));
        Assert.Equal("1.1.4", manifest["GameVersion"]!.GetValue<string>());
    }

    [Fact]
    public void TerrainTheImportDidNotRebuild_IsKept_ButARebuiltOneWins()
    {
        Write(Path.Combine(Generated, "terrain", "1.bin"), "old terrain");
        Write(Path.Combine(Generated, "navigation", "1.bin"), "old ground");
        Write(Path.Combine(Staging, "navigation", "1.bin"), "new ground");
        Write(Path.Combine(Staging, "manifest.json"), "{}");

        PackageInstall.KeepRuntimeState(Generated, Staging);

        Assert.Equal("old terrain", File.ReadAllText(Path.Combine(Staging, "terrain", "1.bin")));
        Assert.Equal("new ground", File.ReadAllText(Path.Combine(Staging, "navigation", "1.bin")));
    }
}
