using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record EmoteRequestPayload(
    [property: JsonPropertyName("name")] string Name);

public sealed record EmotePerformedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);
