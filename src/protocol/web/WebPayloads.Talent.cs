using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record TalentLearnRequest(
    [property: JsonPropertyName("talentId")] uint TalentId);

public sealed record TalentRankPayload(
    [property: JsonPropertyName("talentId")] uint TalentId,
    [property: JsonPropertyName("rank")] int Rank);

public sealed record TalentStatePayload(
    [property: JsonPropertyName("points")] int Points,
    [property: JsonPropertyName("ranks")] IReadOnlyList<TalentRankPayload> Ranks);

public sealed record TalentRefusedPayload(
    [property: JsonPropertyName("talentId")] uint TalentId,
    [property: JsonPropertyName("reason")] string Reason);
