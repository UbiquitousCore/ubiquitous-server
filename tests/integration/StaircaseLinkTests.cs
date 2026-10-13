using Ubiquitous.AssetExtractor;
using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.World;

namespace Ubiquitous.Integration.Tests;

public class StaircaseLinkTests
{
    private static List<BakeTriangle> Steps(float fromX, float toX, float fromY, int treads)
    {
        var triangles = new List<BakeTriangle>();
        float run = (toX - fromX) / treads;
        for (int i = 0; i < treads; i++)
        {
            BakedGround.AddFloor(triangles, fromX + (i * run), 2f, fromX + ((i + 1) * run), 10f, fromY + i + 1f);
        }

        return triangles;
    }

    private static readonly NavigationParameters Rules = NavigationBaker.DefaultParameters;

    [Fact]
    public void AFlightOfSteps_HasAWayUpFromItsFootToItsHead()
    {
        var link = StaircaseLinks.Of("build/maincity_stair_01.bdae", Steps(12f, 24f, 0f, 4), Rules);

        Assert.NotNull(link);
        Assert.True(link!.Value.To.Y - link.Value.From.Y > 2f, "it climbs");
        Assert.True(link.Value.To.X > link.Value.From.X, "and it goes along");
    }

    [Fact]
    public void TheSameShapeUnderAnotherName_IsNotAFlightOfSteps()
    {
        Assert.Null(StaircaseLinks.Of("nature/other_rock_03.bdae", Steps(12f, 24f, 0f, 4), Rules));
    }

    [Fact]
    public void FloorsStackedOverEachOther_AreNotAFlightOfSteps()
    {
        var storeys = new List<BakeTriangle>();
        BakedGround.AddFloor(storeys, 0, 0, 8, 8, 0f);
        BakedGround.AddFloor(storeys, 0, 0, 8, 8, 4f);
        BakedGround.AddFloor(storeys, 0, 0, 8, 8, 8f);

        Assert.Null(StaircaseLinks.Of("build/tower_step01.bdae", storeys, Rules));
    }

    [Fact]
    public void OneTallStep_IsAKerbRatherThanAFlight()
    {
        var kerb = new List<BakeTriangle>();
        BakedGround.AddFloor(kerb, 0, 0, 4, 8, 0f);
        BakedGround.AddFloor(kerb, 4, 0, 8, 8, 1f);

        Assert.Null(StaircaseLinks.Of("build/maincity_bigstep01.bdae", kerb, Rules));
    }

    [Fact]
    public void ACreatureCanWalkUpAFlightTheBakeLeavesInPieces()
    {
        var ground = new List<BakeTriangle>();
        BakedGround.AddFloor(ground, 0, 0, 14, 12, 0f);
        BakedGround.AddFloor(ground, 22, 0, 36, 12, 4f);

        var stairs = Steps(12f, 24f, 0f, 4);
        var placed = new StaticObjectCollisionSource(
            [new CollisionPlacement("build/maincity_stair_01.bdae", new Position(0f, 0f, 0f), 0f, 1f)],
            _ => stairs);

        var below = new Position(6f, 0f, 6f);
        var above = new Position(30f, 4f, 6f);

        var withoutStairs = BakedGround.Mesh(ground);
        var stopsShort = withoutStairs.FindRoute(below, above);
        Assert.NotNull(stopsShort);
        Assert.True(stopsShort![^1].X < 14f, $"it reached x = {stopsShort[^1].X}");

        var result = NavigationBaker.Bake(
            new CompositeCollisionSource([new TriangleCollisionSource(ground), placed]), Rules);
        var mesh = new NavigationMesh(result.Parameters, result.Mesh);

        var route = mesh.FindRoute(below, above);
        Assert.NotNull(route);
        Assert.Equal(above.X, route![^1].X, precision: 0);
    }
}
