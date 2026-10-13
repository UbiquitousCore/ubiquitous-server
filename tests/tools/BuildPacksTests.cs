using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public sealed class BuildPacksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ubiquitous-build-packs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void CopyPack_PullsTheNamedPackOutOfTheIpa()
    {
        var build = Path.Combine(_root, "build.ipa");
        Directory.CreateDirectory(_root);
        var tables = PackedArchiveFixture.Build(("tables/creature_proto.tbl", [1, 2, 3]));
        File.WriteAllBytes(build, PackedArchiveFixture.Build(standardSignature: true,
            ("Payload/Game.app/tables.bin", tables),
            ("Payload/Game.app/res.bin", PackedArchiveFixture.Build(("model/scene/house.bdae", [9])))));
        var output = Path.Combine(_root, "incoming");

        Assert.Equal(0, BuildPacks.CopyPack(build, "TABLES.BIN", output, TextWriter.Null));

        Assert.Equal(tables, File.ReadAllBytes(Path.Combine(output, "tables.bin")));
    }

    [Fact]
    public void CopyPack_ReportsAPackTheBuildDoesNotHold()
    {
        var build = Path.Combine(_root, "build.ipa");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(build, PackedArchiveFixture.Build(standardSignature: true,
            ("Payload/Game.app/res.bin", PackedArchiveFixture.Build(("model/scene/house.bdae", [9])))));
        var output = Path.Combine(_root, "incoming");

        Assert.Equal(1, BuildPacks.CopyPack(build, "tables.bin", output, TextWriter.Null));

        Assert.False(File.Exists(Path.Combine(output, "tables.bin")));
    }
}
