using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record MailOpenedPayload(
    [property: JsonPropertyName("open")] bool Open);

public sealed record MailInboxEntryPayload(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("senderName")] string SenderName,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("gold")] int Gold,
    [property: JsonPropertyName("sentAt")] long SentAtUnixSeconds,
    [property: JsonPropertyName("read")] bool Read,
    [property: JsonPropertyName("goldClaimed")] bool GoldClaimed,
    [property: JsonPropertyName("itemCount")] int ItemCount = 0,
    [property: JsonPropertyName("itemsClaimed")] bool ItemsClaimed = false);

public sealed record MailInboxPayload(
    [property: JsonPropertyName("messages")] IReadOnlyList<MailInboxEntryPayload> Messages);

public sealed record MailSendPayload(
    [property: JsonPropertyName("recipientName")] string RecipientName,
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("gold")] int Gold,
    [property: JsonPropertyName("itemIds")] IReadOnlyList<string>? ItemIds = null);

public sealed record MailSentPayload(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record MailIdPayload(
    [property: JsonPropertyName("id")] string Id);

public sealed record MailAttachmentPayload(
    [property: JsonPropertyName("entry")] int Entry,
    [property: JsonPropertyName("count")] int Count);

public sealed record MailMessagePayload(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("senderName")] string SenderName,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("gold")] int Gold,
    [property: JsonPropertyName("sentAt")] long SentAtUnixSeconds,
    [property: JsonPropertyName("goldClaimed")] bool GoldClaimed,
    [property: JsonPropertyName("items")] IReadOnlyList<MailAttachmentPayload>? Items = null,
    [property: JsonPropertyName("itemsClaimed")] bool ItemsClaimed = false);

public sealed record MailClaimedPayload(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("gold")] int Gold,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("items")] IReadOnlyList<MailAttachmentPayload>? Items = null);

public sealed record MailDeletedPayload(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("reason")] string? Reason);
