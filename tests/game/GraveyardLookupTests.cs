using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class GraveyardLookupTests
{
    private static readonly MapDefinition MapWithRecall = new(
        Id: 1, InternalName: "map-1", RecallMapId: 2, RecallX: 900f, RecallY: 5f, RecallZ: 900f);
    private static readonly MapDefinition RecallMap = new(Id: 2, InternalName: "map-2");
    private static readonly MapDefinition MapWithNoRecall = new(Id: 3, InternalName: "map-3");

    private static GameContent ContentWith(
        IReadOnlyList<MapGraveyardDefinition> graveyards, params MapDefinition[] maps) =>
        GameContent.Development with
        {
            Graveyards = graveyards,
            Maps = maps.ToDictionary(map => map.Id),
        };

    [Fact]
    public void Resolve_PicksTheNearestGraveyardOnTheSameMap()
    {
        var content = ContentWith(
        [
            new MapGraveyardDefinition(1, MapId: 1, X: 100f, Y: 0f, Z: 0f, Yaw: 0f, ZoneId: 0, Faction: 0),
            new MapGraveyardDefinition(2, MapId: 1, X: 10f, Y: 0f, Z: 0f, Yaw: 1.5f, ZoneId: 0, Faction: 0),
        ],
        MapWithRecall);

        var destination = GraveyardLookup.Resolve(content, mapId: 1, from: new Position(0f, 0f, 0f));

        Assert.Equal((ushort)1, destination.MapId);
        Assert.Equal(new Position(10f, 0f, 0f), destination.Position);
        Assert.Equal(1.5f, destination.Yaw);
    }

    [Fact]
    public void Resolve_IgnoresGraveyardsOnOtherMaps()
    {
        var content = ContentWith(
        [
            new MapGraveyardDefinition(1, MapId: 2, X: 1f, Y: 0f, Z: 1f, Yaw: 0f, ZoneId: 0, Faction: 0),
        ],
        MapWithNoRecall);

        var destination = GraveyardLookup.Resolve(content, mapId: 3, from: new Position(50f, 5f, 50f));

        Assert.Equal((ushort)3, destination.MapId);
        Assert.Equal(new Position(50f, 5f, 50f), destination.Position);
    }

    [Fact]
    public void Resolve_FallsBackToTheMapsOwnRecallPoint()
    {
        var content = ContentWith(
        [
            new MapGraveyardDefinition(1, MapId: 2, X: 905f, Y: 5f, Z: 905f, Yaw: 0.2f, ZoneId: 0, Faction: 0),
        ],
        MapWithRecall, RecallMap);

        var destination = GraveyardLookup.Resolve(content, mapId: 1, from: new Position(0f, 0f, 0f));

        Assert.Equal((ushort)2, destination.MapId);
        Assert.Equal(new Position(900f, 5f, 900f), destination.Position);
        Assert.Equal(0f, destination.Yaw);
    }

    [Fact]
    public void Resolve_LeavesThePlayerWhereTheyStandWhenNothingElseIsKnown()
    {
        var content = ContentWith([], MapWithNoRecall);

        var destination = GraveyardLookup.Resolve(content, mapId: 3, from: new Position(50f, 5f, 50f));

        Assert.Equal((ushort)3, destination.MapId);
        Assert.Equal(new Position(50f, 5f, 50f), destination.Position);
    }

    [Fact]
    public void Resolve_LeavesThePlayerWhereTheyStandOnAnUnknownMap()
    {
        var content = GameContent.Development with { Graveyards = [], Maps = new Dictionary<ushort, MapDefinition>() };

        var destination = GraveyardLookup.Resolve(content, mapId: 99, from: new Position(50f, 5f, 50f));

        Assert.Equal((ushort)99, destination.MapId);
        Assert.Equal(new Position(50f, 5f, 50f), destination.Position);
    }
}
