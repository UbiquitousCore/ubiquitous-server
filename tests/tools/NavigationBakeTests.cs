using Ubiquitous.AssetExtractor;
using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.World;

namespace Ubiquitous.Integration.Tests;

public class NavigationBakeTests
{
    private static void AddFloor(List<BakeTriangle> triangles, float minX, float minZ, float maxX, float maxZ, float y)
    {
        var a = new Position(minX, y, minZ);
        var b = new Position(maxX, y, minZ);
        var c = new Position(maxX, y, maxZ);
        var d = new Position(minX, y, maxZ);
        triangles.Add(new BakeTriangle(a, c, b));
        triangles.Add(new BakeTriangle(a, d, c));
    }

    private static void AddBox(
        List<BakeTriangle> triangles, float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        var corners = new Position[8];
        for (int i = 0; i < 8; i++)
        {
            corners[i] = new Position(
                (i & 1) == 0 ? minX : maxX,
                (i & 2) == 0 ? minY : maxY,
                (i & 4) == 0 ? minZ : maxZ);
        }

        void Quad(int a, int b, int c, int d)
        {
            triangles.Add(new BakeTriangle(corners[a], corners[b], corners[c]));
            triangles.Add(new BakeTriangle(corners[a], corners[c], corners[d]));
        }

        Quad(2, 6, 7, 3);
        Quad(0, 1, 5, 4);
        Quad(0, 4, 6, 2);
        Quad(1, 3, 7, 5);
        Quad(0, 2, 3, 1);
        Quad(4, 5, 7, 6);
    }

    private static NavigationMesh MeshOf(NavigationBakeResult result) =>
        new(result.Parameters, result.Mesh);

    [Fact]
    public void ATwoStoreyBuilding_KeepsBothFloorsAtTheSamePlace()
    {
        var triangles = new List<BakeTriangle>();
        AddFloor(triangles, 0, 0, 10, 10, 0f);
        AddFloor(triangles, 0, 0, 10, 10, 4f);

        var mesh = MeshOf(NavigationBaker.Bake(triangles));

        AssertGround(0f, mesh.GroundHeightNear(5f, 5f, 0.2f));
        AssertGround(4f, mesh.GroundHeightNear(5f, 5f, 3.8f));

        int downstairs = mesh.PolygonNear(5f, 5f, 0f);
        int upstairs = mesh.PolygonNear(5f, 5f, 4f);
        Assert.NotEqual(downstairs, upstairs);

        Assert.True(mesh.CanStepStraight(new Position(3f, 0f, 3f), new Position(7f, 0f, 7f)));
        Assert.True(mesh.CanStepStraight(new Position(3f, 4f, 3f), new Position(7f, 4f, 7f)));
        AssertGround(4f, mesh.GroundHeightNear(7f, 7f, 4f, maxDrift: 0.5f));
    }

    [Fact]
    public void ACeilingOverAFloor_IsNotGroundToStandOn()
    {
        var triangles = new List<BakeTriangle>();
        AddFloor(triangles, 0, 0, 16, 16, 0f);

        var ceiling = new List<BakeTriangle>();
        AddFloor(ceiling, 0, 0, 16, 16, 5f);
        foreach (var t in ceiling) triangles.Add(new BakeTriangle(t.A, t.C, t.B));

        var mesh = MeshOf(NavigationBaker.Bake(triangles));

        AssertGround(0f, mesh.GroundHeightNear(8f, 8f, 0f, maxDrift: 0.5f));
        Assert.Null(mesh.GroundHeightNear(8f, 8f, 5f, maxDrift: 0.5f));
    }

    [Fact]
    public void TheUndersideOfARoof_IsNotGroundToStandOn()
    {
        var triangles = new List<BakeTriangle>();
        AddFloor(triangles, 0, 0, 16, 16, 0f);
        AddBox(triangles, 4, 3f, 4, 12, 4f, 12);

        var mesh = MeshOf(NavigationBaker.Bake(triangles));

        AssertGround(0f, mesh.GroundHeightNear(8f, 8f, 0f, maxDrift: 0.5f));
        AssertGround(4f, mesh.GroundHeightNear(8f, 8f, 4f, maxDrift: 0.5f));

        Assert.Null(mesh.GroundHeightNear(8f, 8f, 3f, maxDrift: 0.5f));
    }

    [Fact]
    public void AFloorWithSomethingSolidOnIt_IsWalkableAroundItAndOverItButNotUnderIt()
    {
        var triangles = new List<BakeTriangle>();
        AddFloor(triangles, 0, 0, 16, 16, 0f);
        AddBox(triangles, 6, 0, 6, 10, 1f, 10);

        var mesh = MeshOf(NavigationBaker.Bake(triangles));

        AssertGround(0f, mesh.GroundHeightNear(2f, 2f, 0f));
        AssertGround(1f, mesh.GroundHeightNear(8f, 8f, 1f));

        Assert.Null(mesh.GroundHeightNear(8f, 8f, 0f, maxDrift: 0.5f));

        Assert.False(mesh.CanStepStraight(new Position(2f, 0f, 8f), new Position(14f, 0f, 8f)));
        Assert.True(mesh.CanStepStraight(new Position(2f, 0f, 2f), new Position(14f, 0f, 2f)));
    }

    [Fact]
    public void GroundSteeperThanTheLimit_IsNotWalkable()
    {
        var steep = new List<BakeTriangle>();
        AddRamp(steep, rise: 8f);

        var mesh = MeshOf(NavigationBaker.Bake(steep));

        Assert.Equal(0, mesh.PolygonTotal);
    }

    [Fact]
    public void GroundInsideTheLimit_IsWalkable()
    {
        var gentle = new List<BakeTriangle>();
        AddRamp(gentle, rise: 2f);

        var mesh = MeshOf(NavigationBaker.Bake(gentle));

        Assert.True(mesh.PolygonTotal > 0);
        Assert.NotNull(mesh.GroundHeightNear(6f, 6f, 3f));
    }

    private static void AddRamp(List<BakeTriangle> triangles, float rise)
    {
        for (int z = 0; z < 12; z++)
        {
            float y0 = z * rise / 4f;
            float y1 = (z + 1) * rise / 4f;
            var a = new Position(0, y0, z);
            var b = new Position(12, y0, z);
            var c = new Position(12, y1, z + 1);
            var d = new Position(0, y1, z + 1);
            triangles.Add(new BakeTriangle(a, c, b));
            triangles.Add(new BakeTriangle(a, d, c));
        }
    }

    [Fact]
    public void AGapNarrowerThanABody_IsNotWalkable()
    {
        var mesh = MeshOf(NavigationBaker.Bake(Corridor(0.6f)));

        Assert.Null(mesh.GroundHeightNear(10f, 8f, 0f, maxDrift: 0.5f));
        Assert.False(mesh.CanStepStraight(new Position(10f, 0f, 2f), new Position(10f, 0f, 14f)));
    }

    [Fact]
    public void AGapWideEnoughForABody_IsWalkableThrough()
    {
        var mesh = MeshOf(NavigationBaker.Bake(Corridor(3f)));

        AssertGround(0f, mesh.GroundHeightNear(10f, 8f, 0f, maxDrift: 0.5f));
        Assert.True(mesh.CanStepStraight(new Position(10f, 0f, 2f), new Position(10f, 0f, 14f)));
    }

    private static List<BakeTriangle> Corridor(float gap)
    {
        var triangles = new List<BakeTriangle>();
        AddFloor(triangles, 0, 0, 20, 16, 0f);

        float half = gap / 2f;
        AddBox(triangles, 0f, 0f, 7f, 10f - half, 3f, 9f);
        AddBox(triangles, 10f + half, 0f, 7f, 20f, 3f, 9f);
        return triangles;
    }

    [Fact]
    public void ABakeLinksItsPolygonsAcrossTheTilesItWasBuiltIn()
    {
        var triangles = new List<BakeTriangle>();
        AddFloor(triangles, 0, 0, 140, 16, 0f);

        var mesh = MeshOf(NavigationBaker.Bake(triangles));

        Assert.True(mesh.CanStepStraight(new Position(40f, 0f, 8f), new Position(100f, 0f, 8f)));
    }

    [Fact]
    public void EveryTile_CarriesTheSurfaceThatFollowsTheGroundInsideItsPolygons()
    {
        var triangles = new List<BakeTriangle>();
        for (float x = 0; x < 24f; x += 1f)
        {
            AddFloor(triangles, x, 0f, x + 1f, 24f, x < 8f ? 0f : x < 16f ? (x - 8f) * 0.7f : 5.6f);
        }

        var mesh = NavigationBaker.Bake(triangles).Mesh;

        int tiles = 0;
        for (int i = 0; i < mesh.GetMaxTiles(); i++)
        {
            var tile = mesh.GetTile(i);
            if (tile?.data?.header is not { polyCount: > 0 } header) continue;

            tiles++;
            Assert.Equal(header.polyCount, header.detailMeshCount);
            Assert.True(header.detailTriCount >= header.polyCount, "a triangle per polygon at least");
        }

        Assert.True(tiles > 0, "something baked");
    }

    [Fact]
    public void TheSummary_SaysHowMuchOfTheGroundCameOutWalkable()
    {
        var triangles = new List<BakeTriangle>();
        AddFloor(triangles, 0, 0, 20, 20, 0f);

        var result = NavigationBaker.Bake(triangles);

        Assert.InRange(result.Summary.WalkableFraction, 0.8f, 1f);
        Assert.InRange(result.Summary.WalkableArea, 300f, 400f);
        Assert.True(result.Summary.PolygonTotal > 0);
    }

    [Fact]
    public void CorrectionsOutliveARebakeOfChangedGround()
    {
        var edit = new NavigationEdit(
            NavigationEditKind.Unwalkable, MinX: 6f, MaxX: 12f, MinZ: 6f, MaxZ: 12f, Note: "a ledge nobody should reach");
        var overrides = new NavigationOverrides(13, [edit]);

        var first = new List<BakeTriangle>();
        AddFloor(first, 0, 0, 20, 20, 0f);
        var second = new List<BakeTriangle>();
        AddFloor(second, 0, 0, 24, 22, 0f);

        var firstBake = NavigationBaker.Bake(first);
        var secondBake = NavigationBaker.Bake(second);
        Assert.NotEqual(firstBake.Summary.WalkableArea, secondBake.Summary.WalkableArea);

        foreach (var ground in new[] { first, second })
        {
            Assert.NotNull(MeshOf(NavigationBaker.Bake(ground)).GroundHeightNear(9f, 9f, 0f));

            var corrected = MeshOf(NavigationBaker.Bake(ground, overrides));
            Assert.Null(corrected.GroundHeightNear(9f, 9f, 0f));
            Assert.NotNull(corrected.GroundHeightNear(3f, 3f, 0f));
        }
    }

    private static void AssertGround(float expected, float? actual)
    {
        Assert.NotNull(actual);
        Assert.InRange(
            actual!.Value,
            expected - NavigationBaker.CellHeight,
            expected + NavigationBaker.CellHeight);
    }
}
