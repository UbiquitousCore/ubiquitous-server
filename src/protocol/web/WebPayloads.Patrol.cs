using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record PatrolFilePayload(
    [property: JsonPropertyName("mapId")] int MapId,
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("text")] string? Text);

public sealed record PatrolSavePayload(
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("text")] string Text);

public sealed record PatrolSavedPayload(
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("routes")] int Routes);
