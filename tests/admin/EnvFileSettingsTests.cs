using Ubiquitous.Admin.Stores;

namespace Ubiquitous.Admin.Tests;

public sealed class EnvFileSettingsTests
{
    [Fact]
    public void AValueLandsOnTheCommentedLineShowingItAndEverythingElseStays()
    {
        var text = "# Bots.\n# UBIQUITOUS_XP_RATE=1\nPOSTGRES_PASSWORD=keep\n";

        var written = EnvFileSettings.Apply(text, new Dictionary<string, string?> { ["UBIQUITOUS_XP_RATE"] = "2" });

        Assert.Equal("# Bots.\nUBIQUITOUS_XP_RATE=2\nPOSTGRES_PASSWORD=keep\n", written);
    }

    [Fact]
    public void ANullRemovesTheSettingSoItsDefaultApplies()
    {
        var written = EnvFileSettings.Apply("A=1\nB=2\n", new Dictionary<string, string?> { ["A"] = null, ["C"] = null });

        Assert.Equal("B=2\n", written);
    }

    [Fact]
    public void ANewSettingGoesAtTheEndAndWindowsLineEndingsSurvive()
    {
        var written = EnvFileSettings.Apply("A=1\r\n", new Dictionary<string, string?> { ["B"] = "x" });

        Assert.Equal("A=1\r\nB=x\r\n", written);
    }
}
