using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record InstanceEnterPayload(
    [property: JsonPropertyName("mapId")] int MapId,
    [property: JsonPropertyName("pos")] Position? Position = null,
    [property: JsonPropertyName("yaw")] float Yaw = 0f);
