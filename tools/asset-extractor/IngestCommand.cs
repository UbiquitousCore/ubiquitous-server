using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

// The whole import in one command, so Docker and a native run do exactly the
// same thing: unpack, import, validate, install keeping saves, add looks, bake.
public static class IngestCommand
{
    public static int Run(string source, string dataDirectory, TextWriter log)
    {
        var code = Import(source, dataDirectory, log);
        var generated = Path.Combine(dataDirectory, "generated");
        log.WriteLine();
        foreach (var line in code == 0
                     ? IngestReport.Done(ReadManifest(generated), IngestReport.InContainer)
                     : IngestReport.Failed(source, IngestReport.InContainer))
            log.WriteLine(line);
        return code;
    }

    private static int Import(string source, string dataDirectory, TextWriter log)
    {
        if (IngestSource.Resolve(source) is not { } found)
        {
            log.WriteLine($"nothing at {source} — copy your 1.1.4 .ipa into {source}/ and run again");
            return 2;
        }

        var rawRoot = Path.Combine(dataDirectory, "raw");
        var tablesDirectory = Path.Combine(rawRoot, "tables");
        var worldDirectory = Path.Combine(rawRoot, "world");
        var generated = Path.Combine(dataDirectory, "generated");
        var staging = Path.Combine(dataDirectory, "cache", "import");

        var tables = found.Tables;
        if (tables is null)
        {
            if (!found.FullBuild)
            {
                log.WriteLine($"no game file in {source} — copy your 1.1.4 .ipa into {source}/ and run again");
                return 2;
            }

            log.WriteLine(IngestReport.Step(1, "Reading the game file"));
            log.WriteLine($"pulling tables.bin out of {found.Build}");
            if (BuildPacks.CopyPack(found.Build, "tables.bin", found.DropDirectory, log) != 0) return 1;
            tables = IngestSource.FindFirst(found.DropDirectory, "tables.bin");
            if (tables is null)
            {
                log.WriteLine($"no tables.bin in {found.Build}");
                return 1;
            }
        }

        if (found.FullBuild)
        {
            log.WriteLine(IngestReport.Step(2, "Unpacking maps and collision models"));
            if (Program.ExtractWorld(found.Build, rawRoot) != 0) return 1;
            if (CollisionPacks.Extract(found.Build, rawRoot, log) != 0) return 1;
        }
        else
        {
            log.WriteLine("tables only — skipping world archives and collision models");
        }

        log.WriteLine(IngestReport.Step(3, "Importing the game tables"));
        if (Program.Extract(tables, rawRoot) != 0) return 1;
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        if (Program.ImportFromDir(tablesDirectory, staging, WorldArchives(worldDirectory)) != 0) return 1;
        log.WriteLine(IngestReport.Step(4, "Checking and installing the data"));
        if (Program.Check(staging) != 0) return 1;
        PackageInstall.Install(generated, staging, log);

        log.WriteLine(IngestReport.Step(5, "Adding character looks"));
        if (found.FullBuild && !AddLooks(found.Build, tablesDirectory, generated, log))
            log.WriteLine($"warning: no looks in {found.Build} — bots fall back to plain bodies");

        if (!Directory.Exists(Path.Combine(generated, "terrain")))
        {
            log.WriteLine($"no terrain in {generated} — import a whole client build to bake walkable ground");
            return 2;
        }

        log.WriteLine(IngestReport.Step(6, "Baking walkable ground (the longest step)"));
        try
        {
            NavigationBakeCommand.Run(
                generated,
                ExistingDirectory(tablesDirectory),
                ExistingDirectory(Path.Combine(rawRoot, "model")),
                ExistingDirectory(worldDirectory),
                onlyMap: null,
                log);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FTableFormatException)
        {
            log.WriteLine($"Could not bake walkable ground: {ex.Message}");
            return 1;
        }

        log.WriteLine($"ingested from {found.Build}: package in {generated}, saves kept");
        return 0;
    }

    private static DataManifest? ReadManifest(string generated)
    {
        var path = Path.Combine(generated, DataManifest.FileName);
        try
        {
            return File.Exists(path) ? DataManifestJson.Load(path) : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private static bool AddLooks(string build, string tablesDirectory, string generated, TextWriter log)
    {
        try
        {
            return PlayerLookReader.Extract(build, tablesDirectory, generated, log) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FTableFormatException)
        {
            log.WriteLine(ex.Message);
            return false;
        }
    }

    private static Dictionary<string, byte[]>? WorldArchives(string worldDirectory)
    {
        if (!Directory.Exists(worldDirectory)) return null;
        var archives = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(worldDirectory, "*.bin"))
            archives[Path.GetFileNameWithoutExtension(path)] = File.ReadAllBytes(path);
        return archives.Count == 0 ? null : archives;
    }

    private static string? ExistingDirectory(string path) => Directory.Exists(path) ? path : null;
}
