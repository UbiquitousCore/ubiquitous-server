using Ubiquitous.Data;

namespace Ubiquitous.World.Tests;

public class TerrainHeightmapTests
{
    private const int Side = TerrainPackage.SamplesPerSide;

    private static TerrainTile Flat(int tileX, int tileZ, short height)
    {
        var heights = new short[TerrainPackage.SamplesPerTile];
        Array.Fill(heights, height);
        return new TerrainTile(tileX, tileZ, heights);
    }

    private static TerrainTile Shaped(int tileX, int tileZ, Func<int, int, short> sample)
    {
        var heights = new short[TerrainPackage.SamplesPerTile];
        for (int z = 0; z < Side; z++)
        {
            for (int x = 0; x < Side; x++) heights[(z * Side) + x] = sample(x, z);
        }

        return new TerrainTile(tileX, tileZ, heights);
    }

    [Fact]
    public void HeightAt_ReadsASampleExactlyWhenItLandsOnAGridPoint()
    {
        var terrain = new TerrainHeightmap([Shaped(0, 0, (x, z) => (short)(x == 3 && z == 5 ? 1234 : 0))]);

        Assert.Equal(12.34f, terrain.HeightAt(3, 5)!.Value, precision: 4);
    }

    [Fact]
    public void HeightAt_IsNullWhereNoTileCoversThePosition()
    {
        var terrain = new TerrainHeightmap([Flat(0, 0, 500)]);

        Assert.Null(terrain.HeightAt(TerrainPackage.UnitsPerTile + 1, 0));
        Assert.Null(terrain.HeightAt(0, -1));
        Assert.False(terrain.Covers(-1, -1));
    }

    [Fact]
    public void HeightAt_PlacesNegativeCoordinatesInTheRightTile()
    {
        var terrain = new TerrainHeightmap(
            [Shaped(-1, -1, (x, z) => (short)(x == 63 && z == 63 ? 777 : 0))]);

        Assert.Equal(7.77f, terrain.HeightAt(-1, -1)!.Value, precision: 4);
    }

    [Fact]
    public void HeightAt_InterpolatesAcrossASlope()
    {
        var terrain = new TerrainHeightmap([Shaped(0, 0, (x, _) => (short)(x * 100))]);

        Assert.Equal(10.5f, terrain.HeightAt(10.5f, 4)!.Value, precision: 4);
        Assert.Equal(0.25f, terrain.HeightAt(0.25f, 0)!.Value, precision: 4);
    }

    [Fact]
    public void HeightAt_SplitsEachCellOnTheSameDiagonalAsTheClient()
    {
        var terrain = new TerrainHeightmap([Shaped(0, 0, (x, z) =>
            (short)((x + z) % 2 == 0 ? 0 : 1000))]);

        Assert.Equal(10f, terrain.HeightAt(0.5f, 0.5f)!.Value, precision: 4);

        Assert.Equal(5f, terrain.HeightAt(0.25f, 0.25f)!.Value, precision: 4);
        Assert.Equal(5f, terrain.HeightAt(0.75f, 0.75f)!.Value, precision: 4);

        Assert.Equal(10f, terrain.HeightAt(1f, 0f)!.Value, precision: 4);
        Assert.Equal(0f, terrain.HeightAt(1f, 1f)!.Value, precision: 4);
    }

    [Fact]
    public void HeightAt_InterpolatesTheFinalCellAtATileFarEdge()
    {
        var terrain = new TerrainHeightmap([Shaped(0, 0, (x, _) => (short)(x * 100))]);

        Assert.Equal(63.5f, terrain.HeightAt(63.5f, 10)!.Value, precision: 3);
        Assert.Equal(64f, terrain.HeightAt(63.999f, 10)!.Value, precision: 1);
    }

    [Fact]
    public void HeightSource_AnswersPerMapAndIsNullForAMapWithNoTerrain()
    {
        var source = new TerrainHeightSource(new Dictionary<ushort, TerrainHeightmap>
        {
            [1] = new TerrainHeightmap([Flat(0, 0, 620)]),
        });

        Assert.Equal(6.2f, source.HeightAt(1, 10, 10)!.Value, precision: 3);
        Assert.Null(source.HeightAt(4, 10, 10));
        Assert.Equal(1, source.MapCount);
        Assert.NotNull(source.ForMap(1));
        Assert.Null(source.ForMap(4));
    }
}
