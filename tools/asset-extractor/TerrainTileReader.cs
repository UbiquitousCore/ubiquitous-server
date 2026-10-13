using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public static class TerrainTileReader
{
    private const int TileHeaderBytes = 24;
    private const int ChunkHeaderBytes = 12;
    private const int ChunksPerTile = 64;
    private const int HeightOffset = TileHeaderBytes + (ChunkHeaderBytes * ChunksPerTile);

    private const short EmptyTileFill = -1000;

    // Chunk header: flags (bit 16 water), then the water surface in hundredths at byte 4.
    private const uint WaterFlag = 1u << 16;
    private const int WaterLevelOffset = 4;
    private const int ChunksPerSide = 8;

    private static readonly byte[] Magic = "ATIL"u8.ToArray();

    private static readonly Regex TilePattern =
        new(@"terrain/(-?\d+)_(-?\d+)\.trn$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<TerrainTile> ReadTiles(Stream zipStream)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        using var archive = PackedArchive.Open(zipStream);

        var tiles = new List<TerrainTile>();
        foreach (var name in archive.Names)
        {
            var match = TilePattern.Match(name);
            if (!match.Success) continue;

            int tileX = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int tileZ = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

            var tile = ReadTile(archive.Read(name), tileX, tileZ);
            if (tile is not null) tiles.Add(tile);
        }

        return tiles;
    }

    public static TerrainTile? ReadTile(ReadOnlySpan<byte> data, int tileX, int tileZ)
    {
        if (data.Length < HeightOffset + (TerrainPackage.SamplesPerTile * 2)) return null;
        if (!data[..4].SequenceEqual(Magic)) return null;

        var heights = new short[TerrainPackage.SamplesPerTile];
        bool empty = true;
        for (int i = 0; i < heights.Length; i++)
        {
            short sample = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(HeightOffset + (i * 2), 2));
            heights[i] = sample;
            if (sample != EmptyTileFill) empty = false;
        }

        return empty ? null : new TerrainTile(tileX, tileZ, heights);
    }

    // The water a tile holds, one square per chunk. A chunk's stored level below its own
    // lowest ground is a placeholder the client does not draw, so it is not water.
    public static IReadOnlyList<NavigationWater> ReadWater(ReadOnlySpan<byte> data, int tileX, int tileZ)
    {
        if (data.Length < HeightOffset + (TerrainPackage.SamplesPerTile * 2)) return [];
        if (!data[..4].SequenceEqual(Magic)) return [];

        const int units = TerrainPackage.UnitsPerTile;
        const int side = units / ChunksPerSide;
        var water = new List<NavigationWater>();
        for (int chunk = 0; chunk < ChunksPerTile; chunk++)
        {
            var header = data.Slice(TileHeaderBytes + (chunk * ChunkHeaderBytes), ChunkHeaderBytes);
            if ((BinaryPrimitives.ReadUInt32LittleEndian(header) & WaterFlag) == 0) continue;
            float surface = BinaryPrimitives.ReadInt16LittleEndian(header[WaterLevelOffset..]) * TerrainPackage.HeightScale;

            int cx = chunk % ChunksPerSide;
            int cz = chunk / ChunksPerSide;
            float lowest = float.MaxValue;
            for (int z = cz * side; z <= (cz + 1) * side; z++)
            {
                for (int x = cx * side; x <= (cx + 1) * side; x++)
                {
                    short sample = BinaryPrimitives.ReadInt16LittleEndian(
                        data.Slice(HeightOffset + (((z * TerrainPackage.SamplesPerSide) + x) * 2), 2));
                    lowest = MathF.Min(lowest, sample * TerrainPackage.HeightScale);
                }
            }

            if (surface > lowest)
                water.Add(new NavigationWater((tileX * units) + (cx * side), (tileZ * units) + (cz * side), side, surface));
        }

        return water;
    }

    public static IReadOnlyList<NavigationWater> ReadWater(PackedArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var water = new List<NavigationWater>();
        foreach (var name in archive.Names)
        {
            var match = TilePattern.Match(name);
            if (!match.Success) continue;
            water.AddRange(ReadWater(archive.Read(name),
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)));
        }

        return water;
    }
}
