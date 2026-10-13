namespace Ubiquitous.Data.Tests;

public class TerrainPackageTests
{
    private static TerrainTile Tile(int tileX, int tileZ, short seed)
    {
        var heights = new short[TerrainPackage.SamplesPerTile];
        for (int i = 0; i < heights.Length; i++) heights[i] = (short)(seed + i);
        return new TerrainTile(tileX, tileZ, heights);
    }

    [Fact]
    public void WriteThenRead_ReturnsEveryTileUnchanged()
    {
        var tiles = new[] { Tile(0, 0, 100), Tile(-11, 7, -2000) };
        using var stream = new MemoryStream();

        TerrainPackage.Write(stream, mapId: 1, tiles);
        stream.Position = 0;
        var read = TerrainPackage.Read(stream);

        Assert.Equal(1, read.MapId);
        Assert.Equal(2, read.Tiles.Count);
        foreach (var expected in tiles)
        {
            var actual = read.Tiles.Single(t => t.TileX == expected.TileX && t.TileZ == expected.TileZ);
            Assert.Equal(expected.Heights, actual.Heights);
        }
    }

    [Fact]
    public void WriteThenRead_SurvivesAMapWithNoTilesAtAll()
    {
        using var stream = new MemoryStream();

        TerrainPackage.Write(stream, mapId: 13, []);
        stream.Position = 0;

        Assert.Empty(TerrainPackage.Read(stream).Tiles);
    }

    [Fact]
    public void Read_RejectsSomethingThatIsNotATerrainFile()
    {
        using var stream = new MemoryStream("not terrain at all"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => TerrainPackage.Read(stream));
    }

    [Fact]
    public void Write_RejectsATileWithTheWrongSampleCount()
    {
        using var stream = new MemoryStream();
        var wrong = new TerrainTile(0, 0, new short[4]);

        Assert.Throws<InvalidDataException>(() => TerrainPackage.Write(stream, mapId: 1, [wrong]));
    }

    [Fact]
    public void TileGeometryHasTheExpectedSampleCountAndSpacing()
    {
        Assert.Equal(64, TerrainPackage.UnitsPerTile);
        Assert.Equal(65, TerrainPackage.SamplesPerSide);
        Assert.Equal(65 * 65, TerrainPackage.SamplesPerTile);
        Assert.Equal(0.01f, TerrainPackage.HeightScale);
    }
}
