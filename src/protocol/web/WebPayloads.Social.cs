using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record SocialTargetPayload(
    [property: JsonPropertyName("targetId")] string? TargetId = null,
    [property: JsonPropertyName("name")] string? Name = null);

public sealed record SocialEntryPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("online")] bool Online = false,
    [property: JsonPropertyName("level")] int? Level = null,
    [property: JsonPropertyName("race")] int? Race = null,
    [property: JsonPropertyName("class")] int? Class = null,
    [property: JsonPropertyName("gender")] int? Gender = null,
    [property: JsonPropertyName("mapId")] int? MapId = null,
    [property: JsonPropertyName("position")] Position? Position = null);

public sealed record SocialListUpdatedPayload(
    [property: JsonPropertyName("friends")] IReadOnlyList<SocialEntryPayload> Friends,
    [property: JsonPropertyName("ignored")] IReadOnlyList<SocialEntryPayload> Ignored);

public sealed record SocialWhoListPayload(
    [property: JsonPropertyName("players")] IReadOnlyList<SocialEntryPayload> Players);
