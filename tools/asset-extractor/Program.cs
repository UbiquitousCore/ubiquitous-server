namespace Ubiquitous.AssetExtractor;

public static class Program
{
    public static int Main(string[] args)
    {
        string? GetValue(string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        IReadOnlyList<string> GetValues(string name)
        {
            var values = new List<string>();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    values.Add(args[i + 1]);
                }
            }

            return values;
        }

        IReadOnlyDictionary<string, byte[]>? LoadWorldArchives()
        {
            var paths = GetValues("--world-archive");
            if (paths.Count == 0) return null;
            var archives = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                archives[Path.GetFileNameWithoutExtension(path)] = File.ReadAllBytes(path);
            }

            return archives;
        }

        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  asset-extractor --ingest <build-or-dir> [--data <dir>]");
            Console.WriteLine("                                              The whole import from a dropped-in client");
            Console.WriteLine("                                              build (or tables.bin): unpack, import,");
            Console.WriteLine("                                              validate, install into <dir>/generated");
            Console.WriteLine("                                              keeping saves, add looks, bake walkable");
            Console.WriteLine("                                              ground. <dir> defaults to data.");
            Console.WriteLine("  asset-extractor --synthetic --out <dir>   Write a minimal synthetic package.");
            Console.WriteLine("  asset-extractor --check <dir>              Validate an existing package.");
            Console.WriteLine("  asset-extractor --inspect <archive>        List the tables a packed archive carries.");
            Console.WriteLine("  asset-extractor --extract <archive> --out <dir>");
            Console.WriteLine("                                              Unpack every .tbl entry to <dir>, once,");
            Console.WriteLine("                                              so later work reads plain files instead");
            Console.WriteLine("                                              of re-unpacking the archive each time.");
            Console.WriteLine("  asset-extractor --import <archive> --out <dir>");
            Console.WriteLine("                                              Write a data package (maps, creatures,");
            Console.WriteLine("                                              creature spawns, spells, items, zones, quests,");
            Console.WriteLine("                                              quest rewards, service NPCs, teleport");
            Console.WriteLine("                                              stops, travel routes, static object spawns)");
            Console.WriteLine("                                              using the pinned build-1.1.4 column layouts.");
            Console.WriteLine("  asset-extractor --import-from-dir <dir> --out <dir>");
            Console.WriteLine("                                              Same as --import, but reads plain .tbl files");
            Console.WriteLine("                                              already unpacked by --extract instead of a");
            Console.WriteLine("                                              packed archive — for re-running an import");
            Console.WriteLine("                                              without re-unpacking the archive every time.");
            Console.WriteLine("  asset-extractor --extract-world <archive-or-dir> --out <dir>");
            Console.WriteLine("                                              Put the per-map world archives (city.bin,");
            Console.WriteLine("                                              land1.bin, ...) under <dir>/world, where");
            Console.WriteLine("                                              the bake reads what each map is built");
            Console.WriteLine("                                              from. A separate step from --extract");
            Console.WriteLine("                                              because the archives are large and an");
            Console.WriteLine("                                              operator who only wants tables need not");
            Console.WriteLine("                                              copy them.");
            Console.WriteLine("  asset-extractor --extract-collision <build> --out <dir>");
            Console.WriteLine("                                              Write every collision model (.phy) the");
            Console.WriteLine("                                              client build's packs hold under <dir>, at");
            Console.WriteLine("                                              its path in the pack, searching");
            Console.WriteLine("                                              model_phy.bin, tables.bin, then res.bin as");
            Console.WriteLine("                                              the client does. <build> is the .ipa, the");
            Console.WriteLine("                                              unpacked app, or a folder of the packs.");
            Console.WriteLine("  asset-extractor --extract-pack <build> --pack <name> --out <dir>");
            Console.WriteLine("                                              Copy one pack (usually tables.bin) out");
            Console.WriteLine("                                              of a client build, so later steps read");
            Console.WriteLine("                                              a plain file.");
            Console.WriteLine("  asset-extractor --extract-looks <build> --tables-dir <dir> --package <dir>");
            Console.WriteLine("                                              Count each playable body's skins, faces,");
            Console.WriteLine("                                              hair styles and hair colours in the client");
            Console.WriteLine("                                              build and write them into the package.");
            Console.WriteLine("  asset-extractor --bake <package-dir> [--tables-dir <dir>] [--models <dir>] [--world-dir <dir>] [--map <id>]");
            Console.WriteLine("                                              Bake walkable ground for every map the");
            Console.WriteLine("                                              package has terrain for, writing one file");
            Console.WriteLine("                                              per map into <package-dir>/navigation and");
            Console.WriteLine("                                              printing a per-map summary. With");
            Console.WriteLine("                                              --world-dir the bake also stands on what");
            Console.WriteLine("                                              each map is built from — its buildings.");
            Console.WriteLine("                                              Hand-written corrections beside it are");
            Console.WriteLine("                                              never touched.");
            Console.WriteLine("  asset-extractor --nav-probe <package-dir> --map <id> --at <x,y,z> [--radius <units>]");
            Console.WriteLine("                                              [--tables-dir <dir>] [--models <dir>] [--world-dir <dir>]");
            Console.WriteLine("                                              Explain the bake at one place: what the");
            Console.WriteLine("                                              finished ground holds there, how much of");
            Console.WriteLine("                                              the ground around it can be walked to, and");
            Console.WriteLine("                                              — by running that one tile again stage by");
            Console.WriteLine("                                              stage — which stage the floor disappeared");
            Console.WriteLine("                                              at. Give it the same --models and");
            Console.WriteLine("                                              --world-dir the bake was run with, or it");
            Console.WriteLine("                                              is explaining different collision.");
            Console.WriteLine("  --world-archive <path> (repeatable, with --import or --import-from-dir)");
            Console.WriteLine("                                              A per-map world archive (e.g. city.bin)");
            Console.WriteLine("                                              to compute that map's playable bounds");
            Console.WriteLine("                                              from, matched to a map by its own");
            Console.WriteLine("                                              File column stem. Optional — a map with no");
            Console.WriteLine("                                              matching archive imports with no bounds.");
            Console.WriteLine();
            Console.WriteLine("Every DataCatalog dataset has a pinned column layout for build 1.1.4 (see");
            Console.WriteLine("CreatureProtoColumns and siblings).");
            return 0;
        }


        var ingestSource = GetValue("--ingest");
        if (ingestSource is not null)
        {
            return IngestCommand.Run(ingestSource, GetValue("--data") ?? "data", Console.Out);
        }

        var inspectPath = GetValue("--inspect");
        if (inspectPath is not null)
        {
            return Inspect(inspectPath);
        }

        var extractPath = GetValue("--extract");
        if (extractPath is not null)
        {
            var extractOut = GetValue("--out");
            if (extractOut is null)
            {
                Console.WriteLine("--extract requires --out <dir>.");
                return 2;
            }

            return Extract(extractPath, extractOut);
        }

        var extractWorldPath = GetValue("--extract-world");
        if (extractWorldPath is not null)
        {
            var worldOut = GetValue("--out");
            if (worldOut is null)
            {
                Console.WriteLine("--extract-world requires --out <dir>.");
                return 2;
            }

            return ExtractWorld(extractWorldPath, worldOut);
        }

        var extractCollisionPath = GetValue("--extract-collision");
        if (extractCollisionPath is not null)
        {
            var collisionOut = GetValue("--out");
            if (collisionOut is null)
            {
                Console.WriteLine("--extract-collision requires --out <dir>.");
                return 2;
            }

            return CollisionPacks.Extract(extractCollisionPath, collisionOut, Console.Out);
        }

        var extractPackBuild = GetValue("--extract-pack");
        if (extractPackBuild is not null)
        {
            var packName = GetValue("--pack");
            var packOut = GetValue("--out");
            if (packName is null || packOut is null)
            {
                Console.WriteLine("--extract-pack requires --pack <name> and --out <dir>.");
                return 2;
            }

            return BuildPacks.CopyPack(extractPackBuild, packName, packOut, Console.Out);
        }

        var extractLooksPath = GetValue("--extract-looks");
        if (extractLooksPath is not null)
        {
            var package = GetValue("--package");
            var tablesDir = GetValue("--tables-dir");
            if (package is null || tablesDir is null)
            {
                Console.WriteLine("--extract-looks requires --tables-dir <dir> and --package <dir>.");
                return 2;
            }

            return PlayerLookReader.Extract(extractLooksPath, tablesDir, package, Console.Out);
        }

        var importFromDir = GetValue("--import-from-dir");
        if (importFromDir is not null)
        {
            var importOut = GetValue("--out");
            if (importOut is null)
            {
                Console.WriteLine("--import-from-dir requires --out <dir>.");
                return 2;
            }

            return ImportFromDir(importFromDir, importOut, LoadWorldArchives());
        }

        var importPath = GetValue("--import");
        if (importPath is not null)
        {
            var importOut = GetValue("--out");
            if (importOut is null)
            {
                Console.WriteLine("--import requires --out <dir>.");
                return 2;
            }

            return Import(importPath, importOut, LoadWorldArchives());
        }

        var probeDir = GetValue("--nav-probe");
        if (probeDir is not null)
        {
            var mapValue = GetValue("--map");
            if (mapValue is null
                || !ushort.TryParse(mapValue, System.Globalization.CultureInfo.InvariantCulture, out ushort probeMap))
            {
                Console.WriteLine("--nav-probe needs --map <id>.");
                return 2;
            }

            var atValue = GetValue("--at");
            if (atValue is null || !TryReadPoint(atValue, out var at))
            {
                Console.WriteLine("--nav-probe needs --at x,y,z — the place on the map to explain.");
                return 2;
            }

            float radius = 40f;
            var radiusValue = GetValue("--radius");
            if (radiusValue is not null
                && !float.TryParse(radiusValue, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out radius))
            {
                Console.WriteLine($"--radius takes a distance in world units, not '{radiusValue}'.");
                return 2;
            }

            try
            {
                NavigationProbeCommand.Run(
                    probeDir,
                    GetValue("--tables-dir"),
                    GetValue("--models"),
                    GetValue("--world-dir"),
                    probeMap,
                    at,
                    radius,
                    Console.Out);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FTableFormatException)
            {
                Console.WriteLine($"Could not explain the bake: {ex.Message}");
                return 1;
            }

            return 0;
        }

        var bakeDir = GetValue("--bake");
        if (bakeDir is not null)
        {
            ushort? onlyMap = null;
            var mapValue = GetValue("--map");
            if (mapValue is not null)
            {
                if (!ushort.TryParse(mapValue, System.Globalization.CultureInfo.InvariantCulture, out ushort mapId))
                {
                    Console.WriteLine($"--map takes a map id, not '{mapValue}'.");
                    return 2;
                }

                onlyMap = mapId;
            }

            try
            {
                NavigationBakeCommand.Run(
                    bakeDir,
                    GetValue("--tables-dir"),
                    GetValue("--models"),
                    GetValue("--world-dir"),
                    onlyMap,
                    Console.Out);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FTableFormatException)
            {
                Console.WriteLine($"Could not bake walkable ground: {ex.Message}");
                return 1;
            }

            return 0;
        }

        if (args.Contains("--synthetic", StringComparer.OrdinalIgnoreCase))
        {
            var residual = GetValue("--out") ?? Path.Combine("data", "generated");
            try
            {
                Ubiquitous.Data.DataPackageWriter.WriteSyntheticPackage(residual);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"Could not write package: {ex.Message}");
                return 1;
            }

            Console.WriteLine($"Wrote synthetic package to {residual}.");
            return 0;
        }

        var checkDir = GetValue("--check");
        if (checkDir is not null)
        {
            return Check(checkDir);
        }

        Console.WriteLine("Usage: asset-extractor (--ingest <build-or-dir> | --synthetic --out <dir> | --check <dir> | --inspect <archive> | --extract <archive> --out <dir> | --extract-world <archive-or-dir> --out <dir> | --extract-pack <build> --pack <name> --out <dir> | --import <archive> --out <dir> | --import-from-dir <dir> --out <dir> | --bake <package-dir> | --nav-probe <package-dir> --map <id> --at <x,y,z> | --help)");
        return 2;
    }

    internal static int Check(string checkDir)
    {
        Ubiquitous.Data.DataValidationResult validation;
        try
        {
            validation = Ubiquitous.Data.DataPackageWriter.ValidatePackage(checkDir);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or System.Text.Json.JsonException)
        {
            Console.WriteLine($"Invalid package: {ex.Message}");
            return 1;
        }

        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
            {
                Console.WriteLine($"  - {error}");
            }

            return 1;
        }

        Console.WriteLine("Package valid.");
        return 0;
    }

    private static bool TryReadPoint(string text, out Ubiquitous.Common.Position point)
    {
        point = default;
        var parts = text.Split(',');
        if (parts.Length != 3) return false;

        var numbers = new float[3];
        for (int i = 0; i < 3; i++)
        {
            if (!float.TryParse(
                parts[i].Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out numbers[i]))
            {
                return false;
            }
        }

        point = new Ubiquitous.Common.Position(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    private static IReadOnlyDictionary<string, byte[]>? ReadArchive(string archivePath, out byte[] archiveBytes)
    {
        try
        {
            archiveBytes = File.ReadAllBytes(archivePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not read archive: {ex.Message}");
            archiveBytes = [];
            return null;
        }

        try
        {
            return TableArchiveReader.ReadEntries(archiveBytes);
        }
        catch (TableArchiveFormatException ex)
        {
            Console.WriteLine($"Not a recognized packed archive: {ex.Message}");
            return null;
        }
    }

    private static IReadOnlyDictionary<string, byte[]>? ReadArchive(string archivePath) =>
        ReadArchive(archivePath, out _);

    private static int Inspect(string archivePath)
    {
        if (ReadArchive(archivePath) is not { } entries)
        {
            return 1;
        }

        Console.WriteLine($"{entries.Count} entries.");
        foreach (var name in entries.Keys.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!name.EndsWith(".tbl", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  {name} (not a table)");
                continue;
            }

            try
            {
                var header = TableLayoutReader.Read(entries[name]);
                Console.WriteLine($"  {name}: {header.Rows} rows, {header.Cols} cols, format \"{header.ColumnTypes}\"");
            }
            catch (FTableFormatException ex)
            {
                Console.WriteLine($"  {name}: not a readable table ({ex.Message})");
            }
        }

        return 0;
    }

    internal static int Extract(string archivePath, string outDirectory)
    {
        if (ReadArchive(archivePath) is not { } entries)
        {
            return 1;
        }

        var (destinations, refused) = ArchivePaths.AllInside(outDirectory, entries.Keys);
        if (destinations is null)
        {
            Console.Error.WriteLine(
                $"Refusing {archivePath}: entry \"{refused}\" would be written outside {outDirectory}. The file may have been tampered with.");
            return 1;
        }

        int written = 0;
        foreach (var (name, bytes) in entries)
        {
            string destination = destinations[name];
            string? destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.WriteAllBytes(destination, bytes);
            written++;
        }

        Console.WriteLine($"Extracted {written} entries to {outDirectory}.");
        return 0;
    }

    private static IReadOnlyList<(string Name, Func<byte[]> Read)> MapArchivesInside(string path)
    {
        try
        {
            using var container = PackedArchive.Open(path);
            var names = container.Names
                .Where(name => name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (names.Length <= 1) return [];
            return [.. names.Select(name => (Path.GetFileName(name), (Func<byte[]>)(() => ReadEntry(path, name))))];
        }
        catch (Exception ex) when (ex is PackedArchiveFormatException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static byte[] ReadEntry(string archivePath, string entry)
    {
        using var archive = PackedArchive.Open(archivePath);
        return archive.Read(entry);
    }

    private static int CopyOutOfBuild(
        IReadOnlyList<(string Name, Func<byte[]> Read)> entries, string destination)
    {
        int copied = 0;
        int skipped = 0;
        foreach (var (name, read) in entries)
        {
            try
            {
                var bytes = read();
                using var archive = PackedArchive.Open(new MemoryStream(bytes, writable: false));
                if (!LooksLikeAMap(archive))
                {
                    skipped++;
                    continue;
                }

                File.WriteAllBytes(Path.Combine(destination, name), bytes);
                Console.WriteLine($"  {name}");
                copied++;
            }
            catch (Exception ex) when (ex is PackedArchiveFormatException or IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        Console.WriteLine($"{copied} world archives in {destination} ({skipped} skipped).");
        return copied > 0 ? 0 : 1;
    }

    private static bool LooksLikeAMap(PackedArchive archive) =>
        archive.Names.Any(name => WorldItemReader.TileOfEntry(name) is not null)
        || archive.Names.Any(name => name.Contains("/terrain/", StringComparison.OrdinalIgnoreCase));

    internal static int ExtractWorld(string source, string outDirectory)
    {
        var archives = new List<string>();
        var insideBuild = new List<(string Name, Func<byte[]> Read)>();
        if (Directory.Exists(source))
        {
            archives.AddRange(Directory.EnumerateFiles(source, "*.bin").OrderBy(path => path, StringComparer.Ordinal));
        }
        else if (File.Exists(source))
        {
            insideBuild.AddRange(MapArchivesInside(source));
            if (insideBuild.Count == 0) archives.Add(source);
        }
        else
        {
            Console.WriteLine($"No world archive at {source}.");
            return 1;
        }

        var destination = Path.Combine(outDirectory, "world");
        Directory.CreateDirectory(destination);

        if (insideBuild.Count > 0) return CopyOutOfBuild(insideBuild, destination);

        int copied = 0;
        int skipped = 0;
        foreach (var path in archives)
        {
            int tiles;
            try
            {
                using var archive = PackedArchive.Open(path);
                tiles = archive.Names.Count(name => WorldItemReader.TileOfEntry(name) is not null);
                if (tiles == 0 && !archive.Names.Any(name => name.Contains("/terrain/", StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"  {Path.GetFileName(path)}: no map in it — skipped.");
                    skipped++;
                    continue;
                }
            }
            catch (Exception ex) when (ex is PackedArchiveFormatException or IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"  {Path.GetFileName(path)}: {ex.Message} — skipped.");
                skipped++;
                continue;
            }

            var target = Path.Combine(destination, Path.GetFileName(path));
            try
            {
                File.Copy(path, target, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"  {Path.GetFileName(path)}: {ex.Message} — skipped.");
                skipped++;
                continue;
            }

            long megabytes = new FileInfo(target).Length / (1024 * 1024);
            Console.WriteLine(
                $"  {Path.GetFileName(path)}: {megabytes} MB, {tiles} tiles of scenery.");
            copied++;
        }

        Console.WriteLine($"{copied} world archives in {destination} ({skipped} skipped).");
        return copied == 0 ? 1 : 0;
    }

    private static int Import(string archivePath, string outDirectory, IReadOnlyDictionary<string, byte[]>? worldArchives = null)
    {
        if (ReadArchive(archivePath, out var archiveBytes) is not { } entries)
        {
            return 1;
        }

        PackageImporter.ImportResult result;
        try
        {
            result = PackageImporter.ImportBuild114(entries, outDirectory, PackageImporter.HashArchive(archiveBytes), worldArchives);
        }
        catch (FTableFormatException ex)
        {
            Console.WriteLine($"Could not import: {ex.Message}");
            return 1;
        }

        ReportImportResult(outDirectory, result);
        return 0;
    }

    internal static int ImportFromDir(
        string tablesDirectory, string outDirectory, IReadOnlyDictionary<string, byte[]>? worldArchives = null)
    {
        var entries = new Dictionary<string, byte[]>();
        foreach (var file in Directory.GetFiles(tablesDirectory))
        {
            entries[$"tables/{Path.GetFileName(file)}"] = File.ReadAllBytes(file);
        }

        PackageImporter.ImportResult result;
        try
        {
            result = PackageImporter.ImportBuild114(entries, outDirectory, sourceHash: "local-tables-directory", worldArchives);
        }
        catch (FTableFormatException ex)
        {
            Console.WriteLine($"Could not import: {ex.Message}");
            return 1;
        }

        ReportImportResult(outDirectory, result);
        return 0;
    }

    private static void ReportImportResult(string outDirectory, PackageImporter.ImportResult result)
    {
        Console.WriteLine($"Wrote package to {outDirectory}:");
        Console.WriteLine($"  {result.MapCount} maps, {result.CreatureCount} creatures, " +
            $"{result.SpawnCount} creature spawns ({result.DroppedSpawnCount} dropped: unresolvable creature entry), " +
            $"{result.SpellCount} spells, {result.ItemCount} items, " +
            $"{result.ZoneCount} zones ({result.DroppedZoneCount} dropped), " +
            $"{result.QuestCount} quests ({result.DroppedQuestCount} dropped), " +
            $"{result.NpcCount} service NPCs (NPC flags plus quest givers), {result.QuestRewardCount} quest rewards, " +
            $"{result.TeleportStopCount} teleport stops ({result.DroppedTeleportStopCount} dropped), " +
            $"{result.MapGraveyardCount} map graveyards ({result.DroppedMapGraveyardCount} dropped), " +
            $"{result.TravelRouteCount} travel routes ({result.DroppedTravelRouteCount} dropped), " +
            $"{result.StaticObjectCount} static object spawns ({result.DroppedStaticObjectCount} dropped), " +
            $"{result.StorageUpgradeCount} storage upgrades, {result.StaticObjectTypeCount} static object types, " +
            $"{result.VendorStockCount} vendor stock tabs, " +
            $"{result.StaticObjectQuestStarterCount} static object quest starters, " +
            $"{result.StaticObjectQuestFinisherCount} static object quest finishers, " +
            $"{result.StaticObjectQuestItemBindingCount} static object quest item bindings, " +
            $"{result.CreatureQuestFinisherCount} creature quest finishers, " +
            $"{result.CreatureLootTableCount} per-creature loot tables, " +
            $"{result.GossipMenuCount} conversation menus ({result.DroppedGossipTeleportCount} teleport offers dropped), " +
            $"{result.TalentTreeCount} talent trees.");
    }
}
