using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed class WorldArchiveCollisionSource : ICollisionSource
{
    private readonly List<Placed> _placed = [];
    private readonly List<NavigationLink> _links = [];
    private readonly SortedSet<string> _unresolved = new(StringComparer.OrdinalIgnoreCase);

    public WorldArchiveCollisionSource(
        IEnumerable<WorldItemTile> tiles, Func<string, IReadOnlyList<BakeTriangle>?> collisionOf)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(collisionOf);

        var bounds = new Dictionary<IReadOnlyList<BakeTriangle>, ModelBounds>();
        var ways = new Dictionary<string, NavigationLink?>(StringComparer.Ordinal);
        foreach (var tile in tiles)
        {
            float originX = tile.TileX * TerrainPackage.UnitsPerTile;
            float originZ = tile.TileZ * TerrainPackage.UnitsPerTile;
            foreach (var placement in tile.Placements)
            {
                Placements++;
                if (!Transform.TryBuild(placement, originX, originZ, out var transform)) continue;

                if (Add(placement.Model, collisionOf(placement.Model), transform, bounds, ways)) Resolved++;
                else _unresolved.Add(placement.Model);

                var interior = InteriorOf(placement.Model);
                if (interior is not null && Add(interior, collisionOf(interior), transform, bounds, ways)) Interiors++;
            }
        }
    }

    public int Placements { get; }

    public int Resolved { get; }

    public int Interiors { get; }

    public IReadOnlyCollection<string> UnresolvedModels => _unresolved;

    public static string? InteriorOf(string model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!model.EndsWith(".bdae", StringComparison.OrdinalIgnoreCase)) return null;
        if (model.EndsWith("_inside.bdae", StringComparison.OrdinalIgnoreCase)) return null;

        return string.Concat(model.AsSpan(0, model.Length - 5), "_inside.bdae");
    }

    public IEnumerable<(int X, int Z)> TilesOf(float tileSize)
    {
        var tiles = new HashSet<(int X, int Z)>();
        foreach (var placed in _placed)
        {
            int fromX = (int)MathF.Floor(placed.MinX / tileSize);
            int toX = (int)MathF.Floor(placed.MaxX / tileSize);
            int fromZ = (int)MathF.Floor(placed.MinZ / tileSize);
            int toZ = (int)MathF.Floor(placed.MaxZ / tileSize);
            for (int z = fromZ; z <= toZ; z++)
            {
                for (int x = fromX; x <= toX; x++) tiles.Add((x, z));
            }
        }

        return tiles;
    }

    public IReadOnlyList<BakeTriangle> TrianglesIn(float minX, float minZ, float maxX, float maxZ)
    {
        var triangles = new List<BakeTriangle>();
        foreach (var placed in _placed)
        {
            if (placed.MaxX < minX || placed.MinX > maxX || placed.MaxZ < minZ || placed.MinZ > maxZ) continue;

            foreach (var triangle in placed.Model)
            {
                var a = placed.Transform.Apply(triangle.A);
                var b = placed.Transform.Apply(triangle.B);
                var c = placed.Transform.Apply(triangle.C);

                triangles.Add(new BakeTriangle(a, b, c));
            }
        }

        return triangles;
    }

    private bool Add(
        string name,
        IReadOnlyList<BakeTriangle>? model,
        Transform transform,
        Dictionary<IReadOnlyList<BakeTriangle>, ModelBounds> bounds,
        Dictionary<string, NavigationLink?> ways)
    {
        if (model is null || model.Count == 0) return false;

        if (!bounds.TryGetValue(model, out var box))
        {
            box = ModelBounds.Of(model);
            bounds[model] = box;
        }

        var placed = box.PlacedBy(transform);
        _placed.Add(new Placed(model, transform, placed.MinX, placed.MaxX, placed.MinZ, placed.MaxZ));

        if (!ways.TryGetValue(name, out var way))
        {
            way = StaircaseLinks.Of(name, model, NavigationBaker.DefaultParameters);
            ways[name] = way;
        }

        if (way is { } steps)
        {
            _links.Add(new NavigationLink(transform.Apply(steps.From), transform.Apply(steps.To)));
        }

        return true;
    }

    public IEnumerable<NavigationLink> LinksIn(float minX, float minZ, float maxX, float maxZ)
    {
        foreach (var link in _links)
        {
            if (link.From.X < minX || link.From.X >= maxX) continue;
            if (link.From.Z < minZ || link.From.Z >= maxZ) continue;
            yield return link;
        }
    }

    private readonly record struct ModelBounds(
        float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ)
    {
        public static ModelBounds Of(IReadOnlyList<BakeTriangle> model)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var triangle in model)
            {
                Widen(triangle.A);
                Widen(triangle.B);
                Widen(triangle.C);
            }

            return new ModelBounds(minX, maxX, minY, maxY, minZ, maxZ);

            void Widen(Position vertex)
            {
                minX = MathF.Min(minX, vertex.X);
                maxX = MathF.Max(maxX, vertex.X);
                minY = MathF.Min(minY, vertex.Y);
                maxY = MathF.Max(maxY, vertex.Y);
                minZ = MathF.Min(minZ, vertex.Z);
                maxZ = MathF.Max(maxZ, vertex.Z);
            }
        }

        public (float MinX, float MaxX, float MinZ, float MaxZ) PlacedBy(Transform transform)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int corner = 0; corner < 8; corner++)
            {
                var placed = transform.Apply(new Position(
                    (corner & 1) == 0 ? MinX : MaxX,
                    (corner & 2) == 0 ? MinY : MaxY,
                    (corner & 4) == 0 ? MinZ : MaxZ));
                minX = MathF.Min(minX, placed.X);
                maxX = MathF.Max(maxX, placed.X);
                minZ = MathF.Min(minZ, placed.Z);
                maxZ = MathF.Max(maxZ, placed.Z);
            }

            return (minX, maxX, minZ, maxZ);
        }
    }

    private readonly record struct Transform(
        Position Position, PlacementScale Scale, float TurnX, float TurnY, float TurnZ, float TurnW)
    {
        public static bool TryBuild(
            WorldItemPlacement placement, float originX, float originZ, out Transform transform)
        {
            transform = default;
            var at = new Position(
                originX + placement.Position.X, placement.Position.Y, originZ + placement.Position.Z);
            if (!IsUsable(at.X) || !IsUsable(at.Y) || !IsUsable(at.Z)) return false;

            var scale = new PlacementScale(
                Sized(placement.Scale.X), Sized(placement.Scale.Y), Sized(placement.Scale.Z));

            var turn = placement.Turn;
            float length = MathF.Sqrt((turn.X * turn.X) + (turn.Y * turn.Y) + (turn.Z * turn.Z) + (turn.W * turn.W));
            if (!float.IsFinite(length) || length < 1e-9f)
            {
                transform = new Transform(at, scale, 0f, 0f, 0f, 1f);
                return true;
            }

            transform = new Transform(
                at, scale, -turn.X / length, -turn.Y / length, -turn.Z / length, turn.W / length);
            return true;
        }

        public Position Apply(Position local)
        {
            float x = local.X * Scale.X;
            float y = local.Y * Scale.Y;
            float z = local.Z * Scale.Z;

            float cx = (2f * TurnY * z) - (2f * TurnZ * y);
            float cy = (2f * TurnZ * x) - (2f * TurnX * z);
            float cz = (2f * TurnX * y) - (2f * TurnY * x);

            return new Position(
                Position.X + x + (TurnW * cx) + (TurnY * cz) - (TurnZ * cy),
                Position.Y + y + (TurnW * cy) + (TurnZ * cx) - (TurnX * cz),
                Position.Z + z + (TurnW * cz) + (TurnX * cy) - (TurnY * cx));
        }

        private static bool IsUsable(float value) => float.IsFinite(value);

        private static float Sized(float factor) => float.IsFinite(factor) && factor != 0f ? factor : 1f;
    }

    private sealed record Placed(
        IReadOnlyList<BakeTriangle> Model, Transform Transform, float MinX, float MaxX, float MinZ, float MaxZ);
}
