using System.IO.Compression;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class WorldBoundsReaderTests
{
    [Fact]
    public void ComputeBounds_CoversEveryTileFromTheLowestToTheFarEdgeOfTheHighest()
    {
        var names = new[]
        {
            "world/city/terrain/-011_-007.trn",
            "world/city/terrain/0005_0009.trn",
            "world/city/terrain/0000_0000.trn",
            "world/city/items/-011_-007.itm",
        };

        var bounds = WorldBoundsReader.ComputeBounds(names);

        Assert.NotNull(bounds);
        Assert.Equal(-11 * 64, bounds!.Value.MinX);
        Assert.Equal(6 * 64, bounds.Value.MaxX);
        Assert.Equal(-7 * 64, bounds.Value.MinZ);
        Assert.Equal(10 * 64, bounds.Value.MaxZ);
    }

    [Fact]
    public void ComputeBounds_ReturnsNullWhenNoEntryLooksLikeATerrainTile()
    {
        var names = new[] { "world/city.wld", "world/city/items/0000_0000.itm" };

        Assert.Null(WorldBoundsReader.ComputeBounds(names));
    }

    [Fact]
    public void ComputeBounds_HandlesASingleTileMap()
    {
        var names = new[] { "world/tiny/terrain/0000_0000.trn" };

        var bounds = WorldBoundsReader.ComputeBounds(names);

        Assert.Equal(0f, bounds!.Value.MinX);
        Assert.Equal(64f, bounds.Value.MaxX);
        Assert.Equal(0f, bounds.Value.MinZ);
        Assert.Equal(64f, bounds.Value.MaxZ);
    }

    [Fact]
    public void ComputeBoundsFromArchive_ReadsAZipArchive()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("world/city/terrain/-001_-001.trn");
            archive.CreateEntry("world/city/terrain/0002_0003.trn");
            archive.CreateEntry("world/city.wld");
        }

        stream.Position = 0;
        var bounds = WorldBoundsReader.ComputeBoundsFromArchive(stream);

        Assert.NotNull(bounds);
        Assert.Equal(-64f, bounds!.Value.MinX);
        Assert.Equal(192f, bounds.Value.MaxX);
        Assert.Equal(-64f, bounds.Value.MinZ);
        Assert.Equal(256f, bounds.Value.MaxZ);
    }
}
