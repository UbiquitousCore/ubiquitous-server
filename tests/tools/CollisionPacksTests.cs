using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public sealed class CollisionPacksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ubiquitous-collision-packs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ABuildsBestPackHoldsAModelAndOtherFilesAreNotCollision()
    {
        var build = Path.Combine(_root, "build.ipa");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(build, PackedArchiveFixture.Build(standardSignature: true,
            ("Payload/Game.app/res.bin", PackedArchiveFixture.Build(
                ("model/scene/house.phy", [1]), ("model/scene/rock.PHY", [2]), ("model/scene/house.bdae", [9]))),
            ("Payload/Game.app/model_phy.bin", PackedArchiveFixture.Build(("model/scene/house.phy", [3])))));
        var output = Path.Combine(_root, "raw");

        Assert.Equal(0, CollisionPacks.Extract(build, output, TextWriter.Null));

        Assert.Equal([3], File.ReadAllBytes(Path.Combine(output, "model", "scene", "house.phy")));
        Assert.Equal([2], File.ReadAllBytes(Path.Combine(output, "model", "scene", "rock.PHY")));
        Assert.False(File.Exists(Path.Combine(output, "model", "scene", "house.bdae")));
    }

    [Fact]
    public void AnotherBuildFillsOnlyTheModelsAlreadyMissing()
    {
        var packs = Path.Combine(_root, "packs");
        Directory.CreateDirectory(packs);
        File.WriteAllBytes(Path.Combine(packs, "model_phy.bin"), PackedArchiveFixture.Build(
            ("model/scene/house.phy", [7]), ("model/scene/tent.phy", [8])));
        var output = Path.Combine(_root, "raw");
        Directory.CreateDirectory(Path.Combine(output, "model", "scene"));
        File.WriteAllBytes(Path.Combine(output, "model", "scene", "house.phy"), [1]);

        Assert.Equal(0, CollisionPacks.Extract(packs, output, TextWriter.Null));

        Assert.Equal([1], File.ReadAllBytes(Path.Combine(output, "model", "scene", "house.phy")));
        Assert.Equal([8], File.ReadAllBytes(Path.Combine(output, "model", "scene", "tent.phy")));
    }

    [Fact]
    public void ATamperedPackNamingAPathOutsideTheOutputIsRefusedAndNothingIsWritten()
    {
        var packs = Path.Combine(_root, "packs");
        Directory.CreateDirectory(packs);
        File.WriteAllBytes(Path.Combine(packs, "model_phy.bin"), PackedArchiveFixture.Build(
            ("model/scene/house.phy", [7]), ("../../escape.phy", [6])));
        var output = Path.Combine(_root, "deep", "raw");

        Assert.Equal(1, CollisionPacks.Extract(packs, output, TextWriter.Null));

        Assert.False(File.Exists(Path.Combine(_root, "escape.phy")));
        Assert.False(File.Exists(Path.Combine(output, "model", "scene", "house.phy")));
    }

    [Fact]
    public void ABuildWithNoCollisionPackIsReported()
    {
        Directory.CreateDirectory(_root);

        Assert.Equal(1, CollisionPacks.Extract(_root, Path.Combine(_root, "raw"), TextWriter.Null));
    }
}
