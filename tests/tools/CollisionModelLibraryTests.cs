using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public sealed class CollisionModelLibraryTests : IDisposable
{
    private readonly string _models = Path.Combine(Path.GetTempPath(), $"ubiquitous-phy-{Guid.NewGuid():N}");

    public CollisionModelLibraryTests() => Directory.CreateDirectory(Path.Combine(_models, "scene", "build"));

    public void Dispose() => Directory.Delete(_models, recursive: true);

    private void Block(string name, float halfX)
    {
        using var stream = File.Create(Path.Combine(_models, "scene", "build", name));
        using var writer = new BinaryWriter(stream);
        writer.Write(1);
        writer.Write(3);
        writer.Write(0);
        writer.Write(0f);
        foreach (var value in new[] { 0f, 0f, 1f, halfX, -1f, 1f }) writer.Write(value);
    }

    private static float Width(IReadOnlyList<BakeTriangle>? triangles) =>
        triangles!.SelectMany(t => new[] { t.A.X, t.B.X, t.C.X }).Max() * 2;

    [Fact]
    public void AModelPlacedUnderItsSName_UsesTheCollisionShippedUnderItsBaseName()
    {
        Block("stonewall.phy", 3f);

        Assert.Equal(6f, Width(new CollisionModelLibrary(_models).TrianglesOf("model/scene/build/stonewall_s.bdae")), 3);
    }

    [Fact]
    public void AModelNamedWithADoubledUnderscore_UsesTheCollisionNamedWithOne()
    {
        Block("orcbuilding_arena01.phy", 2f);

        Assert.NotNull(new CollisionModelLibrary(_models).TrianglesOf("model/scene/build/orcbuilding__arena01.bdae"));
    }

    [Fact]
    public void AModelsOwnCollision_WinsOverItsBaseNames()
    {
        Block("tower.phy", 3f);
        Block("tower_s.phy", 1f);

        Assert.Equal(2f, Width(new CollisionModelLibrary(_models).TrianglesOf("model/scene/build/tower_s.bdae")), 3);
    }

    [Fact]
    public void AModelWithNoCollisionUnderAnyName_StillHasNone()
    {
        Block("tower.phy", 3f);

        Assert.Null(new CollisionModelLibrary(_models).TrianglesOf("model/scene/build/grass_s.bdae"));
    }
}
