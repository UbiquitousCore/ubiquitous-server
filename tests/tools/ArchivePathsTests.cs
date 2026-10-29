using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public sealed class ArchivePathsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ubiquitous-archive-paths");

    [Theory]
    [InlineData("model/scene/house.phy")]
    [InlineData("model\\scene\\house.phy")]
    [InlineData("model/./scene/house.phy")]
    public void AMemberInsideTheDirectoryIsWrittenThere(string name)
    {
        Assert.Equal(
            Path.Combine(Path.GetFullPath(Root), "model", "scene", "house.phy"),
            ArchivePaths.Inside(Root, name));
    }

    [Theory]
    [InlineData("../escape.phy")]
    [InlineData("model/../../escape.phy")]
    [InlineData("/home/someone/.bashrc")]
    [InlineData("")]
    public void AMemberNamedOutsideTheDirectoryHasNoDestination(string name)
    {
        Assert.Null(ArchivePaths.Inside(Root, name));
    }

    [Fact]
    public void ANameThatOnlySharesTheDirectorysPrefixIsOutsideIt()
    {
        Assert.Null(ArchivePaths.Inside(Root, "../ubiquitous-archive-paths-other/x.phy"));
    }

    [Fact]
    public void OneBadNameRefusesTheWholeArchive()
    {
        var (destinations, refused) = ArchivePaths.AllInside(Root, ["a.tbl", "../b.tbl", "c.tbl"]);
        Assert.Null(destinations);
        Assert.Equal("../b.tbl", refused);
    }
}
