using Ubiquitous.Common;

namespace Ubiquitous.AssetExtractor;

public sealed record CollisionPlacement(string Model, Position Position, float Yaw, float Scale);

public sealed class StaticObjectCollisionSource : ICollisionSource
{
    private readonly List<Placed> _placed = [];
    private readonly List<NavigationLink> _links = [];
    private readonly SortedSet<string> _unresolvedModels = new(StringComparer.Ordinal);

    public StaticObjectCollisionSource(
        IEnumerable<CollisionPlacement> placements, Func<string, IReadOnlyList<BakeTriangle>?> collisionOf)
    {
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(collisionOf);

        var ways = new Dictionary<string, NavigationLink?>(StringComparer.Ordinal);
        foreach (var placement in placements)
        {
            var model = collisionOf(placement.Model);
            if (model is null || model.Count == 0)
            {
                Unresolved++;
                _unresolvedModels.Add(placement.Model);
                continue;
            }

            var triangles = new BakeTriangle[model.Count];
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < model.Count; i++)
            {
                var a = ToWorld(model[i].A, placement);
                var b = ToWorld(model[i].B, placement);
                var c = ToWorld(model[i].C, placement);
                triangles[i] = new BakeTriangle(a, b, c);
                minX = MathF.Min(minX, MathF.Min(a.X, MathF.Min(b.X, c.X)));
                maxX = MathF.Max(maxX, MathF.Max(a.X, MathF.Max(b.X, c.X)));
                minZ = MathF.Min(minZ, MathF.Min(a.Z, MathF.Min(b.Z, c.Z)));
                maxZ = MathF.Max(maxZ, MathF.Max(a.Z, MathF.Max(b.Z, c.Z)));
            }

            _placed.Add(new Placed(triangles, minX, maxX, minZ, maxZ));

            if (!ways.TryGetValue(placement.Model, out var way))
            {
                way = StaircaseLinks.Of(placement.Model, model, NavigationBaker.DefaultParameters);
                ways[placement.Model] = way;
            }

            if (way is { } steps)
            {
                _links.Add(new NavigationLink(
                    ToWorld(steps.From, placement), ToWorld(steps.To, placement)));
            }
        }
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

    public int Unresolved { get; }

    public IReadOnlyCollection<string> UnresolvedModels => _unresolvedModels;

    public int Placements => _placed.Count;

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

            triangles.AddRange(placed.Triangles);
        }

        return triangles;
    }

    private static Position ToWorld(Position local, CollisionPlacement placement)
    {
        float sin = MathF.Sin(placement.Yaw);
        float cos = MathF.Cos(placement.Yaw);
        float x = local.X * placement.Scale;
        float y = local.Y * placement.Scale;
        float z = local.Z * placement.Scale;
        return new Position(
            placement.Position.X + (x * cos) + (z * sin),
            placement.Position.Y + y,
            placement.Position.Z + (z * cos) - (x * sin));
    }

    private sealed record Placed(BakeTriangle[] Triangles, float MinX, float MaxX, float MinZ, float MaxZ);
}
