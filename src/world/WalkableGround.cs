using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.World;

public interface IWalkableGroundSource
{
    bool HasGround(ushort mapId);

    float? GroundHeightNear(ushort mapId, float x, float z, float nearY);

    bool CanStepStraight(ushort mapId, Position from, Position to);

    IReadOnlyList<Position>? FindRoute(ushort mapId, Position from, Position to);

}

public sealed class NavigationMesh
{
    private float Reach => Parameters.BodyRadius + Parameters.CellSize;

    public const float MaxFall = 4f;

    public const int MaxRoutePolygons = 256;

    private const int MaxRouteCorners = 64;

    private const int MaxPolygonsPerLookup = 64;

    private const float LookupSlack = 0.1f;

    private readonly DtNavMesh _mesh;
    private readonly DtNavMeshQuery _query;
    private readonly IDtQueryFilter _filter = RouteFilter();
    private readonly Dictionary<(int X, int Z), float> _waterBySquare = [];
    private readonly float _waterSquare;
    private readonly long[] _refs;
    private readonly Dictionary<long, int> _index;
    private readonly float _lowest;
    private readonly float _highest;

    public NavigationMesh(NavigationParameters parameters, DtNavMesh mesh, IReadOnlyList<NavigationWater>? water = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(mesh);
        _waterSquare = water is [var first, ..] ? first.Size : 0f;
        foreach (var square in water ?? [])
            _waterBySquare[((int)MathF.Floor(square.MinX / square.Size), (int)MathF.Floor(square.MinZ / square.Size))] = square.Surface;

        Parameters = parameters;
        _mesh = mesh;
        _query = new DtNavMeshQuery(mesh);

        var refs = new List<long>();
        for (int i = 0; i < mesh.GetMaxTiles(); i++)
        {
            var tile = mesh.GetTile(i);
            if (tile?.data?.header is null) continue;
            long baseRef = mesh.GetPolyRefBase(tile);
            for (int poly = 0; poly < tile.data.header.polyCount; poly++)
            {
                refs.Add(baseRef | (long)poly);
            }
        }

        _refs = [.. refs];
        _index = new Dictionary<long, int>(_refs.Length);
        for (int i = 0; i < _refs.Length; i++) _index[_refs[i]] = i;

        mesh.ComputeBounds(out var low, out var high);
        _lowest = low.Y;
        _highest = high.Y;
    }

    public NavigationParameters Parameters { get; }

    public int PolygonTotal => _refs.Length;

    private float[]? _regionAreaOfPolygon;

    // Routes go round water where they can: swim ground costs more to cross than land.
    private static DtQueryDefaultFilter RouteFilter()
    {
        var costs = Enumerable.Repeat(1f, DtDetour.DT_MAX_AREAS).ToArray();
        costs[NavigationAreas.Swim] = NavigationAreas.SwimCost;
        return new DtQueryDefaultFilter(0xffff, 0, costs);
    }

    public float? WaterSurfaceAt(float x, float z) =>
        _waterSquare > 0f && _waterBySquare.TryGetValue(
            ((int)MathF.Floor(x / _waterSquare), (int)MathF.Floor(z / _waterSquare)), out var surface)
            ? surface
            : null;

    // The ground a body could walk to from here, in square units: a pocket cut
    // off by water and a fence is small, the open map is large. Zero off the ground.
    public float ConnectedAreaUnder(float x, float z, float nearY)
    {
        if (Standing(x, z, nearY, Parameters.StepHeight, MaxFall) is not { } standing) return 0f;
        _regionAreaOfPolygon ??= RegionAreas();
        return _regionAreaOfPolygon[_index[standing.Ref]];
    }

    private float[] RegionAreas()
    {
        var region = new int[_refs.Length];
        Array.Fill(region, -1);
        var areas = new List<float>();
        var pending = new Stack<int>();
        for (int start = 0; start < _refs.Length; start++)
        {
            if (region[start] >= 0) continue;
            int id = areas.Count;
            float area = 0f;
            region[start] = id;
            pending.Push(start);
            while (pending.TryPop(out int polygon))
            {
                var (vertices, neighbours) = Polygon(polygon);
                area += FlatArea(vertices);
                foreach (int next in neighbours)
                {
                    if (region[next] >= 0) continue;
                    region[next] = id;
                    pending.Push(next);
                }
            }

            areas.Add(area);
        }

        return [.. region.Select(id => areas[id])];
    }

    private static float FlatArea(IReadOnlyList<Position> vertices)
    {
        float twice = 0f;
        for (int i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];
            twice += (a.X * b.Z) - (b.X * a.Z);
        }

        return MathF.Abs(twice) / 2f;
    }

    public static NavigationMesh FromFile(NavigationPackageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        using var stream = new MemoryStream(file.Mesh, writable: false);
        using var reader = new BinaryReader(stream);
        return new NavigationMesh(file.Parameters, new DtMeshSetReader().Read(reader), file.Water);
    }

    public float? GroundHeightNear(float x, float z, float nearY)
    {
        int index = PolygonNear(x, z, nearY);
        return index < 0 ? null : HeightOf(index, x, z);
    }

    public float? GroundHeightNear(float x, float z, float nearY, float maxDrift) =>
        Standing(x, z, nearY, maxDrift, maxDrift) is { } found ? found.Height : null;

    public float? GroundHeightNear(float x, float z, float nearY, float maxRise, float maxFall) =>
        Standing(x, z, nearY, maxRise, maxFall) is { } found ? found.Height : null;

    public bool CanStepStraight(Position from, Position to)
    {
        if (Standing(from.X, from.Z, from.Y, Parameters.StepHeight, MaxFall) is not { } start) return false;

        Span<long> crossed = stackalloc long[MaxRoutePolygons];
        var status = _query.Raycast(
            start.Ref,
            new RcVec3f(from.X, start.Height, from.Z),
            new RcVec3f(to.X, to.Y, to.Z),
            _filter,
            out float reached,
            out _,
            crossed,
            out _,
            MaxRoutePolygons);

        if (status.Failed()) return false;

        if (reached >= 1f) return true;

        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        float left = MathF.Sqrt((dx * dx) + (dz * dz)) * (1f - reached);
        return left <= PullBack;
    }

    private float PullBack =>
        MathF.Ceiling(Parameters.BodyRadius / Parameters.CellSize) * Parameters.CellSize;

    public IReadOnlyList<Position>? FindRoute(Position from, Position to)
    {
        if (Standing(from.X, from.Z, from.Y, Parameters.StepHeight, MaxFall) is not { } start) return null;
        if (Standing(to.X, to.Z, to.Y, Parameters.StepHeight, MaxFall) is not { } goal) return null;

        var startAt = new RcVec3f(from.X, start.Height, from.Z);
        var goalAt = new RcVec3f(to.X, goal.Height, to.Z);

        Span<long> polygons = stackalloc long[MaxRoutePolygons];
        var status = _query.FindPath(
            start.Ref, goal.Ref, startAt, goalAt, _filter, polygons, out int count, MaxRoutePolygons);
        if (status.Failed() || count == 0) return null;

        Span<DtStraightPath> corners = stackalloc DtStraightPath[MaxRouteCorners];
        status = _query.FindStraightPath(
            startAt, goalAt, polygons, count, corners, out int cornerCount, MaxRouteCorners, 0);
        if (status.Failed() || cornerCount == 0) return null;

        var route = new List<Position>(cornerCount);
        for (int i = 1; i < cornerCount; i++)
        {
            route.Add(new Position(corners[i].pos.X, corners[i].pos.Y, corners[i].pos.Z));
        }

        return route;
    }

    public int PolygonNear(float x, float z, float nearY)
    {
        float reachesEverything = MathF.Max(MathF.Abs(nearY - _lowest), MathF.Abs(nearY - _highest)) + 1f;

        long best = 0;
        float bestDrift = float.MaxValue;
        foreach (var (candidate, height, over) in Candidates(x, z, nearY, reachesEverything, 0f))
        {
            if (!over) continue;
            float drift = MathF.Abs(height - nearY);
            if (drift >= bestDrift) continue;

            best = candidate;
            bestDrift = drift;
        }

        return best == 0 ? -1 : _index[best];
    }

    public int PolygonUnder(float x, float z, float nearY) =>
        Standing(x, z, nearY, Parameters.StepHeight, MaxFall) is { } found ? _index[found.Ref] : -1;

    public float HeightOf(int polygonIndex, float x, float z)
    {
        long polygon = _refs[polygonIndex];
        var at = new RcVec3f(x, 0f, z);
        if (_query.GetPolyHeight(polygon, at, out float height).Succeeded()) return height;

        _mesh.ClosestPointOnPoly(polygon, at, out var closest, out _);
        return closest.Y;
    }

    public (IReadOnlyList<Position> Vertices, IReadOnlyList<int> Neighbours) Polygon(int polygonIndex)
    {
        long polygon = _refs[polygonIndex];
        if (_mesh.GetTileAndPolyByRef(polygon, out var tile, out var poly).Failed()) return ([], []);

        var vertices = new Position[poly.vertCount];
        for (int i = 0; i < poly.vertCount; i++)
        {
            int at = poly.verts[i] * 3;
            vertices[i] = new Position(tile.data.verts[at], tile.data.verts[at + 1], tile.data.verts[at + 2]);
        }

        var neighbours = new List<int>();
        for (int link = poly.firstLink; link != DtDetour.DT_NULL_LINK; link = tile.links[link].next)
        {
            if (_index.TryGetValue(tile.links[link].refs, out int neighbour)) neighbours.Add(neighbour);
        }

        return (vertices, [.. neighbours.Distinct().Order()]);
    }

    private (long Ref, float Height)? Standing(float x, float z, float nearY, float maxRise, float maxFall)
    {
        return Best(0f) ?? Best(Reach);

        (long Ref, float Height)? Best(float reach)
        {
            long under = 0;
            float highestUnder = float.MinValue;
            long over = 0;
            float lowestOver = float.MaxValue;

            foreach (var (candidate, height, inside) in Candidates(
                x, z, nearY, MathF.Max(maxRise, maxFall), reach))
            {
                if (reach <= 0f && !inside) continue;

                float above = height - nearY;
                if (above > 0f)
                {
                    if (above > maxRise || height >= lowestOver) continue;
                    over = candidate;
                    lowestOver = height;
                }
                else
                {
                    if (-above > maxFall || height <= highestUnder) continue;
                    under = candidate;
                    highestUnder = height;
                }
            }

            if (under == 0) return over != 0 ? (over, lowestOver) : null;
            if (over == 0) return (under, highestUnder);

            return lowestOver - nearY < nearY - highestUnder
                ? (over, lowestOver)
                : (under, highestUnder);
        }
    }

    private IEnumerable<(long Ref, float Height, bool Inside)> Candidates(
        float x, float z, float nearY, float halfHeight, float reach)
    {
        var at = new RcVec3f(x, nearY, z);
        var extents = new RcVec3f(reach + LookupSlack, halfHeight + LookupSlack, reach + LookupSlack);
        var found = new long[MaxPolygonsPerLookup];
        if (_query.QueryPolygons(at, extents, _filter, found, out int count, MaxPolygonsPerLookup).Failed())
        {
            yield break;
        }

        for (int i = 0; i < count; i++)
        {
            _mesh.ClosestPointOnPoly(found[i], at, out var closest, out bool inside);
            if (!inside)
            {
                float dx = closest.X - x;
                float dz = closest.Z - z;
                if ((dx * dx) + (dz * dz) > reach * reach) continue;
            }

            yield return (found[i], closest.Y, inside);
        }
    }
}

public sealed class WalkableGroundSource : IWalkableGroundSource
{
    private readonly IReadOnlyDictionary<ushort, NavigationMesh> _byMap;

    public WalkableGroundSource(IReadOnlyDictionary<ushort, NavigationMesh> byMap)
    {
        ArgumentNullException.ThrowIfNull(byMap);
        _byMap = byMap;
    }

    public int MapCount => _byMap.Count;

    public NavigationMesh? ForMap(ushort mapId) => _byMap.TryGetValue(mapId, out var mesh) ? mesh : null;

    public bool HasGround(ushort mapId) => _byMap.ContainsKey(mapId);

    public float? GroundHeightNear(ushort mapId, float x, float z, float nearY) =>
        _byMap.TryGetValue(mapId, out var mesh)
            ? mesh.GroundHeightNear(x, z, nearY, mesh.Parameters.StepHeight, NavigationMesh.MaxFall)
            : null;

    public bool CanStepStraight(ushort mapId, Position from, Position to) =>
        _byMap.TryGetValue(mapId, out var mesh) && mesh.CanStepStraight(from, to);

    public float? WaterSurfaceAt(ushort mapId, float x, float z) =>
        _byMap.TryGetValue(mapId, out var mesh) ? mesh.WaterSurfaceAt(x, z) : null;

    public float ConnectedAreaUnder(ushort mapId, Position at) =>
        _byMap.TryGetValue(mapId, out var mesh) ? mesh.ConnectedAreaUnder(at.X, at.Z, at.Y) : 0f;

    public IReadOnlyList<Position>? FindRoute(ushort mapId, Position from, Position to) =>
        _byMap.TryGetValue(mapId, out var mesh) ? mesh.FindRoute(from, to) : null;

    public static WalkableGroundSource LoadFromDirectory(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var byMap = new Dictionary<ushort, NavigationMesh>();
        if (!Directory.Exists(directory)) return new WalkableGroundSource(byMap);

        foreach (var path in Directory.EnumerateFiles(directory, "*.nav"))
        {
            using var stream = File.OpenRead(path);
            var file = NavigationPackage.Read(stream);
            byMap[file.MapId] = NavigationMesh.FromFile(file);
        }

        return new WalkableGroundSource(byMap);
    }
}
