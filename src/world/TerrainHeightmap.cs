using Ubiquitous.Data;

namespace Ubiquitous.World;

public interface ITerrainHeightSource
{
    float? HeightAt(ushort mapId, float x, float z);
}

public sealed class TerrainHeightmap
{
    private readonly Dictionary<(int TileX, int TileZ), short[]> _tiles;

    public TerrainHeightmap(IEnumerable<TerrainTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        _tiles = new Dictionary<(int, int), short[]>();
        foreach (var tile in tiles) _tiles[(tile.TileX, tile.TileZ)] = tile.Heights;
    }

    public int TileCount => _tiles.Count;

    public bool Covers(float x, float z) => _tiles.ContainsKey((TileCoord(x), TileCoord(z)));

    public static int TileCoord(float world) =>
        (int)MathF.Floor(world / TerrainPackage.UnitsPerTile);

    public float? HeightAt(float x, float z)
    {
        int tileX = TileCoord(x);
        int tileZ = TileCoord(z);
        if (!_tiles.TryGetValue((tileX, tileZ), out var heights)) return null;

        const int side = TerrainPackage.SamplesPerSide;
        float localX = x - (tileX * TerrainPackage.UnitsPerTile);
        float localZ = z - (tileZ * TerrainPackage.UnitsPerTile);

        int ix = Math.Clamp((int)MathF.Floor(localX), 0, side - 2);
        int iz = Math.Clamp((int)MathF.Floor(localZ), 0, side - 2);
        float dx = Math.Clamp(localX - ix, 0f, 1f);
        float dz = Math.Clamp(localZ - iz, 0f, 1f);

        float Sample(int col, int row) => heights[(row * side) + col] * TerrainPackage.HeightScale;
        float nearX = Sample(ix + 1, iz);
        float nearZ = Sample(ix, iz + 1);

        if (dx + dz <= 1f)
        {
            float origin = Sample(ix, iz);
            return origin + ((nearX - origin) * dx) + ((nearZ - origin) * dz);
        }

        float far = Sample(ix + 1, iz + 1);
        return far + ((nearZ - far) * (1f - dx)) + ((nearX - far) * (1f - dz));
    }
}

public sealed class TerrainHeightSource : ITerrainHeightSource
{
    private readonly IReadOnlyDictionary<ushort, TerrainHeightmap> _byMap;

    public TerrainHeightSource(IReadOnlyDictionary<ushort, TerrainHeightmap> byMap)
    {
        _byMap = byMap ?? throw new ArgumentNullException(nameof(byMap));
    }

    public int MapCount => _byMap.Count;

    public TerrainHeightmap? ForMap(ushort mapId) =>
        _byMap.TryGetValue(mapId, out var map) ? map : null;

    public float? HeightAt(ushort mapId, float x, float z) =>
        _byMap.TryGetValue(mapId, out var map) ? map.HeightAt(x, z) : null;

    public static TerrainHeightSource LoadFromDirectory(string directory)
    {
        var byMap = new Dictionary<ushort, TerrainHeightmap>();
        if (!Directory.Exists(directory)) return new TerrainHeightSource(byMap);

        foreach (var path in Directory.EnumerateFiles(directory, "*.terrain"))
        {
            using var stream = File.OpenRead(path);
            var file = TerrainPackage.Read(stream);
            byMap[file.MapId] = new TerrainHeightmap(file.Tiles);
        }

        return new TerrainHeightSource(byMap);
    }
}
