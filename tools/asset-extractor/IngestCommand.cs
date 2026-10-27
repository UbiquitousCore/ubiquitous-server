namespace Ubiquitous.AssetExtractor;

// The whole import in one command, so Docker and a native run do exactly the
// same thing: unpack, import, validate, install keeping saves, add looks, bake.
public static class IngestCommand
{
    public static int Run(string source, string dataDirectory, TextWriter log)
    {
        if (IngestSource.Resolve(source) is not { } found)
        {
            log.WriteLine($"nothing at {source} — drop the client .ipa into {source}/ and run again");
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
                log.WriteLine($"no tables.bin under {source}");
                return 2;
            }

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
            if (Program.ExtractWorld(found.Build, rawRoot) != 0) return 1;
            if (CollisionPacks.Extract(found.Build, rawRoot, log) != 0) return 1;
        }
        else
        {
            log.WriteLine("tables only — skipping world archives and collision models");
        }

        if (Program.Extract(tables, rawRoot) != 0) return 1;
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        if (Program.ImportFromDir(tablesDirectory, staging, WorldArchives(worldDirectory)) != 0) return 1;
        if (Program.Check(staging) != 0) return 1;
        PackageInstall.Install(generated, staging, log);

        if (found.FullBuild && !AddLooks(found.Build, tablesDirectory, generated, log))
            log.WriteLine($"warning: no looks in {found.Build} — bots fall back to plain bodies");

        if (!Directory.Exists(Path.Combine(generated, "terrain")))
        {
            log.WriteLine($"no terrain in {generated} — import a whole client build to bake walkable ground");
            return 2;
        }

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
