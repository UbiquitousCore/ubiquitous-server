using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ubiquitous.AssetExtractor;

// Swaps a freshly imported package in for the one the server reads, without
// losing what players made: saves live in the same directory as the data.
public static class PackageInstall
{
    private static readonly string[] RuntimeState =
    [
        "accounts.json", "characters.json", "guilds", "inventory", "mail", "quests", "auctions", "friends",
        "playerLooks.json", ".gitkeep",
    ];

    // Baked separately from the import, so a re-import that did not produce them keeps the old ones.
    private static readonly string[] KeptUnlessRebuilt = ["terrain", "navigation"];

    public static void Install(string generated, string staging, TextWriter log)
    {
        if (Directory.Exists(generated)) KeepRuntimeState(generated, staging);

        var previous = generated + ".previous";
        if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
        if (Directory.Exists(generated)) Directory.Move(generated, previous);
        Directory.Move(staging, generated);
        log.WriteLine($"installed; the package it replaced is in {previous}");
    }

    public static void KeepRuntimeState(string generated, string staging)
    {
        foreach (var name in RuntimeState) Copy(Path.Combine(generated, name), Path.Combine(staging, name));

        foreach (var name in KeptUnlessRebuilt)
        {
            var target = Path.Combine(staging, name);
            if (!Directory.Exists(target)) Copy(Path.Combine(generated, name), target);
        }

        KeepManifestEntries(Path.Combine(generated, "manifest.json"), Path.Combine(staging, "manifest.json"));
    }

    // A dataset kept from the old package needs its manifest entry too, or the server reads past the file.
    private static void KeepManifestEntries(string oldManifest, string newManifest)
    {
        if (!File.Exists(oldManifest)) return;
        if (JsonNode.Parse(File.ReadAllText(oldManifest))?["Datasets"] is not JsonObject kept) return;

        var manifest = JsonNode.Parse(File.ReadAllText(newManifest))!.AsObject();
        if (manifest["Datasets"] is not JsonObject listed)
        {
            listed = [];
            manifest["Datasets"] = listed;
        }

        var directory = Path.GetDirectoryName(newManifest)!;
        foreach (var (name, file) in kept)
        {
            if (listed.ContainsKey(name) || file?.GetValue<string>() is not { } path) continue;
            if (File.Exists(Path.Combine(directory, path)) || Directory.Exists(Path.Combine(directory, path)))
                listed[name] = path;
        }

        File.WriteAllText(newManifest, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static void Copy(string source, string target)
    {
        if (File.Exists(source))
        {
            File.Copy(source, target, overwrite: true);
            return;
        }

        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            Copy(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}
