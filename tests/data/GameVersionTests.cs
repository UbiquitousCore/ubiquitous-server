
namespace Ubiquitous.Data.Tests;

public class GameVersionTests
{
    [Fact]
    public void TargetGameVersion_Is114()
    {
        Assert.Equal("1.1.4", DataManifest.TargetGameVersion);
    }

    [Theory]
    [InlineData("1.1.4", true)]
    [InlineData("2.2.0", false)]
    [InlineData("1.0.2", false)]
    [InlineData("synthetic", false)]
    [InlineData(null, false)]
    public void IsTargetGameVersion_MatchesTargetOnly(string? version, bool expected)
    {
        Assert.Equal(expected, DataManifest.IsTargetGameVersion(version));
    }
}
