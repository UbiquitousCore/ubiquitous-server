using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record DungeonCompletedPayload(
    [property: JsonPropertyName("mapId")] int MapId,
    [property: JsonPropertyName("name")] string Name);

public sealed record WorldHiddenObjectsPayload(
    [property: JsonPropertyName("positions")] IReadOnlyList<Position> Positions);

public sealed record DungeonRestartedPayload(
    [property: JsonPropertyName("count")] int Count);

public sealed record DungeonChooseModePayload(
    [property: JsonPropertyName("mode")] string Mode);

public sealed record DungeonModeChosenPayload(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("count")] int Count);

public sealed record DungeonBarrierOpenedPayload(
    [property: JsonPropertyName("barrierId")] string BarrierId,
    [property: JsonPropertyName("announce")] bool Announce,
    [property: JsonPropertyName("minX")] float MinX = 0,
    [property: JsonPropertyName("maxX")] float MaxX = 0,
    [property: JsonPropertyName("minZ")] float MinZ = 0,
    [property: JsonPropertyName("maxZ")] float MaxZ = 0,
    [property: JsonPropertyName("models")] IReadOnlyList<string>? Models = null);
