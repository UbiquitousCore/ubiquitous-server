using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record PartyInvitePayload(
    [property: JsonPropertyName("networkId")] WebNetworkId? NetworkId = null,
    [property: JsonPropertyName("name")] string? Name = null);

public sealed record PartyInviteSentPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("name")] string Name);

public sealed record PartyInvitedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("name")] string Name);

public sealed record PartyDeclinedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("name")] string Name);

public sealed record PartyKickPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId);

public sealed record PartyVoteKickPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId);

public sealed record PartyVoteKickAskedPayload(
    [property: JsonPropertyName("initiatorId")] WebNetworkId InitiatorId,
    [property: JsonPropertyName("initiatorName")] string InitiatorName,
    [property: JsonPropertyName("targetId")] WebNetworkId TargetId,
    [property: JsonPropertyName("targetName")] string TargetName);

public sealed record PartyVoteKickAnswerPayload(
    [property: JsonPropertyName("agree")] bool Agree);

public sealed record PartyVoteKickRefusedPayload(
    [property: JsonPropertyName("memberId")] WebNetworkId MemberId,
    [property: JsonPropertyName("memberName")] string MemberName,
    [property: JsonPropertyName("initiatorId")] WebNetworkId InitiatorId,
    [property: JsonPropertyName("initiatorName")] string InitiatorName);

public sealed record PartyVoteKickEndedPayload(
    [property: JsonPropertyName("targetId")] WebNetworkId TargetId,
    [property: JsonPropertyName("targetName")] string TargetName,
    [property: JsonPropertyName("passed")] bool Passed);

public sealed record PartyRosterMemberPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("isLeader")] bool IsLeader,
    [property: JsonPropertyName("online")] bool Online);

public sealed record PartyRosterPayload(
    [property: JsonPropertyName("partyId")] string? PartyId,
    [property: JsonPropertyName("members")] IReadOnlyList<PartyRosterMemberPayload> Members);
