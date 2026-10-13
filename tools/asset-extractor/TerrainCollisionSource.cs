using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed class TerrainCollisionSource : ICollisionSource
{
    private readonly Dictionary<(int X, int Z), short[]> _tiles = [];

    public TerrainCollisionSource(IEnumerable<TerrainTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        foreach (var tile in tiles) _tiles[(tile.TileX, tile.TileZ)] = tile.Heights;
    }

    public IEnumerable<(int X, int Z)> TilesOf(float tileSize)
    {
        var bakeTiles = new HashSet<(int X, int Z)>();
        foreach (var (tileX, tileZ) in _tiles.Keys)
        {
            float minX = tileX * TerrainPackage.UnitsPerTile;
            float minZ = tileZ * TerrainPackage.UnitsPerTile;
            int fromX = (int)MathF.Floor(minX / tileSize);
            int toX = (int)MathF.Floor((minX + TerrainPackage.UnitsPerTile - 0.001f) / tileSize);
            int fromZ = (int)MathF.Floor(minZ / tileSize);
            int toZ = (int)MathF.Floor((minZ + TerrainPackage.UnitsPerTile - 0.001f) / tileSize);
            for (int z = fromZ; z <= toZ; z++)
            {
                for (int x = fromX; x <= toX; x++) bakeTiles.Add((x, z));
            }
        }

        return bakeTiles;
    }

    public IReadOnlyList<BakeTriangle> TrianglesIn(float minX, float minZ, float maxX, float maxZ)
    {
        const int units = TerrainPackage.UnitsPerTile;
        var triangles = new List<BakeTriangle>();

        int fromTileX = (int)MathF.Floor(minX / units);
        int toTileX = (int)MathF.Floor(maxX / units);
        int fromTileZ = (int)MathF.Floor(minZ / units);
        int toTileZ = (int)MathF.Floor(maxZ / units);

        for (int tileZ = fromTileZ; tileZ <= toTileZ; tileZ++)
        {
            for (int tileX = fromTileX; tileX <= toTileX; tileX++)
            {
                if (!_tiles.TryGetValue((tileX, tileZ), out var heights)) continue;

                float originX = tileX * units;
                float originZ = tileZ * units;
                int firstX = Math.Clamp((int)MathF.Floor(minX - originX), 0, units - 1);
                int lastX = Math.Clamp((int)MathF.Ceiling(maxX - originX), 0, units - 1);
                int firstZ = Math.Clamp((int)MathF.Floor(minZ - originZ), 0, units - 1);
                int lastZ = Math.Clamp((int)MathF.Ceiling(maxZ - originZ), 0, units - 1);

                for (int z = firstZ; z <= lastZ; z++)
                {
                    for (int x = firstX; x <= lastX; x++)
                    {
                        var a = Sample(heights, originX, originZ, x, z);
                        var b = Sample(heights, originX, originZ, x + 1, z);
                        var c = Sample(heights, originX, originZ, x, z + 1);
                        var d = Sample(heights, originX, originZ, x + 1, z + 1);

                        triangles.Add(new BakeTriangle(a, d, b));
                        triangles.Add(new BakeTriangle(a, c, d));
                    }
                }
            }
        }

        return triangles;
    }

    private static Position Sample(short[] heights, float originX, float originZ, int x, int z) =>
        new(
            originX + x,
            heights[(z * TerrainPackage.SamplesPerSide) + x] * TerrainPackage.HeightScale,
            originZ + z);
}
