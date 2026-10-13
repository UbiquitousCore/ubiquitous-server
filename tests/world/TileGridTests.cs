using Ubiquitous.Common;

namespace Ubiquitous.World.Tests;

public sealed class TileGridTests
{
    [Fact]
    public void TileOf_FloorsBothWays()
    {
        Assert.Equal((0, 0), TileGrid.TileOf(1, 3));
        Assert.Equal((1, 0), TileGrid.TileOf(30, 3));
        Assert.Equal((-1, -1), TileGrid.TileOf(-1, -1));
        Assert.Equal((-2, -1), TileGrid.TileOf(-17, -1));
    }

    [Fact]
    public void IsActive_CoversTwoTilesOut()
    {
        var player = new Position(1, 2, 3);

        Assert.True(TileGrid.IsActive(new Position(47, 2, 3), player));
        Assert.False(TileGrid.IsActive(new Position(48, 2, 3), player));
        Assert.False(TileGrid.IsActive(new Position(1, 2, 51), player));
    }

    [Fact]
    public void Cadence_HoldsDocumentedShape()
    {
        Assert.Equal(16f, TileGrid.TileSize);
        Assert.Equal(2, TileGrid.ActivationRadiusTiles);
        Assert.Equal(TimeSpan.FromMinutes(3), TileGrid.SleepGrace);
    }
}
