using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record ReportPlayerPayload(
    [property: JsonPropertyName("targetId")] string TargetId,
    [property: JsonPropertyName("reason")] string Reason);
