using System.Buffers.Binary;
using System.IO.Compression;
using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

public class TerrainTileReaderTests
{
    private const int HeightOffset = 24 + (12 * 64);

    private static byte[] TileBytes(Func<int, int, short> sample, uint version = 9)
    {
        var bytes = new byte[HeightOffset + (TerrainPackage.SamplesPerTile * 2)];
        "ATIL"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), version);
        for (int z = 0; z < TerrainPackage.SamplesPerSide; z++)
        {
            for (int x = 0; x < TerrainPackage.SamplesPerSide; x++)
            {
                int index = (z * TerrainPackage.SamplesPerSide) + x;
                BinaryPrimitives.WriteInt16LittleEndian(
                    bytes.AsSpan(HeightOffset + (index * 2)), sample(x, z));
            }
        }

        return bytes;
    }

    private static MemoryStream Archive(params (string Name, byte[] Bytes)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(bytes);
            }
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void ReadTiles_TakesTheHeightsAndTheGridIndicesFromTheEntryName()
    {
        using var archive = Archive(
            ("world/city/terrain/0003_-002.trn", TileBytes((x, z) => (short)((x * 10) + z))),
            ("world/city/items/0003_-002.itm", [1, 2, 3]),
            ("world/city.wld", [4, 5, 6]));

        var tiles = TerrainTileReader.ReadTiles(archive);

        var tile = Assert.Single(tiles);
        Assert.Equal(3, tile.TileX);
        Assert.Equal(-2, tile.TileZ);
        Assert.Equal(TerrainPackage.SamplesPerTile, tile.Heights.Length);
        Assert.Equal(74, tile.Heights[(4 * TerrainPackage.SamplesPerSide) + 7]);
    }

    [Fact]
    public void ReadTiles_ReadsAWorldArchiveWithTheClientsOwnHeaderSignature()
    {
        var bytes = PackedArchiveFixture.Build(
            ("world/city/terrain/0001_0002.trn", TileBytes((_, _) => 250)));

        var tile = Assert.Single(TerrainTileReader.ReadTiles(new MemoryStream(bytes)));
        Assert.Equal(1, tile.TileX);
        Assert.Equal(2, tile.TileZ);
        Assert.Equal(250, tile.Heights[0]);
    }

    [Fact]
    public void ReadTiles_AcceptsATileWhateverItsVersion()
    {
        using var archive = Archive(("world/city/terrain/0000_0000.trn", TileBytes((_, _) => 500, version: 10)));

        Assert.Single(TerrainTileReader.ReadTiles(archive));
    }

    [Fact]
    public void ReadTiles_DropsAnUnbuiltTileButKeepsBuiltGroundAtMinusTen()
    {
        using var empty = Archive(("world/city/terrain/0000_0000.trn", TileBytes((_, _) => -1000)));
        Assert.Empty(TerrainTileReader.ReadTiles(empty));

        using var built = Archive(("world/city/terrain/0000_0000.trn",
            TileBytes((x, z) => (short)(x == 0 && z == 0 ? -999 : -1000))));
        var tile = Assert.Single(TerrainTileReader.ReadTiles(built));
        Assert.Equal(-1000, tile.Heights[TerrainPackage.SamplesPerSide + 1]);
    }

    [Fact]
    public void ReadTile_RejectsBytesThatAreNotATileFile()
    {
        Assert.Null(TerrainTileReader.ReadTile(new byte[HeightOffset + 8450], 0, 0));
        Assert.Null(TerrainTileReader.ReadTile("ATIL"u8.ToArray(), 0, 0));
    }

    private static void Water(byte[] tile, int chunk, short surfaceHundredths)
    {
        var header = tile.AsSpan(24 + (chunk * 12), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(header, 1u << 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[4..], surfaceHundredths);
    }

    [Fact]
    public void ReadWater_GivesEachFlaggedChunkAsASquareAtItsSurface()
    {
        var tile = TileBytes((_, _) => 0);
        Water(tile, chunk: 9, surfaceHundredths: 250);

        var water = Assert.Single(TerrainTileReader.ReadWater(tile, tileX: 2, tileZ: -1));

        Assert.Equal(new NavigationWater((2 * 64) + 8, -64 + 8, 8, 2.5f), water);
    }

    [Fact]
    public void ReadWater_SkipsALevelLyingBelowTheChunksGround()
    {
        var tile = TileBytes((_, _) => 500);
        Water(tile, chunk: 0, surfaceHundredths: 250);

        Assert.Empty(TerrainTileReader.ReadWater(tile, 0, 0));
    }
}
