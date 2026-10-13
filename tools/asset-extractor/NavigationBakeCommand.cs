using System.Globalization;
using System.Text.Json;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record NavigationBakeReport(
    ushort MapId,
    NavigationBakeSummary Summary,
    int Placements,
    int MissingModels,
    long Bytes,
    int BuildingPlacements = 0,
    int BuildingsWithCollision = 0,
    int Interiors = 0,
    int Spawns = 0,
    int SpawnsOnGround = 0,
    int WaterSquares = 0)
{
    public float SpawnsOnGroundFraction => Spawns <= 0 ? 1f : (float)SpawnsOnGround / Spawns;
}

public static class NavigationBakeCommand
{
    private const int UnresolvedModelsNamed = 8;

    public static IReadOnlyList<NavigationBakeReport> Run(
        string packageDirectory,
        string? tablesDirectory,
        string? modelDirectory,
        string? worldDirectory,
        ushort? onlyMap,
        TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(packageDirectory);
        ArgumentNullException.ThrowIfNull(log);

        var navigationDirectory = Path.Combine(packageDirectory, NavigationPackage.DirectoryName);
        Directory.CreateDirectory(navigationDirectory);

        var spawnsByMap = LoadSpawnPoints(packageDirectory, log);
        var reports = new List<NavigationBakeReport>();
        foreach (var map in EachMap(packageDirectory, tablesDirectory, modelDirectory, worldDirectory, onlyMap, log))
        {
            var corrections = LoadCorrections(navigationDirectory, map.MapId, log);
            var result = NavigationBaker.Bake(map.Collision, NavigationBaker.DefaultParameters, corrections, map.Water);

            var outPath = Path.Combine(navigationDirectory, NavigationPackage.FileName(map.MapId));
            using (var stream = File.Create(outPath))
            {
                NavigationPackage.Write(
                    stream,
                    new NavigationPackageFile(map.MapId, result.Parameters, NavigationBaker.Serialise(result.Mesh), map.Water));
            }

            var spawns = spawnsByMap.TryGetValue(map.MapId, out var placed) ? placed : [];
            var report = new NavigationBakeReport(
                map.MapId, result.Summary, map.Placements, map.MissingModels, new FileInfo(outPath).Length,
                map.BuildingPlacements, map.BuildingsWithCollision, map.Interiors,
                spawns.Count, StandingOnGround(result.Mesh, spawns), map.Water?.Count ?? 0);
            reports.Add(report);
            log.WriteLine(Describe(report));
        }

        if (reports.Count == 0)
        {
            log.WriteLine("no map had terrain to bake from.");
        }
        else
        {
            log.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"baked {reports.Count} maps, {reports.Sum(report => (long)report.Summary.PolygonTotal)} polygons, into {navigationDirectory}"));
        }

        return reports;
    }

    public sealed record MapCollision(
        ushort MapId,
        ICollisionSource Collision,
        int Placements,
        int MissingModels,
        int BuildingPlacements,
        int BuildingsWithCollision,
        int Interiors,
        IReadOnlyList<NavigationWater>? Water = null);

    public static IEnumerable<MapCollision> EachMap(
        string packageDirectory,
        string? tablesDirectory,
        string? modelDirectory,
        string? worldDirectory,
        ushort? onlyMap,
        TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(packageDirectory);
        ArgumentNullException.ThrowIfNull(log);

        var terrainDirectory = Path.Combine(packageDirectory, TerrainPackage.DirectoryName);
        if (!Directory.Exists(terrainDirectory))
        {
            throw new DirectoryNotFoundException(
                $"No {TerrainPackage.DirectoryName} directory in {packageDirectory}; a map's ground is baked from its terrain and its collision.");
        }

        var placementsByMap = LoadPlacements(tablesDirectory, log);
        var archiveByMap = LoadWorldArchives(tablesDirectory, worldDirectory, log);
        var models = modelDirectory is null ? null : new CollisionModelLibrary(modelDirectory);
        if (models is not null)
        {
            log.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"collision models: {models.FileCount} in {modelDirectory}"));
        }
        else
        {
            log.WriteLine("collision models: none given, baking terrain only");
        }

        foreach (var path in Directory.EnumerateFiles(terrainDirectory, "*.terrain")
            .OrderBy(name => name, StringComparer.Ordinal))
        {
            TerrainPackageFile terrain;
            using (var stream = File.OpenRead(path))
            {
                terrain = TerrainPackage.Read(stream);
            }

            if (onlyMap is not null && terrain.MapId != onlyMap) continue;

            var sources = new List<ICollisionSource> { new TerrainCollisionSource(terrain.Tiles) };
            int placed = 0;
            int missing = 0;
            if (models is not null && placementsByMap.TryGetValue(terrain.MapId, out var placements))
            {
                var scenery = new StaticObjectCollisionSource(placements, models.TrianglesOf);
                placed = scenery.Placements;
                missing = scenery.Unresolved;
                if (scenery.UnresolvedModels.Count > 0)
                {
                    log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"  map {terrain.MapId}: no collision for {scenery.UnresolvedModels.Count} of the models its spawned objects use, among them:"));
                    foreach (var model in scenery.UnresolvedModels.Take(UnresolvedModelsNamed)) log.WriteLine($"    {model}");
                }
                if (placed > 0) sources.Add(scenery);
            }

            int building = 0;
            int buildingCollision = 0;
            int interiors = 0;
            if (models is not null && archiveByMap.TryGetValue(terrain.MapId, out var archivePath))
            {
                var buildings = ReadBuildings(archivePath, models, log);
                if (buildings is not null)
                {
                    building = buildings.Placements;
                    buildingCollision = buildings.Resolved;
                    interiors = buildings.Interiors;
                    if (buildingCollision > 0 || interiors > 0) sources.Add(buildings);
                }
            }

            var water = archiveByMap.TryGetValue(terrain.MapId, out var waterArchive) ? ReadWater(waterArchive, log) : [];

            yield return new MapCollision(
                terrain.MapId,
                sources.Count == 1 ? sources[0] : new CompositeCollisionSource(sources),
                placed,
                missing,
                building,
                buildingCollision,
                interiors,
                water);
        }
    }

    private static IReadOnlyList<NavigationWater> ReadWater(string archivePath, TextWriter log)
    {
        try
        {
            using var archive = PackedArchive.Open(archivePath);
            return TerrainTileReader.ReadWater(archive);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or PackedArchiveFormatException)
        {
            log.WriteLine($"  {Path.GetFileName(archivePath)}: {ex.Message} — baking this map without its water");
            return [];
        }
    }

    private static NavigationOverrides? LoadCorrections(string navigationDirectory, ushort mapId, TextWriter log)
    {
        var path = Path.Combine(navigationDirectory, NavigationPackage.OverrideFileName(mapId));
        if (!File.Exists(path)) return null;

        var corrections = NavigationOverrideFile.Load(path);
        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"map {mapId}: {corrections.Edits.Count} hand-written corrections from {Path.GetFileName(path)}"));
        return corrections;
    }

    private static int StandingOnGround(DtNavMesh mesh, IReadOnlyList<Position> spawns)
    {
        if (spawns.Count == 0) return 0;

        var query = new DtNavMeshQuery(mesh);
        var filter = new DtQueryDefaultFilter();
        var reach = new RcVec3f(SpawnReach, SpawnReach, SpawnReach);
        int found = 0;
        foreach (var spawn in spawns)
        {
            var status = query.FindNearestPoly(
                new RcVec3f(spawn.X, spawn.Y, spawn.Z), reach, filter, out long polygon, out _, out _);
            if (status.Succeeded() && polygon != 0) found++;
        }

        return found;
    }

    private const float SpawnReach = 1f;

    private static IReadOnlyDictionary<ushort, List<Position>> LoadSpawnPoints(string packageDirectory, TextWriter log)
    {
        var byMap = new Dictionary<ushort, List<Position>>();
        var path = Path.Combine(packageDirectory, "creatureSpawns.json");
        if (!File.Exists(path)) return byMap;

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return byMap;

            foreach (var spawn in document.RootElement.EnumerateArray())
            {
                if (!spawn.TryGetProperty("mapId", out var mapId) || !mapId.TryGetUInt16(out ushort map)) continue;
                if (!spawn.TryGetProperty("x", out var x) || !x.TryGetSingle(out float sx)) continue;
                if (!spawn.TryGetProperty("y", out var y) || !y.TryGetSingle(out float sy)) continue;
                if (!spawn.TryGetProperty("z", out var z) || !z.TryGetSingle(out float sz)) continue;

                if (!byMap.TryGetValue(map, out var list)) byMap[map] = list = [];
                list.Add(new Position(sx, sy, sz));
            }
        }
        catch (JsonException error)
        {
            log.WriteLine($"creature spawns are not readable, so the bake cannot check itself against them: {error.Message}");
            return new Dictionary<ushort, List<Position>>();
        }

        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"creature spawns: {byMap.Values.Sum(list => list.Count)} across {byMap.Count} maps, to check each bake against"));
        return byMap;
    }

    public static string Describe(NavigationBakeReport report)
    {
        var walkable = report.Summary.WalkableFraction * 100f;
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"map {report.MapId,3}: {report.Summary.PolygonTotal,8} polygons, " +
            $"{report.Summary.WalkableArea,12:N0} of {report.Summary.CoveredArea,12:N0} square units walkable ({walkable,5:N0}%), " +
            $"{report.Bytes / 1024,7:N0} KB");
        if (report.Spawns > 0)
        {
            line += string.Create(
                CultureInfo.InvariantCulture,
                $", {report.SpawnsOnGround} of {report.Spawns} creatures stand on it ({report.SpawnsOnGroundFraction * 100f:N0}%)");
        }

        if (report.Placements > 0 || report.MissingModels > 0)
        {
            line += string.Create(CultureInfo.InvariantCulture, $", {report.Placements} things standing on it");
            if (report.MissingModels > 0)
            {
                line += string.Create(
                    CultureInfo.InvariantCulture,
                    $" ({report.MissingModels} with no collision in this extract)");
            }
        }

        if (report.BuildingPlacements > 0)
        {
            line += string.Create(
                CultureInfo.InvariantCulture,
                $", {report.BuildingPlacements} placed by the map itself ({report.BuildingsWithCollision} with collision");
            line += report.Interiors > 0
                ? string.Create(CultureInfo.InvariantCulture, $", {report.Interiors} interiors)")
                : ")";
        }

        if (report.WaterSquares > 0)
            line += string.Create(CultureInfo.InvariantCulture, $", {report.WaterSquares} squares of water");

        return line;
    }

    private static WorldArchiveCollisionSource? ReadBuildings(
        string archivePath, CollisionModelLibrary models, TextWriter log)
    {
        try
        {
            using var archive = PackedArchive.Open(archivePath);
            var tiles = WorldItemReader.ReadTiles(archive, out int unreadable);
            if (unreadable > 0)
            {
                log.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {Path.GetFileName(archivePath)}: {unreadable} scenery tiles would not read; their buildings are missing"));
            }

            if (tiles.Count == 0) return null;

            var source = new WorldArchiveCollisionSource(tiles, models.TrianglesOf);
            if (source.UnresolvedModels.Count > 0)
            {
                log.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {Path.GetFileName(archivePath)}: no collision for {source.UnresolvedModels.Count} of the models it places, among them:"));
                foreach (var model in source.UnresolvedModels.Take(UnresolvedModelsNamed))
                {
                    log.WriteLine($"    {model}");
                }
            }

            return source;
        }
        catch (Exception ex) when (ex is PackedArchiveFormatException or IOException or UnauthorizedAccessException)
        {
            log.WriteLine($"  {Path.GetFileName(archivePath)}: {ex.Message} — baking this map without its buildings");
            return null;
        }
    }

    private static IReadOnlyDictionary<ushort, string> LoadWorldArchives(
        string? tablesDirectory, string? worldDirectory, TextWriter log)
    {
        var byMap = new Dictionary<ushort, string>();
        if (worldDirectory is null || !Directory.Exists(worldDirectory))
        {
            log.WriteLine("world archives: none given, a map bakes without the buildings it is made of");
            return byMap;
        }

        if (tablesDirectory is null || !Directory.Exists(tablesDirectory))
        {
            log.WriteLine("world archives: given, but without the tables there is nothing to match them to a map by");
            return byMap;
        }

        var archives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(worldDirectory, "*.bin"))
        {
            archives.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }

        var mapInfo = Path.Combine(tablesDirectory, "map_info.tbl");
        if (!File.Exists(mapInfo))
        {
            log.WriteLine("world archives: no map_info.tbl to match them to a map by");
            return byMap;
        }

        foreach (var map in MapTableMapper.MapMaps(FTableDocument.Load(File.ReadAllBytes(mapInfo)), MapInfoColumns.Build114))
        {
            if (map.File is null) continue;
            if (archives.TryGetValue(Path.GetFileNameWithoutExtension(map.File), out var archive)) byMap[map.Id] = archive;
        }

        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"world archives: {archives.Count} in {worldDirectory}, matched to {byMap.Count} maps"));
        return byMap;
    }

    private static IReadOnlyDictionary<ushort, List<CollisionPlacement>> LoadPlacements(
        string? tablesDirectory, TextWriter log)
    {
        if (tablesDirectory is null || !Directory.Exists(tablesDirectory))
        {
            log.WriteLine("placements: no tables given, nothing will be standing on the ground");
            return new Dictionary<ushort, List<CollisionPlacement>>();
        }

        var byMap = CollisionPlacementReader.ReadByMap(tablesDirectory);
        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"placements: {byMap.Sum(entry => entry.Value.Count)} across {byMap.Count} maps"));
        return byMap;
    }
}
