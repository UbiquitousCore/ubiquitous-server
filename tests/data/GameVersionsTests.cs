using Ubiquitous.Data;

namespace Ubiquitous.Data.Tests;

public sealed class GameVersionsTests
{
    [Theory]
    [InlineData("1.1.4", "1.1.10", -1)]
    [InlineData("2.2.0", "1.1.5", 1)]
    [InlineData("4.2.21", "4.2.21", 0)]
    [InlineData("1.1", "1.1.0", 0)]
    public void Compare_OrdersEachNumberInTurn(string a, string b, int expected)
    {
        Assert.Equal(expected, Math.Sign(GameVersions.Compare(a, b)));
    }

    [Theory]
    [InlineData(null, "1.1.4", true)]
    [InlineData("1.1.4", "4.2.21", true)]
    [InlineData("2.2.0", "2.2.0", true)]
    [InlineData("4.2.21", "2.2.0", false)]
    [InlineData("1.1.4", "1.0.3", false)]
    [InlineData("1.1.4", "unknown", true)]
    [InlineData("2.2.0", "unknown", false)]
    [InlineData("2.2.0", "", false)]
    public void MayPlay_OnlyTheCharactersBuildOrALaterOne(string? character, string server, bool expected)
    {
        Assert.Equal(expected, GameVersions.MayPlay(character, server));
    }

    [Theory]
    [InlineData(null, "2.2.0", "2.2.0")]
    [InlineData("1.1.4", "4.2.21", "4.2.21")]
    [InlineData("4.2.21", "4.2.21", "4.2.21")]
    [InlineData(null, "unknown", null)]
    [InlineData("1.1.4", "", "1.1.4")]
    public void Stamp_KeepsTheNewestBuildPlayed(string? character, string server, string? expected)
    {
        Assert.Equal(expected, GameVersions.Stamp(character, server));
    }
}
