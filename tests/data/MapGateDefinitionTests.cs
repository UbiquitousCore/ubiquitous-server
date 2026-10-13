namespace Ubiquitous.Data.Tests;

public sealed class MapGateDefinitionTests
{
    private static readonly MapGateDefinition CityGate = new(
        SourceMapId: 13, SourceMinX: -320, SourceMaxX: -255, SourceMinZ: 250, SourceMaxZ: 304,
        DestinationMapId: 1, DestinationX: -915, DestinationY: 60, DestinationZ: -293, DestinationYaw: 3.14f);

    [Fact]
    public void Crosses_TrueWhenTheFinalPositionLandsInside()
    {
        Assert.True(CityGate.Crosses(-300, 260, -300, 260));
    }

    [Fact]
    public void Crosses_FalseWhenNeitherEndpointNorTheSegmentBetweenThemTouchesTheRectangle()
    {
        Assert.False(CityGate.Crosses(-300, 200, -300, 245));
    }

    [Fact]
    public void Crosses_TrueWhenTheStepStartsBeforeAndLandsAfterTheRectangleOnTheSameAxis()
    {
        Assert.True(CityGate.Crosses(-300, 240, -300, 320));
    }

    [Fact]
    public void Crosses_TrueForADiagonalStepThatOnlyClipsACorner()
    {
        Assert.True(CityGate.Crosses(-265, 245, -245, 255));
    }

    [Fact]
    public void Crosses_FalseForADiagonalStepThatPassesJustOutsideTheCorner()
    {
        Assert.False(CityGate.Crosses(-267, 245, -247, 253));
    }

    [Fact]
    public void Crosses_FalseWhenTheSegmentIsParallelToAnAxisAndOutsideTheOtherRange()
    {
        Assert.False(CityGate.Crosses(-400, 245, -200, 245));
    }
}
