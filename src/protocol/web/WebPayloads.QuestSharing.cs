using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record QuestSharePayload(
    [property: JsonPropertyName("questId")] uint QuestId);

public sealed record QuestShareOfferedPayload(
    [property: JsonPropertyName("sharerId")] WebNetworkId SharerId,
    [property: JsonPropertyName("sharerName")] string SharerName,
    [property: JsonPropertyName("questId")] uint QuestId,
    [property: JsonPropertyName("titleStringId")] uint TitleStringId,
    [property: JsonPropertyName("detailsStringId")] uint DetailsStringId,
    [property: JsonPropertyName("minLevel")] int MinLevel);

public sealed record QuestShareAnswerPayload(
    [property: JsonPropertyName("questId")] uint QuestId,
    [property: JsonPropertyName("accept")] bool Accept);

public sealed record QuestShareResultPayload(
    [property: JsonPropertyName("memberId")] WebNetworkId MemberId,
    [property: JsonPropertyName("memberName")] string MemberName,
    [property: JsonPropertyName("questId")] uint QuestId,
    [property: JsonPropertyName("result")] string Result);
