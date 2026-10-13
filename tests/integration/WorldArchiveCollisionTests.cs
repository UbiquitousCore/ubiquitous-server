using Ubiquitous.AssetExtractor;
using Ubiquitous.Common;
using Ubiquitous.World;

namespace Ubiquitous.Integration.Tests;

public class WorldArchiveCollisionTests
{
    private const float TileSize = 64f;

    private static BakeTriangle[] Rect(float minX, float minZ, float sizeX, float sizeZ, float y) =>
    [
        new(new Position(minX, y, minZ), new Position(minX, y, minZ + sizeZ), new Position(minX + sizeX, y, minZ + sizeZ)),
        new(new Position(minX, y, minZ), new Position(minX + sizeX, y, minZ + sizeZ), new Position(minX + sizeX, y, minZ)),
    ];

    private static BakeTriangle[] Floor(float minX, float minZ, float size, float y) =>
        Rect(minX, minZ, size, size, y);

    private static WorldItemTile Tile(int tileX, int tileZ, params WorldItemPlacement[] placements) =>
        new(tileX, tileZ, placements);

    private static WorldItemPlacement At(string model, float x, float y, float z) =>
        new(model, new Position(x, y, z), new PlacementTurn(0f, 0f, 0f, 1f), new PlacementScale(1f, 1f, 1f));

    [Fact]
    public void APlacement_StandsWhereItsOwnTileSaysItDoes()
    {
        var source = new WorldArchiveCollisionSource(
            [Tile(2, 3, At("build/hut.bdae", 10f, 4f, 20f))],
            _ => Floor(0f, 0f, 2f, 0f));

        var triangles = source.TrianglesIn(0f, 0f, 1000f, 1000f);

        Assert.Equal(138f, triangles.SelectMany(t => new[] { t.A.X, t.B.X, t.C.X }).Min(), precision: 3);
        Assert.Equal(4f, triangles.SelectMany(t => new[] { t.A.Y, t.B.Y, t.C.Y }).Min(), precision: 3);
        Assert.Equal(1, source.Placements);
        Assert.Equal(1, source.Resolved);
    }

    [Fact]
    public void APlacementWithNoCollisionInThisExtract_IsCountedAndSkipped()
    {
        var source = new WorldArchiveCollisionSource(
            [Tile(0, 0, At("build/hut.bdae", 1f, 0f, 1f), At("scenery/grass.bdae", 2f, 0f, 2f))],
            model => model.StartsWith("build/", StringComparison.Ordinal) ? Floor(0f, 0f, 2f, 0f) : null);

        Assert.Equal(2, source.Placements);
        Assert.Equal(1, source.Resolved);
    }

    [Fact]
    public void AQuarterTurn_SwingsAModelRoundWhereItStands()
    {
        var strip = Rect(0f, 0f, 8f, 1f, 0f);
        float half = MathF.PI / 4f;
        var source = new WorldArchiveCollisionSource(
            [Tile(0, 0, new WorldItemPlacement(
                "build/wall.bdae",
                new Position(0f, 0f, 0f),
                new PlacementTurn(0f, MathF.Sin(half), 0f, MathF.Cos(half)),
                new PlacementScale(1f, 1f, 1f)))],
            _ => strip);

        var triangles = source.TrianglesIn(-100f, -100f, 100f, 100f);
        float widestX = triangles.SelectMany(t => new[] { t.A.X, t.B.X, t.C.X }).Select(MathF.Abs).Max();
        float widestZ = triangles.SelectMany(t => new[] { t.A.Z, t.B.Z, t.C.Z }).Select(MathF.Abs).Max();

        Assert.Equal(1f, widestX, precision: 3);
        Assert.Equal(8f, widestZ, precision: 3);
    }

    [Fact]
    public void ATurn_PutsCollisionWhereTheModelIsDrawn()
    {
        var strip = Rect(0f, -0.5f, 8f, 1f, 0f);
        float half = MathF.PI / 6f;
        var source = new WorldArchiveCollisionSource(
            [Tile(0, 0, new WorldItemPlacement(
                "build/wall.bdae",
                new Position(0f, 0f, 0f),
                new PlacementTurn(0f, MathF.Sin(half), 0f, MathF.Cos(half)),
                new PlacementScale(1f, 1f, 1f)))],
            _ => strip);

        var triangles = source.TrianglesIn(-100f, -100f, 100f, 100f);
        float farZ = triangles.SelectMany(t => new[] { t.A.Z, t.B.Z, t.C.Z })
            .OrderByDescending(MathF.Abs).First();

        Assert.True(farZ > 0f, $"the strip swung to z = {farZ}, which is the wrong way round");
    }

    [Fact]
    public void ASizeOfTwo_MakesAModelTwiceAsLarge()
    {
        var source = new WorldArchiveCollisionSource(
            [Tile(0, 0, new WorldItemPlacement(
                "build/hut.bdae",
                new Position(0f, 0f, 0f),
                new PlacementTurn(0f, 0f, 0f, 1f),
                new PlacementScale(2f, 3f, 2f)))],
            _ => Floor(0f, 0f, 4f, 1f));

        var triangles = source.TrianglesIn(-100f, -100f, 100f, 100f);

        Assert.Equal(8f, triangles.SelectMany(t => new[] { t.A.X, t.B.X, t.C.X }).Max(), precision: 3);
        Assert.Equal(3f, triangles.SelectMany(t => new[] { t.A.Y, t.B.Y, t.C.Y }).Max(), precision: 3);
    }

    [Fact]
    public void AFloor_StaysAFloorRatherThanBecomingACeiling()
    {
        var source = new WorldArchiveCollisionSource(
            [Tile(0, 0, At("build/hut.bdae", 32f, 0f, 32f))],
            _ => Floor(-6f, -6f, 12f, 0f));

        var result = NavigationBaker.Bake(source.TrianglesIn(0f, 0f, 64f, 64f));

        Assert.True(result.Summary.PolygonTotal > 0, "a floor placed by the map bakes as ground");
    }

    [Fact]
    public void AHouse_TakesItsInteriorWithIt()
    {
        var source = new WorldArchiveCollisionSource(
            [Tile(0, 0, At("build/hut.bdae", 10f, 0f, 10f))],
            model => model.EndsWith("_inside.bdae", StringComparison.Ordinal)
                ? Floor(0f, 0f, 2f, 4f)
                : Floor(0f, 0f, 2f, 0f));

        Assert.Equal(1, source.Resolved);
        Assert.Equal(1, source.Interiors);

        var heights = source.TrianglesIn(0f, 0f, 64f, 64f)
            .SelectMany(t => new[] { t.A.Y, t.B.Y, t.C.Y })
            .Distinct()
            .Order()
            .ToList();
        Assert.Equal([0f, 4f], heights);
    }

    [Fact]
    public void AnInteriorPlacedInItsOwnRight_DoesNotLookForAnInteriorOfItsOwn()
    {
        Assert.Equal("build/hut_inside.bdae", WorldArchiveCollisionSource.InteriorOf("build/hut.bdae"));
        Assert.Null(WorldArchiveCollisionSource.InteriorOf("build/hut_inside.bdae"));
        Assert.Null(WorldArchiveCollisionSource.InteriorOf("effects/scene/torch.beff"));
    }

    [Fact]
    public void ATwoStoreyBuilding_BakesAsTwoWalkableFloorsOverTheSameGround()
    {
        var building = new List<BakeTriangle>();
        building.AddRange(Floor(0f, 0f, 12f, 0f));
        building.AddRange(Floor(0f, 0f, 12f, 4f));
        AddWalls(building, 0f, 12f, 0f, 12f, 10f);

        var source = new WorldArchiveCollisionSource(
            [Tile(0, 0, At("build/hall.bdae", 20f, 2f, 20f))],
            _ => building);

        var result = NavigationBaker.Bake(source.TrianglesIn(0f, 0f, 128f, 128f));
        var mesh = new NavigationMesh(result.Parameters, result.Mesh);

        var lower = mesh.GroundHeightNear(26f, 26f, 2f, maxDrift: 0.5f);
        var upper = mesh.GroundHeightNear(26f, 26f, 6f, maxDrift: 0.5f);

        BakedGround.AssertGround(2f, lower);
        BakedGround.AssertGround(6f, upper);

        Assert.NotEqual(mesh.PolygonNear(26f, 26f, 2f), mesh.PolygonNear(26f, 26f, 6f));
    }

    private static void AddWalls(List<BakeTriangle> into, float minX, float maxX, float minZ, float maxZ, float height)
    {
        void Wall(Position a, Position b)
        {
            var aTop = new Position(a.X, a.Y + height, a.Z);
            var bTop = new Position(b.X, b.Y + height, b.Z);
            into.Add(new BakeTriangle(a, aTop, bTop));
            into.Add(new BakeTriangle(a, bTop, b));
        }

        Wall(new Position(minX, 0f, minZ), new Position(maxX, 0f, minZ));
        Wall(new Position(maxX, 0f, minZ), new Position(maxX, 0f, maxZ));
        Wall(new Position(maxX, 0f, maxZ), new Position(minX, 0f, maxZ));
        Wall(new Position(minX, 0f, maxZ), new Position(minX, 0f, minZ));
    }

    [Fact]
    public void OnlyTheTilesAPlacementReachesInto_AreAskedFor()
    {
        var source = new WorldArchiveCollisionSource(
            [Tile(0, 0, At("build/hut.bdae", 4f, 0f, 4f)), Tile(5, 5, At("build/hut.bdae", 4f, 0f, 4f))],
            _ => Floor(0f, 0f, 2f, 0f));

        var tiles = source.TilesOf(TileSize).Order().ToList();

        Assert.Equal([(0, 0), (5, 5)], tiles);
        Assert.Empty(source.TrianglesIn(200f, 200f, 260f, 260f));
    }
}
