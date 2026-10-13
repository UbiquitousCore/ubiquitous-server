using System.IO.Compression;

namespace Ubiquitous.Data;

public sealed record TerrainTile(int TileX, int TileZ, short[] Heights);

public sealed record TerrainPackageFile(ushort MapId, IReadOnlyList<TerrainTile> Tiles);

public static class TerrainPackage
{
    public const int UnitsPerTile = 64;

    public const int SamplesPerSide = UnitsPerTile + 1;

    public const int SamplesPerTile = SamplesPerSide * SamplesPerSide;

    public const float HeightScale = 0.01f;

    private static readonly byte[] MagicBytes = "UBQTERR\u0001"u8.ToArray();

    public static string FileName(ushort mapId) => $"{mapId}.terrain";

    public const string DirectoryName = "terrain";

    public static void Write(Stream stream, ushort mapId, IReadOnlyList<TerrainTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(tiles);

        stream.Write(MagicBytes, 0, MagicBytes.Length);
        using var header = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        header.Write(mapId);
        header.Write((ushort)SamplesPerSide);
        header.Write(tiles.Count);
        header.Flush();

        using var deflate = new DeflateStream(stream, CompressionLevel.Optimal, leaveOpen: true);
        using var writer = new BinaryWriter(deflate, System.Text.Encoding.UTF8, leaveOpen: true);
        foreach (var tile in tiles)
        {
            if (tile.Heights.Length != SamplesPerTile)
            {
                throw new InvalidDataException(
                    $"Tile {tile.TileX}_{tile.TileZ} carries {tile.Heights.Length} samples; a tile carries {SamplesPerTile}.");
            }

            writer.Write(tile.TileX);
            writer.Write(tile.TileZ);
            foreach (var sample in tile.Heights) writer.Write(sample);
        }
    }

    public static TerrainPackageFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var magic = new byte[MagicBytes.Length];
        stream.ReadExactly(magic);
        if (!magic.AsSpan().SequenceEqual(MagicBytes))
        {
            throw new InvalidDataException("Not a terrain file, or written by a different format version.");
        }

        ushort mapId;
        int side;
        int tileCount;
        using (var header = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            mapId = header.ReadUInt16();
            side = header.ReadUInt16();
            tileCount = header.ReadInt32();
        }

        if (side != SamplesPerSide)
        {
            throw new InvalidDataException($"Terrain file states {side} samples per side; this build reads {SamplesPerSide}.");
        }

        if (tileCount < 0) throw new InvalidDataException($"Terrain file states {tileCount} tiles.");

        var tiles = new List<TerrainTile>(tileCount);
        using var deflate = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new BinaryReader(deflate, System.Text.Encoding.UTF8, leaveOpen: true);
        for (int i = 0; i < tileCount; i++)
        {
            int tileX = reader.ReadInt32();
            int tileZ = reader.ReadInt32();
            var heights = new short[SamplesPerTile];
            for (int s = 0; s < SamplesPerTile; s++) heights[s] = reader.ReadInt16();
            tiles.Add(new TerrainTile(tileX, tileZ, heights));
        }

        return new TerrainPackageFile(mapId, tiles);
    }
}
