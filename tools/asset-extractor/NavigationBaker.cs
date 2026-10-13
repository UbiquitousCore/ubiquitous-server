using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public readonly record struct BakeTriangle(Position A, Position B, Position C);

public interface ICollisionSource
{
    IEnumerable<(int X, int Z)> TilesOf(float tileSize);

    IReadOnlyList<BakeTriangle> TrianglesIn(float minX, float minZ, float maxX, float maxZ);

    IEnumerable<NavigationLink> LinksIn(float minX, float minZ, float maxX, float maxZ) => [];
}

public sealed class TriangleCollisionSource(IReadOnlyList<BakeTriangle> triangles) : ICollisionSource
{
    public IEnumerable<(int X, int Z)> TilesOf(float tileSize)
    {
        var tiles = new HashSet<(int X, int Z)>();
        foreach (var triangle in triangles)
        {
            int minX = (int)MathF.Floor(MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X)) / tileSize);
            int maxX = (int)MathF.Floor(MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X)) / tileSize);
            int minZ = (int)MathF.Floor(MathF.Min(triangle.A.Z, MathF.Min(triangle.B.Z, triangle.C.Z)) / tileSize);
            int maxZ = (int)MathF.Floor(MathF.Max(triangle.A.Z, MathF.Max(triangle.B.Z, triangle.C.Z)) / tileSize);
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++) tiles.Add((x, z));
            }
        }

        return tiles;
    }

    public IReadOnlyList<BakeTriangle> TrianglesIn(float minX, float minZ, float maxX, float maxZ)
    {
        var inside = new List<BakeTriangle>();
        foreach (var triangle in triangles)
        {
            if (MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X)) < minX) continue;
            if (MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X)) > maxX) continue;
            if (MathF.Max(triangle.A.Z, MathF.Max(triangle.B.Z, triangle.C.Z)) < minZ) continue;
            if (MathF.Min(triangle.A.Z, MathF.Min(triangle.B.Z, triangle.C.Z)) > maxZ) continue;

            inside.Add(triangle);
        }

        return inside;
    }
}

public sealed class CompositeCollisionSource(IReadOnlyList<ICollisionSource> sources) : ICollisionSource
{
    public IEnumerable<(int X, int Z)> TilesOf(float tileSize) =>
        sources.SelectMany(source => source.TilesOf(tileSize)).Distinct();

    public IReadOnlyList<BakeTriangle> TrianglesIn(float minX, float minZ, float maxX, float maxZ)
    {
        var triangles = new List<BakeTriangle>();
        foreach (var source in sources) triangles.AddRange(source.TrianglesIn(minX, minZ, maxX, maxZ));
        return triangles;
    }

    public IEnumerable<NavigationLink> LinksIn(float minX, float minZ, float maxX, float maxZ) =>
        sources.SelectMany(source => source.LinksIn(minX, minZ, maxX, maxZ));
}

public sealed record NavigationBakeSummary(int PolygonTotal, float WalkableArea, float CoveredArea, int TileCount = 0)
{
    public float WalkableFraction => CoveredArea <= 0f ? 0f : WalkableArea / CoveredArea;
}

public sealed record NavigationBakeResult(
    NavigationParameters Parameters,
    DtNavMesh Mesh,
    NavigationBakeSummary Summary);

public static class NavigationBaker
{
    public const float CellSize = 0.5f;

    public const float CellHeight = 0.1f;

    public const float BodyRadius = 0.4f;

    public const float StepHeight = 0.6f;

    public const float MaxWalkableSlopeDegrees = 60f;

    public const float Headroom = 2.0f;

    public const int TileCells = 128;


    public static NavigationParameters DefaultParameters { get; } =
        new(CellSize, BodyRadius, StepHeight, MaxWalkableSlopeDegrees, Headroom);

    public static NavigationBakeResult Bake(IReadOnlyList<BakeTriangle> triangles) =>
        Bake(new TriangleCollisionSource(triangles), DefaultParameters);

    public static NavigationBakeResult Bake(IReadOnlyList<BakeTriangle> triangles, NavigationParameters parameters) =>
        Bake(new TriangleCollisionSource(triangles), parameters);

    public static NavigationBakeResult Bake(
        IReadOnlyList<BakeTriangle> triangles, NavigationOverrides? corrections) =>
        Bake(new TriangleCollisionSource(triangles), DefaultParameters, corrections);

    public static NavigationBakeResult Bake(
        ICollisionSource collision, NavigationParameters parameters, NavigationOverrides? corrections = null,
        IReadOnlyList<NavigationWater>? water = null)
    {
        ArgumentNullException.ThrowIfNull(collision);
        ArgumentNullException.ThrowIfNull(parameters);

        var added = AddedFloors(corrections);
        var source = added.Count == 0
            ? collision
            : new CompositeCollisionSource([collision, new TriangleCollisionSource(added)]);

        float tileSize = parameters.CellSize * TileCells;
        var tiles = source.TilesOf(tileSize).ToArray();
        if (tiles.Length == 0)
        {
            return new NavigationBakeResult(parameters, EmptyMesh(parameters), new NavigationBakeSummary(0, 0f, 0f));
        }

        var config = ConfigFor(parameters);
        int borderCells = config.BorderSize;
        float border = borderCells * parameters.CellSize;

        var (lowest, highest) = HeightRange(source, tiles, tileSize, border);
        if (float.IsInfinity(lowest))
        {
            return new NavigationBakeResult(parameters, EmptyMesh(parameters), new NavigationBakeSummary(0, 0f, 0f));
        }

        var mesh = NewMesh(parameters, tiles);
        var builder = new RcBuilder();
        var boxes = UnwalkableBoxes(corrections);
        float walkableArea = 0f;
        float coveredArea = 0f;
        int polygonTotal = 0;
        int tileCount = 0;

        foreach (var (tileX, tileZ) in tiles)
        {
            float minX = tileX * tileSize;
            float minZ = tileZ * tileSize;
            var triangles = source.TrianglesIn(
                minX - border, minZ - border, minX + tileSize + border, minZ + tileSize + border);
            if (triangles.Count == 0) continue;

            coveredArea += CoveredInside(triangles, minX, minZ, tileSize);

            var geometry = GeometryOf(triangles);
            foreach (var box in boxes) geometry.AddConvexVolume(box);
            foreach (var pool in SwimVolumes(water, minX - border, minZ - border, minX + tileSize + border, minZ + tileSize + border))
                geometry.AddConvexVolume(pool);

            var built = builder.Build(
                geometry,
                new RcBuilderConfig(
                    config,
                    new RcVec3f(0f, lowest - parameters.StepHeight, 0f),
                    new RcVec3f(0f, highest + parameters.Headroom + CellHeight, 0f),
                    tileX,
                    tileZ),
                keepInterResults: false);

            if (built?.Mesh is not { npolys: > 0 } polyMesh) continue;

            var links = source.LinksIn(minX, minZ, minX + tileSize, minZ + tileSize).ToArray();
            var data = ToTile(polyMesh, built.MeshDetail, parameters, tileX, tileZ, links);
            if (data is null) continue;

            mesh.AddTile(data, 0, 0L, out _);
            tileCount++;
            polygonTotal += polyMesh.npolys;
            walkableArea += AreaOf(polyMesh);
        }

        return new NavigationBakeResult(
            parameters,
            mesh,
            new NavigationBakeSummary(polygonTotal, walkableArea, coveredArea, tileCount));
    }

    public static byte[] Serialise(DtNavMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            new DtMeshSetWriter().Write(writer, mesh, RcByteOrder.LITTLE_ENDIAN, cCompatibility: false);
        }

        return buffer.ToArray();
    }

    internal static RcConfig ConfigFor(NavigationParameters parameters) =>
        new(
            useTiles: true,
            tileSizeX: TileCells,
            tileSizeZ: TileCells,
            borderSize: RcConfig.CalcBorder(parameters.BodyRadius, parameters.CellSize),
            RcPartition.WATERSHED,
            parameters.CellSize,
            CellHeight,
            parameters.MaxSlopeDegrees,
            parameters.Headroom,
            parameters.BodyRadius,
            parameters.StepHeight,
            MinRegionArea,
            MergeRegionArea,
            MaxEdgeLength,
            MaxSimplificationError,
            MaxVerticesPerPolygon,
            DetailSampleDistance,
            DetailSampleMaxError,
            filterLowHangingObstacles: true,
            filterLedgeSpans: true,
            filterWalkableLowHeightSpans: true,
            WalkableArea,
            buildMeshDetail: true);

    internal static RcSampleInputGeomProvider GeometryOf(IReadOnlyList<BakeTriangle> triangles)
    {
        var vertices = new float[triangles.Count * 9];
        var faces = new int[triangles.Count * 3];
        for (int i = 0; i < triangles.Count; i++)
        {
            var t = triangles[i];
            Write(vertices, (i * 3) + 0, t.A);
            Write(vertices, (i * 3) + 1, t.B);
            Write(vertices, (i * 3) + 2, t.C);
            faces[(i * 3) + 0] = (i * 3) + 0;
            faces[(i * 3) + 1] = (i * 3) + 1;
            faces[(i * 3) + 2] = (i * 3) + 2;
        }

        return new RcSampleInputGeomProvider(vertices, faces);

        static void Write(float[] into, int vertex, Position p)
        {
            into[(vertex * 3) + 0] = p.X;
            into[(vertex * 3) + 1] = p.Y;
            into[(vertex * 3) + 2] = p.Z;
        }
    }

    private static DtNavMesh EmptyMesh(NavigationParameters parameters) => NewMesh(parameters, []);

    private static DtNavMesh NewMesh(NavigationParameters parameters, IReadOnlyCollection<(int X, int Z)> tiles)
    {
        float tileSize = parameters.CellSize * TileCells;
        var option = new DtNavMeshParams
        {
            orig = new RcVec3f(0f, 0f, 0f),
            tileWidth = tileSize,
            tileHeight = tileSize,
            maxTiles = Math.Max(1, tiles.Count),
            maxPolys = MaxPolygonsPerTile,
        };

        var mesh = new DtNavMesh();
        mesh.Init(in option, MaxVerticesPerPolygon);
        return mesh;
    }

    private static DtMeshData? ToTile(
        RcPolyMesh mesh,
        RcPolyMeshDetail? detail,
        NavigationParameters parameters,
        int tileX,
        int tileZ,
        IReadOnlyList<NavigationLink> links)
    {
        var flags = new int[mesh.npolys];
        for (int i = 0; i < mesh.npolys; i++)
        {
            if (mesh.areas[i] is RcRecast.RC_WALKABLE_AREA or NavigationAreas.Swim) flags[i] = WalkableFlag;
        }

        var option = new DtNavMeshCreateParams
        {
            verts = mesh.verts,
            vertCount = mesh.nverts,
            polys = mesh.polys,
            polyAreas = mesh.areas,
            polyFlags = flags,
            polyCount = mesh.npolys,
            nvp = mesh.nvp,
            detailMeshes = detail?.meshes,
            detailVerts = detail?.verts,
            detailVertsCount = detail?.nverts ?? 0,
            detailTris = detail?.tris,
            detailTriCount = detail?.ntris ?? 0,
            walkableHeight = parameters.Headroom,
            walkableRadius = parameters.BodyRadius,
            walkableClimb = parameters.StepHeight,
            bmin = new RcVec3f(mesh.bmin.X, mesh.bmin.Y - (mesh.ch / 2f), mesh.bmin.Z),
            bmax = mesh.bmax,
            cs = mesh.cs,
            ch = mesh.ch,
            tileX = tileX,
            tileZ = tileZ,
            buildBvTree = true,
        };

        if (links.Count > 0) AddLinks(option, links);

        return DtNavMeshBuilder.CreateNavMeshData(option);
    }

    private static void AddLinks(DtNavMeshCreateParams option, IReadOnlyList<NavigationLink> links)
    {
        var ends = new float[links.Count * 6];
        var radius = new float[links.Count];
        var flags = new int[links.Count];
        var areas = new int[links.Count];
        var direction = new int[links.Count];
        var identity = new int[links.Count];

        for (int i = 0; i < links.Count; i++)
        {
            ends[(i * 6) + 0] = links[i].From.X;
            ends[(i * 6) + 1] = links[i].From.Y;
            ends[(i * 6) + 2] = links[i].From.Z;
            ends[(i * 6) + 3] = links[i].To.X;
            ends[(i * 6) + 4] = links[i].To.Y;
            ends[(i * 6) + 5] = links[i].To.Z;
            radius[i] = LinkReach;
            flags[i] = WalkableFlag;
            areas[i] = RcRecast.RC_WALKABLE_AREA;
            direction[i] = DtDetour.DT_OFFMESH_CON_BIDIR;
            identity[i] = i;
        }

        option.offMeshConVerts = ends;
        option.offMeshConRad = radius;
        option.offMeshConFlags = flags;
        option.offMeshConAreas = areas;
        option.offMeshConDir = direction;
        option.offMeshConUserID = identity;
        option.offMeshConCount = links.Count;
    }

    private const float LinkReach = 2f;

    internal static IReadOnlyList<BakeTriangle> AddedFloors(NavigationOverrides? corrections)
    {
        if (corrections is null) return [];

        var triangles = new List<BakeTriangle>();
        foreach (var edit in corrections.Edits)
        {
            if (edit.Kind != NavigationEditKind.Walkable || edit.FloorY is not { } y) continue;
            var a = new Position(edit.MinX, y, edit.MinZ);
            var b = new Position(edit.MinX, y, edit.MaxZ);
            var c = new Position(edit.MaxX, y, edit.MaxZ);
            var d = new Position(edit.MaxX, y, edit.MinZ);
            triangles.Add(new BakeTriangle(a, b, c));
            triangles.Add(new BakeTriangle(a, c, d));
        }

        return triangles;
    }

    // Ground lying deep enough under a water square to swim over is painted the swim area.
    internal static IEnumerable<RcConvexVolume> SwimVolumes(
        IReadOnlyList<NavigationWater>? water, float minX, float minZ, float maxX, float maxZ)
    {
        foreach (var square in water ?? [])
        {
            if (square.MinX > maxX || square.MinX + square.Size < minX || square.MinZ > maxZ || square.MinZ + square.Size < minZ)
                continue;
            float toX = square.MinX + square.Size;
            float toZ = square.MinZ + square.Size;
            yield return new RcConvexVolume
            {
                verts = [square.MinX, 0f, square.MinZ, square.MinX, 0f, toZ, toX, 0f, toZ, toX, 0f, square.MinZ],
                hmin = float.MinValue / 2f,
                hmax = square.Surface - NavigationAreas.SwimDepth,
                areaMod = SwimArea,
            };
        }
    }

    internal static IReadOnlyList<RcConvexVolume> UnwalkableBoxes(NavigationOverrides? corrections)
    {
        if (corrections is null) return [];

        var volumes = new List<RcConvexVolume>();
        foreach (var edit in corrections.Edits)
        {
            if (edit.Kind != NavigationEditKind.Unwalkable) continue;
            volumes.Add(new RcConvexVolume
            {
                verts =
                [
                    edit.MinX, 0f, edit.MinZ,
                    edit.MinX, 0f, edit.MaxZ,
                    edit.MaxX, 0f, edit.MaxZ,
                    edit.MaxX, 0f, edit.MinZ,
                ],
                hmin = edit.MinY ?? float.MinValue / 2f,
                hmax = edit.MaxY ?? float.MaxValue / 2f,
                areaMod = NotWalkableArea,
            });
        }

        return volumes;
    }

    internal static (float Lowest, float Highest) HeightRange(
        ICollisionSource collision, IReadOnlyList<(int X, int Z)> tiles, float tileSize, float border)
    {
        float lowest = float.PositiveInfinity;
        float highest = float.NegativeInfinity;
        foreach (var (tileX, tileZ) in tiles)
        {
            float minX = tileX * tileSize;
            float minZ = tileZ * tileSize;
            foreach (var t in collision.TrianglesIn(
                minX - border, minZ - border, minX + tileSize + border, minZ + tileSize + border))
            {
                lowest = MathF.Min(lowest, MathF.Min(t.A.Y, MathF.Min(t.B.Y, t.C.Y)));
                highest = MathF.Max(highest, MathF.Max(t.A.Y, MathF.Max(t.B.Y, t.C.Y)));
            }
        }

        return (lowest, highest);
    }

    private static float CoveredInside(
        IReadOnlyList<BakeTriangle> triangles, float minX, float minZ, float tileSize)
    {
        float lowX = float.MaxValue, highX = float.MinValue;
        float lowZ = float.MaxValue, highZ = float.MinValue;
        foreach (var t in triangles)
        {
            lowX = MathF.Min(lowX, MathF.Min(t.A.X, MathF.Min(t.B.X, t.C.X)));
            highX = MathF.Max(highX, MathF.Max(t.A.X, MathF.Max(t.B.X, t.C.X)));
            lowZ = MathF.Min(lowZ, MathF.Min(t.A.Z, MathF.Min(t.B.Z, t.C.Z)));
            highZ = MathF.Max(highZ, MathF.Max(t.A.Z, MathF.Max(t.B.Z, t.C.Z)));
        }

        float width = MathF.Max(0f, MathF.Min(highX, minX + tileSize) - MathF.Max(lowX, minX));
        float depth = MathF.Max(0f, MathF.Min(highZ, minZ + tileSize) - MathF.Max(lowZ, minZ));
        return width * depth;
    }

    private static float AreaOf(RcPolyMesh mesh)
    {
        float total = 0f;
        for (int poly = 0; poly < mesh.npolys; poly++)
        {
            float twice = 0f;
            var corners = new List<(float X, float Z)>();
            for (int slot = 0; slot < mesh.nvp; slot++)
            {
                int index = mesh.polys[(poly * mesh.nvp * 2) + slot];
                if (index == RcRecast.RC_MESH_NULL_IDX) break;
                corners.Add((
                    mesh.bmin.X + (mesh.verts[(index * 3) + 0] * mesh.cs),
                    mesh.bmin.Z + (mesh.verts[(index * 3) + 2] * mesh.cs)));
            }

            for (int i = 0; i < corners.Count; i++)
            {
                var current = corners[i];
                var next = corners[(i + 1) % corners.Count];
                twice += (current.X * next.Z) - (next.X * current.Z);
            }

            total += MathF.Abs(twice) / 2f;
        }

        return total;
    }

    public const int WalkableFlag = 1;

    private const int MaxPolygonsPerTile = 1 << 14;

    private static readonly RcAreaModification WalkableArea = new(RcRecast.RC_WALKABLE_AREA);

    private static readonly RcAreaModification NotWalkableArea = new(RcRecast.RC_NULL_AREA);

    private static readonly RcAreaModification SwimArea = new(NavigationAreas.Swim);

    private const float MinRegionArea = 1f;

    private const float MergeRegionArea = 25f;

    private const float MaxEdgeLength = 12f;
    private const float MaxSimplificationError = 1.3f;
    private const int MaxVerticesPerPolygon = 6;

    private const float DetailSampleDistance = 6f;
    private const float DetailSampleMaxError = 1f;
}
