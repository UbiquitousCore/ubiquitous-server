namespace Ubiquitous.Common;

public sealed record PersistedMailAttachment(int Entry, int Count);

public sealed record PersistedMailMessage(
    string Id,
    string RecipientGuid,
    string SenderName,
    string Subject,
    string Body,
    int Gold,
    long SentAtUnixSeconds,
    bool Read,
    bool GoldClaimed,
    IReadOnlyList<PersistedMailAttachment>? Attachments = null,
    bool ItemsClaimed = false);

public interface IMailStore
{
    Task<IReadOnlyList<PersistedMailMessage>> ListAsync(string recipientGuid, CancellationToken cancellationToken = default);
    Task SaveAsync(string recipientGuid, IReadOnlyList<PersistedMailMessage> messages, CancellationToken cancellationToken = default);
}
