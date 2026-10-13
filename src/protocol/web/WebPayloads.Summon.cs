using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record SummonMemberPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("canSummon")] bool CanSummon);

public sealed record SummonOpenedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("members")] IReadOnlyList<SummonMemberPayload> Members);

public sealed record SummonRequestPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("memberId")] WebNetworkId MemberId);

public sealed record SummonAskedPayload(
    [property: JsonPropertyName("summonerId")] WebNetworkId SummonerId,
    [property: JsonPropertyName("summonerName")] string SummonerName,
    [property: JsonPropertyName("map")] int Map,
    [property: JsonPropertyName("pos")] Position Position);

public sealed record SummonTooLowPayload(
    [property: JsonPropertyName("summonerName")] string SummonerName);

public sealed record SummonAnswerPayload(
    [property: JsonPropertyName("accept")] bool Accept);

public sealed record SummonResultPayload(
    [property: JsonPropertyName("memberId")] WebNetworkId MemberId,
    [property: JsonPropertyName("memberName")] string MemberName,
    [property: JsonPropertyName("result")] string Result);
