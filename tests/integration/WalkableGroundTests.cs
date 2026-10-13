using Ubiquitous.AssetExtractor;
using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.World;

namespace Ubiquitous.Integration.Tests;

public class WalkableGroundTests
{
    private const ushort MapId = 13;

    private static NavigationMesh TwoStoreys()
    {
        var triangles = new List<BakeTriangle>();
        BakedGround.AddFloor(triangles, 0, 0, 12, 12, 0f);
        BakedGround.AddFloor(triangles, 0, 0, 12, 12, 4f);
        return BakedGround.Mesh(triangles);
    }

    // A field 64 by 64 at height 0, with water at height 2 over the squares given.
    private static NavigationMesh FieldWithWater(float minX, float maxX, float minZ, float maxZ)
    {
        var water = new List<NavigationWater>();
        for (float x = minX; x < maxX; x += 8f)
            for (float z = minZ; z < maxZ; z += 8f)
                water.Add(new NavigationWater(x, z, 8f, 2f));
        return BakedGround.Mesh(BakedGround.Floor(0f, 0f, 64f, 64f, 0f), water);
    }

    [Fact]
    public void FindRoute_GoesRoundAPondWhenTheWayRoundIsNotLong()
    {
        var mesh = FieldWithWater(8f, 56f, 0f, 48f);

        var route = mesh.FindRoute(new Position(4f, 0f, 24f), new Position(60f, 0f, 24f))!;

        Assert.Equal(60f, route[^1].X, 1);
        Assert.Contains(route, corner => corner.Z > 46f);
        Assert.Equal(2f, mesh.WaterSurfaceAt(20f, 4f));
        Assert.Null(mesh.WaterSurfaceAt(20f, 52f));
    }

    [Fact]
    public void FindRoute_SwimsANarrowStreamRatherThanWalkALongWayRound()
    {
        var mesh = FieldWithWater(24f, 32f, 0f, 56f);

        var route = mesh.FindRoute(new Position(8f, 0f, 24f), new Position(56f, 0f, 24f))!;

        Assert.Equal(56f, route[^1].X, 1);
        Assert.All(route, corner => Assert.True(corner.Z < 40f));
    }

    [Fact]
    public void ConnectedAreaUnder_TellsAnIslandFromTheGroundAroundIt()
    {
        var floors = BakedGround.Floor(0f, 0f, 40f, 40f, 0f);
        BakedGround.AddFloor(floors, 50f, 0f, 56f, 6f, 0f);
        var mesh = BakedGround.Mesh(floors);

        var field = mesh.ConnectedAreaUnder(10f, 10f, 0f);
        var island = mesh.ConnectedAreaUnder(53f, 3f, 0f);

        Assert.InRange(field, 1200f, 1600f);
        Assert.InRange(island, 10f, 36f);
        Assert.Equal(0f, mesh.ConnectedAreaUnder(46f, 3f, 0f));
    }

    [Fact]
    public void GroundHeightNear_AnswersTheFloorTheCallerIsStandingOn()
    {
        var mesh = TwoStoreys();

        BakedGround.AssertGround(4f, mesh.GroundHeightNear(6f, 6f, 3.8f));
        BakedGround.AssertGround(0f, mesh.GroundHeightNear(6f, 6f, 0.2f));
    }

    [Fact]
    public void GroundHeightNear_IsNullWhereNothingWalkableCoversThePoint()
    {
        Assert.Null(TwoStoreys().GroundHeightNear(50f, 50f, 0f));
    }

    [Fact]
    public void GroundHeightNear_RefusesAFloorFurtherAwayThanTheCallerWillAccept()
    {
        var mesh = TwoStoreys();

        BakedGround.AssertGround(4f, mesh.GroundHeightNear(6f, 6f, 4f, maxDrift: 0.5f));
        Assert.Null(mesh.GroundHeightNear(6f, 6f, 2f, maxDrift: 0.5f));
    }

    [Fact]
    public void GroundHeightNear_FollowsASlopeRatherThanSteppingDownIt()
    {
        var ramp = BakedGround.Mesh(
        [
            new BakeTriangle(new Position(0, 0, 0), new Position(12, 6, 12), new Position(12, 6, 0)),
            new BakeTriangle(new Position(0, 0, 0), new Position(0, 0, 12), new Position(12, 6, 12)),
        ]);

        Assert.Equal(3f, ramp.GroundHeightNear(6f, 6f, 3f)!.Value, precision: 0);
    }

    [Fact]
    public void TheServersOwnQuery_WillNotAnswerWithAFloorAStoreyAway()
    {
        var ground = new WalkableGroundSource(new Dictionary<ushort, NavigationMesh> { [MapId] = TwoStoreys() });

        BakedGround.AssertGround(0f, ground.GroundHeightNear(MapId, 6f, 6f, 0.3f));
        BakedGround.AssertGround(4f, ground.GroundHeightNear(MapId, 6f, 6f, 4.3f));

        Assert.Null(ground.GroundHeightNear(MapId, 6f, 6f, -4f));
    }

    [Fact]
    public void TheServersOwnQuery_KeepsABodyOnTheStepItIsClimbing()
    {
        var triangles = new List<BakeTriangle>();
        BakedGround.AddFloor(triangles, 0, 0, 12, 12, 1f);
        BakedGround.AddFloor(triangles, 0, 0, 12, 12, 4.2f);
        var ground = new WalkableGroundSource(
            new Dictionary<ushort, NavigationMesh> { [MapId] = BakedGround.Mesh(triangles) });

        BakedGround.AssertGround(4.2f, ground.GroundHeightNear(MapId, 6f, 6f, 4.16f));

        BakedGround.AssertGround(1f, ground.GroundHeightNear(MapId, 6f, 6f, 1.1f));
    }

    [Fact]
    public void TheServersOwnQuery_PutsABodyDownOntoTheFloorUnderIt()
    {
        var ground = BakedGround.Source(MapId, BakedGround.Floor(0, 0, 12, 12, 0f));

        BakedGround.AssertGround(0f, ground.GroundHeightNear(MapId, 6f, 6f, 3f));

        Assert.Null(ground.GroundHeightNear(MapId, 6f, 6f, NavigationMesh.MaxFall + 2f));
    }

    [Fact]
    public void GroundHeightNear_AnswersForABodyStandingAtTheEdgeOfTheGround()
    {
        var ground = BakedGround.Source(MapId, BakedGround.Floor(0, 0, 10, 10, 0f));

        BakedGround.AssertGround(0f, ground.GroundHeightNear(MapId, 0.3f, 5f, 0f));

        Assert.Null(ground.GroundHeightNear(MapId, -6f, 5f, 0f));
    }

    [Fact]
    public void PolygonUnder_SaysNothingWhereTheServerWouldSayNothing()
    {
        var triangles = new List<BakeTriangle>();
        BakedGround.AddFloor(triangles, 0, 0, 12, 12, 0f);
        BakedGround.AddFloor(triangles, 0, 0, 12, 12, -12f);
        var mesh = BakedGround.Mesh(triangles);

        Assert.True(mesh.PolygonUnder(6f, 6f, 0.2f) >= 0);

        Assert.Equal(-1, mesh.PolygonUnder(6f, 6f, -5f));

        Assert.True(mesh.PolygonNear(6f, 6f, -5f) >= 0);
    }

    private static List<BakeTriangle> ARoomACorridorAndARoom()
    {
        var triangles = new List<BakeTriangle>();
        BakedGround.AddFloor(triangles, 0, 0, 10, 10, 0f);
        BakedGround.AddFloor(triangles, 10, 0, 20, 3, 0f);
        BakedGround.AddFloor(triangles, 20, 0, 30, 10, 0f);
        return triangles;
    }

    [Fact]
    public void FindRoute_GoesRoundThroughTheGroundThatJoinsTwoPlaces()
    {
        var mesh = BakedGround.Mesh(ARoomACorridorAndARoom());
        var from = new Position(2f, 0f, 8f);
        var to = new Position(28f, 0f, 8f);

        Assert.False(mesh.CanStepStraight(from, to));

        var route = mesh.FindRoute(from, to);

        Assert.NotNull(route);
        Assert.Equal(to.X, route![^1].X, precision: 1);
        Assert.Equal(to.Z, route[^1].Z, precision: 1);

        var at = from;
        foreach (var corner in route)
        {
            Assert.True(mesh.CanStepStraight(at, corner), $"the leg to ({corner.X}, {corner.Z}) is not walkable");
            at = corner;
        }
    }

    [Fact]
    public void FindRoute_IsOneLegWhereTheWayIsAlreadyClear()
    {
        var route = BakedGround.Mesh(ARoomACorridorAndARoom())
            .FindRoute(new Position(2f, 0f, 2f), new Position(8f, 0f, 8f));

        Assert.NotNull(route);
        Assert.Single(route!);
    }

    [Fact]
    public void FindRoute_StopsAtTheEdgeWhenThereIsNoWayAcross()
    {
        var triangles = new List<BakeTriangle>();
        BakedGround.AddFloor(triangles, 0, 0, 10, 10, 0f);
        BakedGround.AddFloor(triangles, 20, 0, 30, 10, 0f);

        var route = BakedGround.Mesh(triangles).FindRoute(new Position(5f, 0f, 5f), new Position(25f, 0f, 5f));

        Assert.NotNull(route);
        Assert.NotEmpty(route!);
        Assert.InRange(route[^1].X, 8f, 10f);
    }

    [Fact]
    public void FindRoute_SaysSoForABodyOffTheBakedGround()
    {
        var ground = BakedGround.Mesh(BakedGround.Floor(0, 0, 10, 10, 0f));

        Assert.Null(ground.FindRoute(new Position(80f, 0f, 80f), new Position(5f, 0f, 5f)));
    }

    [Fact]
    public void CanStepStraight_CrossesGroundThatJoinsUp()
    {
        var triangles = new List<BakeTriangle>();
        BakedGround.AddFloor(triangles, 0, 0, 6, 6, 0f);
        BakedGround.AddFloor(triangles, 6, 0, 12, 6, 0f);

        Assert.True(BakedGround.Mesh(triangles).CanStepStraight(new Position(2f, 0f, 3f), new Position(10f, 0f, 3f)));
    }

    [Fact]
    public void CanStepStraight_AllowsAStepThatEndsInTheStripTheBakeShaved()
    {
        var mesh = BakedGround.Mesh(BakedGround.Floor(0, 0, 12, 12, 0f));

        Assert.True(mesh.CanStepStraight(new Position(6f, 0f, 6f), new Position(11.4f, 0f, 6f)));

        Assert.False(mesh.CanStepStraight(new Position(6f, 0f, 6f), new Position(16f, 0f, 6f)));
    }

    [Fact]
    public void CanStepStraight_StopsAtAWallTheBakeLeftNoGroundBehind()
    {
        var mesh = TwoStoreys();

        Assert.False(mesh.CanStepStraight(new Position(6f, 0f, 6f), new Position(26f, 0f, 6f)));
    }

    [Fact]
    public void CanStepStraight_WillNotCrossAGapWithNoGroundOverIt()
    {
        var triangles = new List<BakeTriangle>();
        BakedGround.AddFloor(triangles, 0, 0, 6, 6, 0f);
        BakedGround.AddFloor(triangles, 10, 0, 16, 6, 0f);

        Assert.False(BakedGround.Mesh(triangles).CanStepStraight(new Position(3f, 0f, 3f), new Position(13f, 0f, 3f)));
    }

    [Fact]
    public void CanStepStraight_RefusesToStartWhereThereIsNoGround()
    {
        var mesh = TwoStoreys();

        Assert.False(mesh.CanStepStraight(new Position(50f, 0f, 50f), new Position(6f, 0f, 6f)));
    }
}

public class WalkableGroundCorrectionTests
{
    private static NavigationOverrides Corrections(params NavigationEdit[] edits) => new(13, edits);

    private static List<BakeTriangle> ARoomWithALedge()
    {
        var triangles = new List<BakeTriangle>();
        BakedGround.AddFloor(triangles, 0, 0, 20, 12, 0f);
        BakedGround.AddFloor(triangles, 0, 0, 8, 12, 4f);
        return triangles;
    }

    [Fact]
    public void AnUnwalkableBox_TakesTheLedgeAwayAndLeavesTheFloorUnderIt()
    {
        var plain = BakedGround.Mesh(ARoomWithALedge());
        BakedGround.AssertGround(4f, plain.GroundHeightNear(4f, 6f, 4f, maxDrift: 0.5f));

        var corrected = BakedGround.Mesh(
            ARoomWithALedge(),
            Corrections(new NavigationEdit(
                NavigationEditKind.Unwalkable,
                MinX: -1, MaxX: 9, MinZ: -1, MaxZ: 13, MinY: 3f, MaxY: 6f)));

        Assert.Null(corrected.GroundHeightNear(4f, 6f, 4f, maxDrift: 0.5f));
        BakedGround.AssertGround(0f, corrected.GroundHeightNear(4f, 6f, 0f, maxDrift: 0.5f));
    }

    [Fact]
    public void AnUnwalkableBoxWithNoHeights_TakesEveryStoreyOverItsFootprint()
    {
        var corrected = BakedGround.Mesh(
            ARoomWithALedge(),
            Corrections(new NavigationEdit(
                NavigationEditKind.Unwalkable, MinX: -1, MaxX: 9, MinZ: -1, MaxZ: 13)));

        Assert.Null(corrected.GroundHeightNear(4f, 6f, 4f, maxDrift: 0.5f));
        Assert.Null(corrected.GroundHeightNear(4f, 6f, 0f, maxDrift: 0.5f));

        BakedGround.AssertGround(0f, corrected.GroundHeightNear(16f, 6f, 0f, maxDrift: 0.5f));
    }

    [Fact]
    public void AWalkableBox_AddsAFloorOverAGapAndJoinsItToWhatItLandsAgainst()
    {
        var withGap = new List<BakeTriangle>();
        BakedGround.AddFloor(withGap, 0, 0, 8, 8, 0f);
        BakedGround.AddFloor(withGap, 12, 0, 20, 8, 0f);

        var from = new Position(4f, 0f, 4f);
        var to = new Position(16f, 0f, 4f);

        Assert.False(BakedGround.Mesh(withGap).CanStepStraight(from, to));

        var bridged = BakedGround.Mesh(
            withGap,
            Corrections(new NavigationEdit(
                NavigationEditKind.Walkable, MinX: 8f, MaxX: 12f, MinZ: 0f, MaxZ: 8f, FloorY: 0f)));

        BakedGround.AssertGround(0f, bridged.GroundHeightNear(10f, 4f, 0f, maxDrift: 0.5f));
        Assert.True(bridged.CanStepStraight(from, to));
    }

    [Fact]
    public void ACorrectionThatCoversNothing_DoesNothing()
    {
        var plain = BakedGround.Mesh(ARoomWithALedge());
        var corrected = BakedGround.Mesh(
            ARoomWithALedge(),
            Corrections(new NavigationEdit(
                NavigationEditKind.Unwalkable, MinX: 900, MaxX: 950, MinZ: 900, MaxZ: 950)));

        Assert.Equal(plain.PolygonTotal, corrected.PolygonTotal);
        BakedGround.AssertGround(0f, corrected.GroundHeightNear(16f, 6f, 0f, maxDrift: 0.5f));
    }
}

public class WalkableGroundSourceTests
{
    [Fact]
    public void LoadFromDirectory_ReadsEveryMapItWasBakedFor()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-nav-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            NavigationBakeFixture.Write(directory, 13, BakedGround.Floor(0, 0, 12, 12, 0f));

            var source = WalkableGroundSource.LoadFromDirectory(directory);

            Assert.Equal(1, source.MapCount);
            Assert.True(source.HasGround(13));
            Assert.False(source.HasGround(1));
            BakedGround.AssertGround(0f, source.GroundHeightNear(13, 6f, 6f, 0f));
            Assert.Null(source.GroundHeightNear(1, 6f, 6f, 0f));
            Assert.True(source.CanStepStraight(13, new Position(2f, 0f, 6f), new Position(10f, 0f, 6f)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadFromDirectory_IsEmptyWhereNoMapWasEverBaked()
    {
        var source = WalkableGroundSource.LoadFromDirectory(
            Path.Combine(Path.GetTempPath(), $"ubiquitous-nav-missing-{Guid.NewGuid():N}"));

        Assert.Equal(0, source.MapCount);
        Assert.False(source.HasGround(13));
        Assert.Null(source.GroundHeightNear(13, 0f, 0f, 0f));
        Assert.False(source.CanStepStraight(13, new Position(0, 0, 0), new Position(1, 0, 1)));
    }
}
