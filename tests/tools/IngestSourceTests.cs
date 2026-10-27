using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public sealed class IngestSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ubiquitous-ingest-source-" + Guid.NewGuid().ToString("N"));

    public IngestSourceTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }

    [Fact]
    public void AFolderWithAnIpa_IsAWholeBuild()
    {
        var ipa = Touch("Game-1.1.4.IPA");

        var source = IngestSource.Resolve(_root)!;

        Assert.Equal(ipa, source.Build);
        Assert.True(source.FullBuild);
        Assert.Null(source.Tables);
    }

    [Fact]
    public void AFolderOfUnpackedPacks_BuildsFromThePacksFolder()
    {
        Touch("app", "packs", "res.bin");
        var tables = Touch("app", "packs", "tables.bin");

        var source = IngestSource.Resolve(_root)!;

        Assert.Equal(Path.Combine(_root, "app", "packs"), source.Build);
        Assert.Equal(tables, source.Tables);
        Assert.True(source.FullBuild);
    }

    [Fact]
    public void TablesAlone_IsNotAWholeBuild()
    {
        var tables = Touch("tables.bin");

        var source = IngestSource.Resolve(tables)!;

        Assert.Equal(tables, source.Tables);
        Assert.False(source.FullBuild);
    }

    [Fact]
    public void TablesBesideAnIpa_TakesTheBuildFromTheIpa()
    {
        var tables = Touch("tables.bin");
        var ipa = Touch("game.ipa");

        var source = IngestSource.Resolve(tables)!;

        Assert.Equal(ipa, source.Build);
        Assert.True(source.FullBuild);
    }

    [Fact]
    public void PacksDeeperThanThreeLevels_AreNotFound()
    {
        Touch("a", "b", "c", "tables.bin");

        Assert.Null(IngestSource.Resolve(_root)!.Tables);
    }

    [Fact]
    public void NothingThere_IsNoSource() =>
        Assert.Null(IngestSource.Resolve(Path.Combine(_root, "missing")));
}
