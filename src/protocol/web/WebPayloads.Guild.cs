using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record GuildFoundPayload(
    [property: JsonPropertyName("name")] string Name);

public sealed record GuildFoundPromptPayload(
    [property: JsonPropertyName("entry")] int Entry);

public sealed record GuildInvitePayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId);

public sealed record GuildInviteSentPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("name")] string Name);

public sealed record GuildInvitedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("guildName")] string GuildName);

public sealed record GuildDeclinedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("name")] string Name);

public sealed record GuildKickPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId);

public sealed record GuildRosterMemberPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("isLeader")] bool IsLeader);

public sealed record GuildRosterPayload(
    [property: JsonPropertyName("guildId")] string? GuildId,
    [property: JsonPropertyName("guildName")] string? GuildName,
    [property: JsonPropertyName("members")] IReadOnlyList<GuildRosterMemberPayload> Members);
