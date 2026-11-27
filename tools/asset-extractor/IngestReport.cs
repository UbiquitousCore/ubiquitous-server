using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

// What the import tells the person who ran it: where it is, and once it
// stops, whether it worked and the one command to type next. The commands
// differ between the Docker stack and a native run from a clone.
public static class IngestReport
{
    public const int StepCount = 6;

    // Microsoft's .NET images set this; a native run does not.
    public static bool InContainer =>
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);

    public static string Step(int number, string what) => $"[{number}/{StepCount}] {what}...";

    public static IReadOnlyList<string> Done(DataManifest? manifest, bool inContainer)
    {
        var lines = new List<string> { "Done! The game data is imported." };
        if (manifest is not null)
        {
            lines.Add($"  Game version: {manifest.GameVersion}, {manifest.Datasets.Count} data sets.");
            if (manifest.ImportNotes is { Count: > 0 } notes)
            {
                lines.Add("  Warning: this version's files leave some things out:");
                lines.AddRange(notes.Select(note => $"    {note}"));
            }
        }

        lines.Add("  Your accounts and characters were not touched.");
        lines.Add("");
        if (inContainer)
        {
            lines.Add("Next: start the server (or restart it, if it is already running):");
            lines.Add("  docker compose up -d");
            lines.Add("  docker compose restart server");
            lines.Add("Then make an account, if you have none yet:");
            lines.Add("  docker compose exec server dotnet server.dll account create <name>");
        }
        else
        {
            lines.Add("Next: start the server:");
            lines.Add("  dotnet run --project src/server");
            lines.Add("Then make an account, if you have none yet:");
            lines.Add("  dotnet run --project src/server -- account create <name>");
        }

        return lines;
    }

    public static IReadOnlyList<string> Failed(string source, bool inContainer)
    {
        var again = inContainer
            ? "docker compose run --rm ingest"
            : $"dotnet run --project tools/asset-extractor -- --ingest {source}";
        return
        [
            "The import did not finish; the message above says why. Nothing in your saves was changed.",
            $"Fix that, then run it again: {again}",
        ];
    }
}
