using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record NavigationPolygonPayload(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("vertices")] IReadOnlyList<float> Vertices,
    [property: JsonPropertyName("links")] IReadOnlyList<int> Links);

public sealed record NavigationParametersPayload(
    [property: JsonPropertyName("cellSize")] float CellSize,
    [property: JsonPropertyName("bodyRadius")] float BodyRadius,
    [property: JsonPropertyName("stepHeight")] float StepHeight,
    [property: JsonPropertyName("maxSlopeDegrees")] float MaxSlopeDegrees,
    [property: JsonPropertyName("headroom")] float Headroom);

public sealed record NavigationGroundPayload(
    [property: JsonPropertyName("mapId")] int MapId,
    [property: JsonPropertyName("baked")] bool Baked,
    [property: JsonPropertyName("polygonTotal")] int PolygonTotal,
    [property: JsonPropertyName("centre")] IReadOnlyList<float> Centre,
    [property: JsonPropertyName("radius")] float Radius,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("parameters")] NavigationParametersPayload? Parameters,
    [property: JsonPropertyName("polygons")] IReadOnlyList<NavigationPolygonPayload> Polygons,
    [property: JsonPropertyName("standingOn")] int? StandingOn,
    [property: JsonPropertyName("groundY")] float? GroundY,
    [property: JsonPropertyName("playerY")] float PlayerY,
    [property: JsonPropertyName("nearestY")] float? NearestY = null);
