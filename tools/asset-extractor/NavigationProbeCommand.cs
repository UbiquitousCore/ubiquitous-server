using System.Globalization;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using DotRecast.Recast;
using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public static class NavigationProbeCommand
{
    private const float SweepSpacing = 1f;

    private const int MaxSweepPoints = 4000;

    private const float SameFloor = 2f;

    public static void Run(
        string packageDirectory,
        string? tablesDirectory,
        string? modelDirectory,
        string? worldDirectory,
        ushort mapId,
        Position at,
        float radius,
        TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(packageDirectory);
        ArgumentNullException.ThrowIfNull(log);

        var parameters = NavigationBaker.DefaultParameters;
        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"map {mapId} at ({at.X:F1}, {at.Y:F1}, {at.Z:F1}); body radius {parameters.BodyRadius}, cell {parameters.CellSize}, step {parameters.StepHeight}, slope {parameters.MaxSlopeDegrees} deg, headroom {parameters.Headroom}"));
        log.WriteLine();

        var gap = ReportFinishedMesh(packageDirectory, mapId, at, radius, log);

        var map = NavigationBakeCommand
            .EachMap(packageDirectory, tablesDirectory, modelDirectory, worldDirectory, mapId, log)
            .FirstOrDefault();
        if (map is null)
        {
            log.WriteLine($"map {mapId} has no terrain in this package, so there was nothing to bake.");
            return;
        }

        log.WriteLine();
        ReportStages(map.Collision, parameters, at, log);

        if (gap is { } missing)
        {
            log.WriteLine();
            log.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"and the square in the middle of the gap, at ({missing.X:F1}, {missing.Y:F1}, {missing.Z:F1}) —"));
            ReportStages(map.Collision, parameters, missing, log);
        }
    }

    private static Position? ReportFinishedMesh(
        string packageDirectory, ushort mapId, Position at, float radius, TextWriter log)
    {
        var path = Path.Combine(
            packageDirectory, NavigationPackage.DirectoryName, NavigationPackage.FileName(mapId));
        if (!File.Exists(path))
        {
            log.WriteLine($"no bake on disk for map {mapId}; run the bake first to compare against it.");
            return null;
        }

        NavigationPackageFile file;
        using (var stream = File.OpenRead(path))
        {
            file = NavigationPackage.Read(stream);
        }

        using var buffer = new MemoryStream(file.Mesh, writable: false);
        using var reader = new BinaryReader(buffer);
        var mesh = new DtMeshSetReader().Read(reader);
        var query = new DtNavMeshQuery(mesh);
        var filter = new DtQueryDefaultFilter();

        log.WriteLine("what the finished bake holds here");
        var here = NearestPolygon(query, filter, at, new RcVec3f(2f, 4f, 2f));
        if (here.Ref == 0)
        {
            log.WriteLine("  nothing walkable within two units of the point, at any height.");
            var anywhere = NearestPolygon(query, filter, at, new RcVec3f(20f, 200f, 20f));
            log.WriteLine(anywhere.Ref == 0
                ? "  nor within twenty units of it."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"  the nearest ground at all is at ({anywhere.At.X:F1}, {anywhere.At.Y:F1}, {anywhere.At.Z:F1})."));
            return null;
        }

        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  standing on ground at ({here.At.X:F1}, {here.At.Y:F2}, {here.At.Z:F1}), {here.At.Y - at.Y:+0.00;-0.00} from the height asked about."));

        return ReportReach(query, filter, here, at, radius, log);
    }

    private static Position? ReportReach(
        DtNavMeshQuery query,
        IDtQueryFilter filter,
        (long Ref, RcVec3f At) from,
        Position at,
        float radius,
        TextWriter log)
    {
        int withGround = 0;
        int straight = 0;
        int reachable = 0;
        int tried = 0;
        var path = new long[512];
        var cutOff = new HashSet<long>();

        var onThisFloor = new RcVec3f(SweepSpacing, SameFloor, SweepSpacing);

        for (float x = at.X - radius; x <= at.X + radius && tried < MaxSweepPoints; x += SweepSpacing)
        {
            for (float z = at.Z - radius; z <= at.Z + radius && tried < MaxSweepPoints; z += SweepSpacing)
            {
                float dx = x - at.X;
                float dz = z - at.Z;
                if ((dx * dx) + (dz * dz) > radius * radius) continue;

                tried++;
                var target = NearestPolygon(query, filter, new Position(x, from.At.Y, z), onThisFloor);
                if (target.Ref == 0) continue;

                withGround++;

                var line = query.Raycast(
                    from.Ref, from.At, target.At, filter, out float got, out _, path, out _, path.Length);
                if (line.Succeeded() && got >= 1f) straight++;

                var status = query.FindPath(
                    from.Ref, target.Ref, from.At, target.At, filter, path, out int count, path.Length);
                if (status.Succeeded() && count > 0 && path[count - 1] == target.Ref) reachable++;
                else cutOff.Add(target.Ref);
            }
        }

        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  of {tried} points within {radius:F0} units, {withGround} have ground on this floor."));
        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {straight} of them are a straight walk away; {reachable} can be walked to at all."));
        if (withGround > reachable)
        {
            log.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {withGround - reachable} patches of ground are cut off from this one — walled in, or an island the bake never joined up."));
            var mesh = query.GetAttachedNavMesh();
            var islands = ReportIslands(mesh, from.Ref, cutOff, log);
            return ReportNearestOtherIsland(mesh, query, filter, islands, from.Ref, at, from.At.Y, log);
        }

        return null;
    }

    private static Dictionary<long, long> ReportIslands(
        DtNavMesh mesh, long standingOn, HashSet<long> cutOff, TextWriter log)
    {
        var island = Islands(mesh);
        var sizes = new Dictionary<long, int>();
        foreach (var (_, root) in island)
        {
            sizes[root] = sizes.GetValueOrDefault(root) + 1;
        }

        var reached = new HashSet<long>();
        var cutSizes = new List<int>();
        foreach (long polygon in cutOff)
        {
            long root = island[polygon];
            if (!reached.Add(root)) continue;
            cutSizes.Add(sizes[root]);
        }

        cutSizes.Sort();
        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  they sit in {cutSizes.Count} separate islands of {cutSizes.Sum()} polygons; the largest is {cutSizes[^1]}, the middling one {cutSizes[cutSizes.Count / 2]}."));
        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  the ground being stood on is one island of {sizes[island[standingOn]]} polygons."));
        log.WriteLine(cutSizes[^1] <= 4
            ? "  every one of them is a scrap — the top of a gravestone, a crate, a fence post. Nothing is meant to walk there."
            : "  some of them are whole pieces of floor, which is a bake that has torn ground in half.");
        return island;
    }

    private static Position? ReportNearestOtherIsland(
        DtNavMesh mesh,
        DtNavMeshQuery query,
        IDtQueryFilter filter,
        Dictionary<long, long> islands,
        long standingOn,
        Position at,
        float floorY,
        TextWriter log)
    {
        long mine = islands[standingOn];
        var here = new RcVec3f(at.X, floorY, at.Z);
        var found = new long[1024];
        if (query.QueryPolygons(
            here, new RcVec3f(12f, SameFloor, 12f), filter, found, out int count, found.Length).Failed())
        {
            return null;
        }

        long best = 0;
        float bestGap = float.MaxValue;
        var bestAt = default(RcVec3f);
        for (int i = 0; i < count; i++)
        {
            if (islands[found[i]] == mine) continue;
            mesh.ClosestPointOnPoly(found[i], here, out var closest, out _);
            float dx = closest.X - at.X;
            float dz = closest.Z - at.Z;
            float gap = MathF.Sqrt((dx * dx) + (dz * dz));
            if (gap >= bestGap) continue;

            best = found[i];
            bestGap = gap;
            bestAt = closest;
        }

        if (best == 0)
        {
            log.WriteLine("  no other ground comes within twelve units of this island on this floor.");
            return null;
        }

        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  the nearest ground it is not joined to is {bestGap:F2} units away and {bestAt.Y - floorY:+0.00;-0.00} in height."));

        var tight = new RcVec3f(Step, SameFloor, Step);
        for (float along = Step; along < bestGap; along += Step)
        {
            float t = along / bestGap;
            var on = new Position(
                at.X + ((bestAt.X - at.X) * t),
                floorY + ((bestAt.Y - floorY) * t),
                at.Z + ((bestAt.Z - at.Z) * t));
            if (NearestPolygon(query, filter, on, tight).Ref == 0)
            {
                log.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  the ground runs out {along:F1} units along the way to it."));
                return on;
            }
        }

        log.WriteLine("  there is ground the whole way to it, so the two are touching and simply not joined.");
        return null;
    }

    private const float Step = 0.5f;

    private static Dictionary<long, long> Islands(DtNavMesh mesh)
    {
        var root = new Dictionary<long, long>();

        long Find(long of)
        {
            long at = of;
            while (root[at] != at) at = root[at];
            root[of] = at;
            return at;
        }

        for (int i = 0; i < mesh.GetMaxTiles(); i++)
        {
            var tile = mesh.GetTile(i);
            if (tile?.data?.header is null) continue;
            long baseRef = mesh.GetPolyRefBase(tile);
            for (int poly = 0; poly < tile.data.header.polyCount; poly++)
            {
                long refs = baseRef | (long)poly;
                root[refs] = refs;
            }
        }

        for (int i = 0; i < mesh.GetMaxTiles(); i++)
        {
            var tile = mesh.GetTile(i);
            if (tile?.data?.header is null) continue;
            long baseRef = mesh.GetPolyRefBase(tile);
            for (int poly = 0; poly < tile.data.header.polyCount; poly++)
            {
                long refs = baseRef | (long)poly;
                for (int link = tile.data.polys[poly].firstLink;
                    link != DtDetour.DT_NULL_LINK;
                    link = tile.links[link].next)
                {
                    long other = tile.links[link].refs;
                    if (other == 0 || !root.ContainsKey(other)) continue;
                    long a = Find(refs);
                    long b = Find(other);
                    if (a != b) root[a] = b;
                }
            }
        }

        var islands = new Dictionary<long, long>(root.Count);
        foreach (long polygon in root.Keys) islands[polygon] = Find(polygon);
        return islands;
    }

    private static void ReportStages(
        ICollisionSource collision, NavigationParameters parameters, Position at, TextWriter log)
    {
        var config = NavigationBaker.ConfigFor(parameters);
        float tileSize = parameters.CellSize * NavigationBaker.TileCells;
        float border = config.BorderSize * parameters.CellSize;
        int tileX = (int)MathF.Floor(at.X / tileSize);
        int tileZ = (int)MathF.Floor(at.Z / tileSize);

        var tiles = collision.TilesOf(tileSize).ToArray();
        var (lowest, highest) = NavigationBaker.HeightRange(collision, tiles, tileSize, border);
        if (float.IsInfinity(lowest))
        {
            log.WriteLine("the map has no collision at all.");
            return;
        }

        float minX = tileX * tileSize;
        float minZ = tileZ * tileSize;
        var triangles = collision.TrianglesIn(
            minX - border, minZ - border, minX + tileSize + border, minZ + tileSize + border);

        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"what the bake did with this square (tile {tileX},{tileZ}, {triangles.Count} triangles reach it)"));
        if (triangles.Count == 0)
        {
            log.WriteLine("  no collision reaches this tile. There is nothing here to bake — this is a hole in the extract, not in the bake.");
            return;
        }

        var geometry = NavigationBaker.GeometryOf(triangles);
        var builderConfig = new RcBuilderConfig(
            config,
            new RcVec3f(0f, lowest - parameters.StepHeight, 0f),
            new RcVec3f(0f, highest + parameters.Headroom + NavigationBaker.CellHeight, 0f),
            tileX,
            tileZ);

        var context = new RcContext();
        var solid = new RcHeightfield(
            builderConfig.width,
            builderConfig.height,
            builderConfig.bmin,
            builderConfig.bmax,
            config.Cs,
            config.Ch,
            config.BorderSize);

        int cellX = (int)MathF.Floor((at.X - solid.bmin.X) / config.Cs);
        int cellZ = (int)MathF.Floor((at.Z - solid.bmin.Z) / config.Cs);
        if (cellX < 0 || cellX >= solid.width || cellZ < 0 || cellZ >= solid.height)
        {
            log.WriteLine("  the point is outside the tile it was worked out to be in; nothing to say.");
            return;
        }

        ReportCollision(triangles, at, config.Cs, config.WalkableSlopeAngle, log);

        var areas = RcRecast.MarkWalkableTriangles(
            context, config.WalkableSlopeAngle, geometry.vertices, geometry.faces,
            geometry.faces.Length / 3, config.WalkableAreaMod);
        int steep = areas.Count(area => area == RcRecast.RC_NULL_AREA);
        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  slope         {areas.Length - steep} of {areas.Length} triangles on this tile are flat enough to walk on"));

        RcRasterizations.RasterizeTriangles(
            context, geometry.vertices, geometry.faces, areas, areas.Length, solid, config.WalkableClimb);
        Report(log, "  rasterised   ", Surfaces(solid, cellX, cellZ));

        RcFilters.FilterLowHangingWalkableObstacles(context, config.WalkableClimb, solid);
        Report(log, "  low kerbs    ", Surfaces(solid, cellX, cellZ));

        RcFilters.FilterLedgeSpans(context, config.WalkableHeight, config.WalkableClimb, solid);
        Report(log, "  ledges       ", Surfaces(solid, cellX, cellZ));

        RcFilters.FilterWalkableLowHeightSpans(context, config.WalkableHeight, solid);
        Report(log, "  headroom     ", Surfaces(solid, cellX, cellZ));

        var compact = RcCompacts.BuildCompactHeightfield(
            context, config.WalkableHeight, config.WalkableClimb, solid);
        Report(log, "  standable    ", CompactSurfaces(compact, cellX, cellZ, regions: false));

        foreach (var volume in geometry.ConvexVolumes())
        {
            RcAreas.MarkConvexPolyArea(context, volume.verts, volume.hmin, volume.hmax, volume.areaMod, compact);
        }

        RcAreas.ErodeWalkableArea(context, config.WalkableRadius, compact);
        Report(log, "  body radius  ", CompactSurfaces(compact, cellX, cellZ, regions: false));

        RcRegions.BuildDistanceField(context, compact);
        RcRegions.BuildRegions(context, compact, config.MinRegionArea, config.MergeRegionArea);
        Report(log, "  islands      ", CompactSurfaces(compact, cellX, cellZ, regions: true));

        var built = new RcBuilder().Build(geometry, builderConfig, keepInterResults: false);
        var mesh = built?.Mesh;
        int covering = mesh is null ? 0 : PolygonsOver(mesh, at.X, at.Z);
        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  polygons      {covering} cover this square"));
    }

    private static void ReportCollision(
        IReadOnlyList<BakeTriangle> triangles, Position at, float cellSize, float slopeDegrees, TextWriter log)
    {
        int over = 0;
        int facingUp = 0;
        float lowest = float.MaxValue;
        float highest = float.MinValue;
        float nearest = float.NaN;
        float cosLimit = MathF.Cos(slopeDegrees * MathF.PI / 180f);
        _ = cellSize;

        var listed = new List<string>();
        foreach (var t in triangles)
        {
            if (!Covers([(t.A.X, t.A.Z), (t.B.X, t.B.Z), (t.C.X, t.C.Z)], at.X, at.Z)) continue;

            if (listed.Count < TrianglesNamed)
            {
                listed.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"({t.A.X:F1},{t.A.Y:F2},{t.A.Z:F1}) ({t.B.X:F1},{t.B.Y:F2},{t.B.Z:F1}) ({t.C.X:F1},{t.C.Y:F2},{t.C.Z:F1}) facing {UprightNess(t):F2}"));
            }

            over++;
            float middle = (t.A.Y + t.B.Y + t.C.Y) / 3f;
            lowest = MathF.Min(lowest, middle);
            highest = MathF.Max(highest, middle);
            if (float.IsNaN(nearest) || MathF.Abs(middle - at.Y) < MathF.Abs(nearest - at.Y)) nearest = middle;
            if (UprightNess(t) >= cosLimit) facingUp++;
        }

        if (over == 0)
        {
            log.WriteLine("  collision     nothing lies over this point at any height. There is no floor here to bake — whatever is wrong is in the extract or in what the map places, and no setting will put ground here.");
            return;
        }

        log.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  collision     {over} triangles lie over it, {facingUp} of them flat enough to stand on; they run from {lowest:F2} to {highest:F2}, nearest {nearest:F2}"));
        if (MathF.Abs(nearest - at.Y) > 1f)
        {
            log.WriteLine(
                "                nothing within a unit of the height asked about — the floor a body stands on here is not in the collision.");
        }

        foreach (var triangle in listed) log.WriteLine($"                {triangle}");
    }

    private const int TrianglesNamed = 8;

    private static float UprightNess(BakeTriangle t)
    {
        float ux = t.B.X - t.A.X, uy = t.B.Y - t.A.Y, uz = t.B.Z - t.A.Z;
        float vx = t.C.X - t.A.X, vy = t.C.Y - t.A.Y, vz = t.C.Z - t.A.Z;
        float nx = (uy * vz) - (uz * vy);
        float ny = (uz * vx) - (ux * vz);
        float nz = (ux * vy) - (uy * vx);
        float length = MathF.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        return length <= 0f ? 0f : ny / length;
    }

    private static List<string> Surfaces(RcHeightfield field, int cellX, int cellZ)
    {
        var heights = new List<string>();
        for (var span = field.spans[cellX + (cellZ * field.width)]; span is not null; span = span.next)
        {
            float top = field.bmin.Y + (span.smax * field.ch);
            heights.Add(span.area == RcRecast.RC_NULL_AREA
                ? string.Create(CultureInfo.InvariantCulture, $"[{top:F2}]")
                : top.ToString("F2", CultureInfo.InvariantCulture));
        }

        return heights;
    }

    private static List<string> CompactSurfaces(RcCompactHeightfield field, int cellX, int cellZ, bool regions)
    {
        var heights = new List<string>();
        var cell = field.cells[cellX + (cellZ * field.width)];
        for (int i = cell.index; i < cell.index + cell.count; i++)
        {
            float top = field.bmin.Y + (field.spans[i].y * field.ch);
            bool kept = field.areas[i] != RcRecast.RC_NULL_AREA && (!regions || field.spans[i].reg != 0);
            heights.Add(kept
                ? top.ToString("F2", CultureInfo.InvariantCulture)
                : string.Create(CultureInfo.InvariantCulture, $"[{top:F2}]"));
        }

        return heights;
    }

    private static void Report(TextWriter log, string stage, List<string> heights)
    {
        int kept = heights.Count(h => !h.StartsWith('['));
        log.WriteLine(heights.Count == 0
            ? $"{stage} nothing at all"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{stage} {kept} of {heights.Count} surfaces stand on: {string.Join(", ", heights)}"));
    }

    private static int PolygonsOver(RcPolyMesh mesh, float x, float z)
    {
        int found = 0;
        for (int poly = 0; poly < mesh.npolys; poly++)
        {
            var corners = new List<(float X, float Z)>();
            for (int slot = 0; slot < mesh.nvp; slot++)
            {
                int index = mesh.polys[(poly * mesh.nvp * 2) + slot];
                if (index == RcRecast.RC_MESH_NULL_IDX) break;
                corners.Add((
                    mesh.bmin.X + (mesh.verts[(index * 3) + 0] * mesh.cs),
                    mesh.bmin.Z + (mesh.verts[(index * 3) + 2] * mesh.cs)));
            }

            if (Covers(corners, x, z)) found++;
        }

        return found;
    }

    private static bool Covers(IReadOnlyList<(float X, float Z)> corners, float x, float z)
    {
        bool inside = false;
        for (int i = 0, j = corners.Count - 1; i < corners.Count; j = i++)
        {
            if ((corners[i].Z > z) == (corners[j].Z > z)) continue;
            float across = ((corners[j].X - corners[i].X) * (z - corners[i].Z)
                / (corners[j].Z - corners[i].Z)) + corners[i].X;
            if (x < across) inside = !inside;
        }

        return inside;
    }

    private static (long Ref, RcVec3f At) NearestPolygon(
        DtNavMeshQuery query, IDtQueryFilter filter, Position at, RcVec3f extents)
    {
        var status = query.FindNearestPoly(
            new RcVec3f(at.X, at.Y, at.Z), extents, filter, out long found, out var point, out _);
        return status.Succeeded() ? (found, point) : (0L, default);
    }
}
