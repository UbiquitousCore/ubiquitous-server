using Ubiquitous.Data;

namespace Ubiquitous.Data.Tests;

public sealed class GameVersionsTests
{
    [Theory]
    [InlineData(null, "1.1.4", true)]
    [InlineData("2.2.0", "2.2.0", true)]
    [InlineData("1.1.4", "4.2.21", false)]
    [InlineData("4.2.21", "2.2.0", false)]
    [InlineData("1.1.4", "1.0.3", false)]
    [InlineData("1.1.4", "unknown", true)]
    [InlineData("2.2.0", "unknown", false)]
    [InlineData("1.1.4", "", true)]
    public void MayPlay_OnlyTheBuildTheCharacterWasMadeOn(string? character, string server, bool expected)
    {
        Assert.Equal(expected, GameVersions.MayPlay(character, server));
    }

    [Theory]
    [InlineData(null, "2.2.0", "2.2.0")]
    [InlineData("1.1.4", "4.2.21", "1.1.4")]
    [InlineData(null, "unknown", null)]
    [InlineData(null, "", null)]
    public void Stamp_KeepsTheBuildItWasMadeOn(string? character, string server, string? expected)
    {
        Assert.Equal(expected, GameVersions.Stamp(character, server));
    }
}
