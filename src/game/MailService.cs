using Ubiquitous.Common;

namespace Ubiquitous.Game;

public enum MailResultCode
{
    Ok,
    SelfMail,
    UnknownRecipient,
    EmptyBody,
    NegativeGold,
    InsufficientCurrency,
    MailNotFound,
    AlreadyClaimed,
    NothingToClaim,
    BagFull,
}

public sealed record MailSummary(string Id, string SenderName, string Subject, int Gold, long SentAtUnixSeconds, bool Read, bool GoldClaimed,
    int ItemCount = 0, bool ItemsClaimed = false);

public sealed record MailItemAttachment(int Entry, int Count);

public sealed record MailSendResult(MailResultCode Code, MailSummary? Sent = null);

public sealed record MailReadResult(MailResultCode Code, MailSummary? Summary = null, string? Body = null,
    IReadOnlyList<MailItemAttachment>? Items = null);

public sealed record MailClaimResult(MailResultCode Code, int GoldClaimed = 0,
    IReadOnlyList<MailItemAttachment>? Items = null);

public sealed record MailDeleteResult(MailResultCode Code);

public sealed class MailService
{
    private readonly ServiceGate _gate = new();
    private readonly IMailStore _store;
    private readonly Func<long> _clock;
    private int _nextId;

    public MailService(IMailStore store, Func<long>? clock = null)
    {
        _store = store;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public Task<MailSendResult> SendAsync(string senderName, string senderGuid, string recipientGuid, string subject, string body, int gold, IReadOnlyList<MailItemAttachment>? attachments = null, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => SendAsyncCore(senderName, senderGuid, recipientGuid, subject, body, gold, attachments, cancellationToken), cancellationToken);

    private async Task<MailSendResult> SendAsyncCore(string senderName, string senderGuid, string recipientGuid, string subject, string body, int gold, IReadOnlyList<MailItemAttachment>? attachments, CancellationToken cancellationToken)
    {
        if (string.Equals(senderGuid, recipientGuid, StringComparison.Ordinal))
            return new MailSendResult(MailResultCode.SelfMail);
        if (string.IsNullOrWhiteSpace(body))
            return new MailSendResult(MailResultCode.EmptyBody);
        if (gold < 0)
            return new MailSendResult(MailResultCode.NegativeGold);

        var existing = await _store.ListAsync(recipientGuid, cancellationToken);
        var persisted = (attachments ?? []).Where(item => item.Count > 0)
            .Select(item => new PersistedMailAttachment(item.Entry, item.Count)).ToArray();
        var message = new PersistedMailMessage(
            Id: NextId(),
            RecipientGuid: recipientGuid,
            SenderName: senderName,
            Subject: subject ?? string.Empty,
            Body: body,
            Gold: gold,
            SentAtUnixSeconds: _clock(),
            Read: false,
            GoldClaimed: false,
            Attachments: persisted,
            ItemsClaimed: false);

        var updated = existing.Append(message).ToArray();
        await _store.SaveAsync(recipientGuid, updated, cancellationToken);
        return new MailSendResult(MailResultCode.Ok, ToSummary(message));
    }

    public Task<IReadOnlyList<MailSummary>> ListInboxAsync(string recipientGuid, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => ListInboxAsyncCore(recipientGuid, cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<MailSummary>> ListInboxAsyncCore(string recipientGuid, CancellationToken cancellationToken)
    {
        var messages = await _store.ListAsync(recipientGuid, cancellationToken);
        return [.. messages.OrderByDescending(m => m.SentAtUnixSeconds).Select(ToSummary)];
    }

    public Task<MailReadResult> ReadAsync(string recipientGuid, string mailId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => ReadAsyncCore(recipientGuid, mailId, cancellationToken), cancellationToken);

    private async Task<MailReadResult> ReadAsyncCore(string recipientGuid, string mailId, CancellationToken cancellationToken)
    {
        var messages = await _store.ListAsync(recipientGuid, cancellationToken);
        var index = messages.ToList().FindIndex(m => string.Equals(m.Id, mailId, StringComparison.Ordinal));
        if (index < 0) return new MailReadResult(MailResultCode.MailNotFound);

        var list = messages.ToList();
        var found = list[index];
        if (!found.Read)
        {
            found = found with { Read = true };
            list[index] = found;
            await _store.SaveAsync(recipientGuid, list, cancellationToken);
        }

        var items = (found.Attachments ?? []).Select(item => new MailItemAttachment(item.Entry, item.Count)).ToArray();
        return new MailReadResult(MailResultCode.Ok, ToSummary(found), found.Body, items);
    }

    public Task<MailClaimResult> ClaimAsync(string recipientGuid, string mailId, int freeSlots, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => ClaimAsyncCore(recipientGuid, mailId, freeSlots, cancellationToken), cancellationToken);

    private async Task<MailClaimResult> ClaimAsyncCore(string recipientGuid, string mailId, int freeSlots, CancellationToken cancellationToken)
    {
        var messages = await _store.ListAsync(recipientGuid, cancellationToken);
        var list = messages.ToList();
        var index = list.FindIndex(m => string.Equals(m.Id, mailId, StringComparison.Ordinal));
        if (index < 0) return new MailClaimResult(MailResultCode.MailNotFound);

        var found = list[index];
        var attachments = found.Attachments ?? [];
        var goldToClaim = !found.GoldClaimed && found.Gold > 0 ? found.Gold : 0;
        var itemsToClaim = !found.ItemsClaimed && attachments.Count > 0
            ? attachments.Select(item => new MailItemAttachment(item.Entry, item.Count)).ToArray()
            : Array.Empty<MailItemAttachment>();

        if (goldToClaim == 0 && itemsToClaim.Length == 0)
        {
            var everCarriedAnything = found.Gold > 0 || attachments.Count > 0;
            return new MailClaimResult(everCarriedAnything ? MailResultCode.AlreadyClaimed : MailResultCode.NothingToClaim);
        }

        if (itemsToClaim.Length > freeSlots) return new MailClaimResult(MailResultCode.BagFull);

        list[index] = found with { GoldClaimed = true, ItemsClaimed = true, Read = true };
        await _store.SaveAsync(recipientGuid, list, cancellationToken);
        return new MailClaimResult(MailResultCode.Ok, goldToClaim, itemsToClaim);
    }

    public Task<MailDeleteResult> DeleteAsync(string recipientGuid, string mailId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => DeleteAsyncCore(recipientGuid, mailId, cancellationToken), cancellationToken);

    private async Task<MailDeleteResult> DeleteAsyncCore(string recipientGuid, string mailId, CancellationToken cancellationToken)
    {
        var messages = await _store.ListAsync(recipientGuid, cancellationToken);
        var list = messages.ToList();
        var index = list.FindIndex(m => string.Equals(m.Id, mailId, StringComparison.Ordinal));
        if (index < 0) return new MailDeleteResult(MailResultCode.MailNotFound);

        list.RemoveAt(index);
        await _store.SaveAsync(recipientGuid, list, cancellationToken);
        return new MailDeleteResult(MailResultCode.Ok);
    }

    private string NextId() => $"mail-{Guid.NewGuid():N}-{_nextId++}";

    private static MailSummary ToSummary(PersistedMailMessage message) =>
        new(message.Id, message.SenderName, message.Subject, message.Gold, message.SentAtUnixSeconds, message.Read, message.GoldClaimed,
            (message.Attachments ?? []).Count, message.ItemsClaimed);
}
