using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record ChatChannelPayload(
    [property: JsonPropertyName("channel")] string Channel);

public sealed record ChatChannelUpdatedPayload(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("joined")] bool Joined);

public sealed record ChatWhisperPayload(
    [property: JsonPropertyName("targetId")] string TargetId,
    [property: JsonPropertyName("text")] string Text);

public sealed record ChatWhisperReceivedPayload(
    [property: JsonPropertyName("senderNetworkId")] WebNetworkId SenderNetworkId,
    [property: JsonPropertyName("senderName")] string SenderName,
    [property: JsonPropertyName("text")] string Text);

public sealed record ChatWhisperSentPayload(
    [property: JsonPropertyName("targetNetworkId")] WebNetworkId TargetNetworkId,
    [property: JsonPropertyName("targetName")] string TargetName,
    [property: JsonPropertyName("text")] string Text);
