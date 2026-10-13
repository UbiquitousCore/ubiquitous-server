using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record PvpFlagChangedPayload(
    [property: JsonPropertyName("flagged")] bool Flagged);
