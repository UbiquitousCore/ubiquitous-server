using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record WorldAmbientPayload(
    [property: JsonPropertyName("weather")] string Weather,
    [property: JsonPropertyName("festival")] string? Festival);
