using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

internal sealed class MailHandler(ISessionPlayer player, SessionOutbox outbox, PlayerBelongings belongings, GameContent content, ICharacterStore characters, MailService mail)
{
    public async Task<IReadOnlyList<string>> Open(JsonElement payload)
    {
        if (player.World is null) return [outbox.Reply(WebMessageTypes.MailOpened, new MailOpenedPayload(false))];
        var networkId = PayloadIds.NetworkId(payload);
        bool isReachableMailbox = networkId is not null
            && player.InReachOfStaticObject(networkId, content.MailBoxEntries);
        if (!isReachableMailbox) return [outbox.Reply(WebMessageTypes.MailOpened, new MailOpenedPayload(false))];

        var inbox = await mail.ListInboxAsync(player.Character!.Guid);
        return
        [
            outbox.Reply(WebMessageTypes.MailOpened, new MailOpenedPayload(true)),
            outbox.Reply(WebMessageTypes.MailInbox, new MailInboxPayload([.. inbox.Select(ToInboxEntry)])),
        ];
    }

    public async Task<IReadOnlyList<string>> Send(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Reply(WebMessageTypes.MailSent, new MailSentPayload(false, "Character selection is required before sending mail.", WireCodes.MailResult.Refused))];

        var request = WebMessageJson.DeserializePayload<MailSendPayload>(payload);
        if (request.Gold < 0)
            return [SendRefused(MailResultCode.NegativeGold)];
        if (request.Gold > belongings.Currency)
            return [SendRefused(MailResultCode.InsufficientCurrency)];

        var recipient = await characters.FindByNameAsync(request.RecipientName);
        if (recipient is null)
            return [SendRefused(MailResultCode.UnknownRecipient)];

        var attached = (request.ItemIds ?? []).Select(id =>
            belongings.Items.Items.FirstOrDefault(item => item.Id == id && string.Equals(item.Area, "bag", StringComparison.Ordinal))).ToArray();
        if (attached.Any(item => item is null))
            return [outbox.Reply(WebMessageTypes.MailSent, new MailSentPayload(false, "One of those items is no longer in your bag.", WireCodes.MailResult.ItemNotInBag))];

        var result = await mail.SendAsync(player.Character.Name, player.Character.Guid, recipient.Guid,
            request.Subject ?? string.Empty, request.Body, request.Gold,
            [.. attached.Select(item => new MailItemAttachment(item!.Entry, item.Count))]);
        if (result.Code != MailResultCode.Ok)
            return [SendRefused(result.Code)];

        var responses = new List<string> { outbox.Reply(WebMessageTypes.MailSent, new MailSentPayload(true, null)) };
        if (player.Hub?.FindByCharacterName(recipient.Name) is { } online)
        {
            var waiting = await mail.UnreadCountAsync(recipient.Guid);
            await player.Hub.DeliverAsync(online, SessionOutbox.Unsequenced(WebMessageTypes.MailUnread, new MailUnreadPayload(waiting)));
        }

        if (request.Gold > 0)
        {
            belongings.Currency -= request.Gold;
            responses.Add(outbox.Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
                new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = belongings.Currency })));
        }

        foreach (var item in attached)
        {
            belongings.Items.TakeOutBagItem(item!.Id);
            responses.Add(outbox.Reply(WebMessageTypes.InventoryItemRemoved, new InventoryItemRemovedPayload(item.Id)));
        }

        return responses;
    }

    public async Task<IReadOnlyList<string>> Read(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before reading mail.")];

        var request = WebMessageJson.DeserializePayload<MailIdPayload>(payload);
        var result = await mail.ReadAsync(player.Character.Guid, request.Id);
        if (result.Code != MailResultCode.Ok || result.Summary is null)
            return [outbox.Error(WireCodes.ProtocolError.InvalidMail, MailRefusalReason(result.Code))];

        return
        [
            outbox.Reply(WebMessageTypes.MailMessage, new MailMessagePayload(
                result.Summary.Id, result.Summary.SenderName, result.Summary.Subject, result.Body ?? string.Empty,
                result.Summary.Gold, result.Summary.SentAtUnixSeconds, result.Summary.GoldClaimed,
                [.. (result.Items ?? []).Select(item => new MailAttachmentPayload(item.Entry, item.Count))],
                result.Summary.ItemsClaimed)),
            await Unread(),
        ];
    }

    public async Task<string> Unread() =>
        outbox.Reply(WebMessageTypes.MailUnread, new MailUnreadPayload(
            player.Character is null ? 0 : await mail.UnreadCountAsync(player.Character.Guid)));

    public async Task<IReadOnlyList<string>> Claim(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Reply(WebMessageTypes.MailClaimed, new MailClaimedPayload(false, 0, "Character selection is required before claiming mail.", Code: WireCodes.MailResult.Refused))];

        var request = WebMessageJson.DeserializePayload<MailClaimPayload>(payload);
        var take = new MailTake(request.Slot, request.MoneyOnly);
        var result = await mail.ClaimAsync(player.Character.Guid, request.Id, belongings.Items.FreeSlots(belongings.BagSlots), take);
        if (result.Code != MailResultCode.Ok)
            return [outbox.Reply(WebMessageTypes.MailClaimed, new MailClaimedPayload(false, 0, MailRefusalReason(result.Code), Code: CodeFor(result.Code)))];

        var granted = (result.Items ?? []).SelectMany(item => belongings.Receive(item.Entry, item.Count)!).ToArray();
        belongings.Currency += result.GoldClaimed;
        var responses = new List<string>
        {
            outbox.Reply(WebMessageTypes.MailClaimed, new MailClaimedPayload(true, result.GoldClaimed, null,
                [.. (result.Items ?? []).Select(item => new MailAttachmentPayload(item.Entry, item.Count))])),
        };
        responses.AddRange(granted.Select(item => outbox.Reply(WebMessageTypes.InventoryItemAdded, belongings.PayloadFor(item))));
        responses.Add(outbox.Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
            new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = belongings.Currency })));
        // A part taken leaves the letter open with the rest still in it.
        if (take != MailTake.Everything) responses.AddRange(await Read(payload));
        return responses;
    }

    public async Task<IReadOnlyList<string>> Delete(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Reply(WebMessageTypes.MailDeleted, new MailDeletedPayload(false, string.Empty, "Character selection is required before deleting mail."))];

        var request = WebMessageJson.DeserializePayload<MailIdPayload>(payload);
        var result = await mail.DeleteAsync(player.Character.Guid, request.Id);
        return
        [
            outbox.Reply(WebMessageTypes.MailDeleted, new MailDeletedPayload(
                result.Code == MailResultCode.Ok, request.Id,
                result.Code == MailResultCode.Ok ? null : MailRefusalReason(result.Code))),
        ];
    }

    private static MailInboxEntryPayload ToInboxEntry(MailSummary summary) =>
        new(summary.Id, summary.SenderName, summary.Subject, summary.Gold, summary.SentAtUnixSeconds, summary.Read, summary.GoldClaimed,
            summary.ItemCount, summary.ItemsClaimed);

    private string SendRefused(MailResultCode code) =>
        outbox.Reply(WebMessageTypes.MailSent, new MailSentPayload(false, MailRefusalReason(code), CodeFor(code)));

    private static string CodeFor(MailResultCode code) => code switch
    {
        MailResultCode.Ok => WireCodes.MailResult.Ok,
        MailResultCode.SelfMail => WireCodes.MailResult.SelfMail,
        MailResultCode.UnknownRecipient => WireCodes.MailResult.UnknownRecipient,
        MailResultCode.EmptyBody => WireCodes.MailResult.EmptyBody,
        MailResultCode.NegativeGold => WireCodes.MailResult.NegativeGold,
        MailResultCode.InsufficientCurrency => WireCodes.MailResult.NotEnoughMoney,
        MailResultCode.MailNotFound => WireCodes.MailResult.MailNotFound,
        MailResultCode.AlreadyClaimed => WireCodes.MailResult.AlreadyClaimed,
        MailResultCode.NothingToClaim => WireCodes.MailResult.NothingToClaim,
        MailResultCode.BagFull => WireCodes.MailResult.InventoryFull,
        MailResultCode.NotInMail => WireCodes.MailResult.NotInMail,
        _ => WireCodes.MailResult.Refused,
    };

    private static string MailRefusalReason(MailResultCode code) => code switch
    {
        MailResultCode.SelfMail => "You cannot mail yourself.",
        MailResultCode.UnknownRecipient => "No player with that name exists.",
        MailResultCode.EmptyBody => "Mail must have a body.",
        MailResultCode.NegativeGold => "Gold cannot be negative.",
        MailResultCode.InsufficientCurrency => "You do not have that much gold.",
        MailResultCode.MailNotFound => "That message does not exist.",
        MailResultCode.AlreadyClaimed => "That mail was already claimed.",
        MailResultCode.NothingToClaim => "There is nothing to claim.",
        MailResultCode.BagFull => "Your bags are full — free a slot and claim again.",
        MailResultCode.NotInMail => "That item is not in this letter.",
        _ => "Mail request refused.",
    };
}
