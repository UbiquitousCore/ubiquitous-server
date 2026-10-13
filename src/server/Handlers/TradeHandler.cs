using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

internal sealed class TradeHandler(ISessionPlayer player, SessionOutbox outbox, PlayerBelongings belongings, TradeService trades)
{
    private string MyTradeId => player.UnitId;

    public async Task<IReadOnlyList<string>> Request(JsonElement payload)
    {
        if (player.World is null || player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before trading.")];
        var targetId = PayloadIds.Read(payload, "targetId");
        if (targetId is null) return [outbox.Error(WireCodes.ProtocolError.InvalidTarget, "A trade target is required.")];

        var targetSession = player.Hub?.FindByCharacterNetworkId(WebNetworkId.FromString(targetId));
        if (targetSession is null || targetSession.CurrentMapId != player.World.MapId || !player.Sees(targetSession))
            return [outbox.Error(WireCodes.ProtocolError.InvalidTarget, "Player is not visible.")];

        var outcome = trades.Request(MyTradeId, player.World.PlayerPosition, targetId);
        if (outcome == TradeService.RequestOutcome.SelfTrade)
            return [outbox.Error(WireCodes.ProtocolError.InvalidTarget, "You cannot trade with yourself.")];
        if (outcome == TradeService.RequestOutcome.AlreadyTrading)
            return [outbox.Error(WireCodes.ProtocolError.TradeRefused, "Someone here is already trading.")];

        if (player.Hub is not null)
        {
            await player.Hub.DeliverAsync(targetSession, SessionOutbox.Unsequenced(WebMessageTypes.TradeRequested,
                new TradeRequestedPayload(WebNetworkId.FromString(MyTradeId))));
        }
        return [];
    }

    public async Task<IReadOnlyList<string>> Accept()
    {
        if (player.World is null || player.Character is null) return [];
        var result = trades.Accept(MyTradeId, player.World.PlayerPosition);
        switch (result.Outcome)
        {
            case TradeService.AcceptOutcome.NoPendingRequest:
                return [outbox.Error(WireCodes.ProtocolError.TradeRefused, "There is no trade request to accept.")];
            case TradeService.AcceptOutcome.AlreadyTrading:
                return [outbox.Error(WireCodes.ProtocolError.TradeRefused, "Someone here is already trading.")];
            case TradeService.AcceptOutcome.OutOfRange:
                return [outbox.Error(WireCodes.ProtocolError.TradeRefused, "You are too far away to accept that trade.")];
        }

        var partnerId = result.PartnerId!;
        var partnerSession = player.Hub?.FindByCharacterNetworkId(WebNetworkId.FromString(partnerId));
        if (partnerSession is null)
        {
            trades.Cancel(MyTradeId);
            return [outbox.Error(WireCodes.ProtocolError.TradeRefused, "That player is no longer available.")];
        }

        if (player.Hub is not null)
        {
            await player.Hub.DeliverAsync(partnerSession, SessionOutbox.Unsequenced(WebMessageTypes.TradeStarted,
                new TradeStartedPayload(WebNetworkId.FromString(MyTradeId))));
        }
        return [outbox.Reply(WebMessageTypes.TradeStarted, new TradeStartedPayload(WebNetworkId.FromString(partnerId)))];
    }

    public async Task<IReadOnlyList<string>> Decline()
    {
        if (player.Character is null) return [];
        var requesterId = trades.Decline(MyTradeId);
        if (requesterId is null || player.Hub is null) return [];

        var requesterSession = player.Hub.FindByCharacterNetworkId(WebNetworkId.FromString(requesterId));
        if (requesterSession is not null)
        {
            await player.Hub.DeliverAsync(requesterSession, SessionOutbox.Unsequenced(WebMessageTypes.TradeDeclined,
                new TradeDeclinedPayload(WebNetworkId.FromString(MyTradeId))));
        }
        return [];
    }

    public async Task<IReadOnlyList<string>> Offer(JsonElement payload)
    {
        if (player.Character is null) return [];
        var request = WebMessageJson.DeserializePayload<TradeOfferPayload>(payload);
        var itemIds = request.ItemIds ?? [];

        if (itemIds.Distinct().Count() != itemIds.Count || itemIds.Any(id =>
            !belongings.Items.Items.Any(item => item.Id == id && string.Equals(item.Area, "bag", StringComparison.Ordinal))))
        {
            return [outbox.Error(WireCodes.ProtocolError.InvalidTrade, "Offered items must be items you are currently carrying.")];
        }

        var (partner, refusal) = await TradePartnerOrRefuseAsync();
        if (refusal is not null) return refusal;

        var result = trades.SetOffer(MyTradeId, itemIds, request.Gold);
        if (result.Outcome == TradeService.OfferOutcome.NotTrading)
            return [outbox.Error(WireCodes.ProtocolError.TradeRefused, "You are not currently trading.")];
        if (result.Outcome == TradeService.OfferOutcome.CancelledByChange)
            return await SendCancelledAsync(partner!, "offer_changed_after_confirm");

        return await SendUpdateAsync(partner!);
    }

    public async Task<IReadOnlyList<string>> Confirm(JsonElement payload)
    {
        if (player.Character is null) return [];
        var request = WebMessageJson.DeserializePayload<TradeConfirmPayload>(payload);

        var (partner, refusal) = await TradePartnerOrRefuseAsync();
        if (refusal is not null) return refusal;

        var result = trades.Confirm(MyTradeId, request.Confirmed);
        if (result.Outcome == TradeService.ConfirmOutcome.NotTrading)
            return [outbox.Error(WireCodes.ProtocolError.TradeRefused, "You are not currently trading.")];

        return result.Outcome == TradeService.ConfirmOutcome.BothConfirmed
            ? await TryExchangeAsync(partner!)
            : await SendUpdateAsync(partner!);
    }

    public async Task<IReadOnlyList<string>> Cancel()
    {
        if (player.Character is null) return [];
        var partnerId = trades.Cancel(MyTradeId);
        if (partnerId is null) return [];

        if (player.Hub is not null)
        {
            var partnerSession = player.Hub.FindByCharacterNetworkId(WebNetworkId.FromString(partnerId));
            if (partnerSession is not null)
            {
                await player.Hub.DeliverAsync(partnerSession, SessionOutbox.Unsequenced(WebMessageTypes.TradeCancelled,
                    new TradeCancelledPayload(WebNetworkId.FromString(MyTradeId), "cancelled")));
            }
        }

        return [outbox.Reply(WebMessageTypes.TradeCancelled, new TradeCancelledPayload(WebNetworkId.FromString(partnerId), "cancelled"))];
    }

    private async Task<(PlaySession? Partner, IReadOnlyList<string>? Refusal)> TradePartnerOrRefuseAsync()
    {
        var partnerId = trades.PartnerOf(MyTradeId);
        if (partnerId is null) return (null, [outbox.Error(WireCodes.ProtocolError.TradeRefused, "You are not currently trading.")]);

        var partnerSession = player.Hub?.FindByCharacterNetworkId(WebNetworkId.FromString(partnerId));
        var mine = player.World?.PlayerPosition;
        var theirs = partnerSession?.PlayerPosition;
        var inRange = partnerSession is not null && mine is not null && theirs is not null
            && mine.Value.DistanceSquaredTo(theirs.Value) <= TradeService.TradeRange * TradeService.TradeRange;
        if (inRange) return (partnerSession, null);

        trades.Cancel(MyTradeId);
        var toMe = outbox.Reply(WebMessageTypes.TradeCancelled, new TradeCancelledPayload(WebNetworkId.FromString(partnerId), "out_of_range"));
        if (player.Hub is not null && partnerSession is not null)
        {
            await player.Hub.DeliverAsync(partnerSession, SessionOutbox.Unsequenced(WebMessageTypes.TradeCancelled,
                new TradeCancelledPayload(WebNetworkId.FromString(MyTradeId), "out_of_range")));
        }
        return (null, [toMe]);
    }

    private async Task<IReadOnlyList<string>> SendCancelledAsync(PlaySession partner, string reason)
    {
        var myId = MyTradeId;
        var partnerId = partner.UnitId;
        if (player.Hub is not null)
        {
            await player.Hub.DeliverAsync(partner, SessionOutbox.Unsequenced(WebMessageTypes.TradeCancelled,
                new TradeCancelledPayload(WebNetworkId.FromString(myId), reason)));
        }
        return [outbox.Reply(WebMessageTypes.TradeCancelled, new TradeCancelledPayload(WebNetworkId.FromString(partnerId), reason))];
    }

    private async Task<IReadOnlyList<string>> SendUpdateAsync(PlaySession partner)
    {
        var myId = MyTradeId;
        var partnerId = partner.UnitId;
        var mine = trades.OfferOf(myId) ?? TradeService.TradeOffer.Empty;
        var theirs = trades.OfferOf(partnerId) ?? TradeService.TradeOffer.Empty;

        var toMe = outbox.Reply(WebMessageTypes.TradeUpdated, new TradeUpdatedPayload(
            WebNetworkId.FromString(partnerId), theirs.Gold, ItemPayloads(partner.Belongings, theirs.ItemIds), theirs.Confirmed, mine.Confirmed));

        if (player.Hub is not null)
        {
            var toPartner = SessionOutbox.Unsequenced(WebMessageTypes.TradeUpdated, new TradeUpdatedPayload(
                WebNetworkId.FromString(myId), mine.Gold, ItemPayloads(belongings, mine.ItemIds), mine.Confirmed, theirs.Confirmed));
            await player.Hub.DeliverAsync(partner, toPartner);
        }

        return [toMe];
    }

    private async Task<IReadOnlyList<string>> TryExchangeAsync(PlaySession partner)
    {
        var myId = MyTradeId;
        var partnerId = partner.UnitId;
        var mineOffer = trades.OfferOf(myId);
        var theirsOffer = trades.OfferOf(partnerId);
        if (mineOffer is null || theirsOffer is null)
            return await SendCancelledAsync(partner, "trade_refused");

        var failureReason = ExchangeFailureReason(belongings, mineOffer, partner.Belongings, theirsOffer);
        if (failureReason is not null)
        {
            trades.Cancel(myId);
            if (player.Hub is not null)
            {
                await player.Hub.DeliverAsync(partner, SessionOutbox.Unsequenced(WebMessageTypes.TradeCancelled,
                    new TradeCancelledPayload(WebNetworkId.FromString(myId), failureReason)));
            }
            return [outbox.Reply(WebMessageTypes.TradeCancelled, new TradeCancelledPayload(WebNetworkId.FromString(partnerId), failureReason))];
        }

        var myGivenItems = mineOffer.ItemIds.Select(id => belongings.Items.TakeOutBagItem(id)!).ToArray();
        var theirGivenItems = theirsOffer.ItemIds.Select(id => partner.Belongings.Items.TakeOutBagItem(id)!).ToArray();

        belongings.Currency -= mineOffer.Gold;
        partner.Belongings.Currency += mineOffer.Gold;
        partner.Belongings.Currency -= theirsOffer.Gold;
        belongings.Currency += theirsOffer.Gold;

        var myReceivedItems = theirGivenItems.Select(item => belongings.Items.Add(item.Entry, item.Count, belongings.BagSlots)!).ToArray();
        var partnerReceivedItems = myGivenItems.Select(item => partner.Belongings.Items.Add(item.Entry, item.Count, partner.Belongings.BagSlots)!).ToArray();

        trades.Cancel(myId);

        var myResponses = new List<string>();
        myResponses.AddRange(myGivenItems.Select(item => outbox.Reply(WebMessageTypes.InventoryItemRemoved, new InventoryItemRemovedPayload(item.Id))));
        myResponses.AddRange(myReceivedItems.Select(item => outbox.Reply(WebMessageTypes.InventoryItemAdded,
            belongings.PayloadFor(item))));
        myResponses.Add(outbox.Reply(WebMessageTypes.PlayerValuesChanged,
            new PlayerValuesChangedPayload(new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = belongings.Currency })));
        myResponses.Add(outbox.Reply(WebMessageTypes.TradeCompleted, new TradeCompletedPayload(WebNetworkId.FromString(partnerId))));

        if (player.Hub is not null)
        {
            foreach (var item in theirGivenItems)
                await player.Hub.DeliverAsync(partner, SessionOutbox.Unsequenced(WebMessageTypes.InventoryItemRemoved, new InventoryItemRemovedPayload(item.Id)));
            foreach (var item in partnerReceivedItems)
            {
                await player.Hub.DeliverAsync(partner, SessionOutbox.Unsequenced(WebMessageTypes.InventoryItemAdded,
                    belongings.PayloadFor(item)));
            }
            await player.Hub.DeliverAsync(partner, SessionOutbox.Unsequenced(WebMessageTypes.PlayerValuesChanged,
                new PlayerValuesChangedPayload(new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = partner.Belongings.Currency })));
            await player.Hub.DeliverAsync(partner, SessionOutbox.Unsequenced(WebMessageTypes.TradeCompleted,
                new TradeCompletedPayload(WebNetworkId.FromString(myId))));
        }

        return myResponses;
    }

    private static string? ExchangeFailureReason(PlayerBelongings a, TradeService.TradeOffer offerA, PlayerBelongings b, TradeService.TradeOffer offerB)
    {
        if (a.Currency < offerA.Gold || b.Currency < offerB.Gold) return "not_enough_gold";

        if (offerA.ItemIds.Any(id => !a.Items.Items.Any(item => item.Id == id && string.Equals(item.Area, "bag", StringComparison.Ordinal)))
            || offerB.ItemIds.Any(id => !b.Items.Items.Any(item => item.Id == id && string.Equals(item.Area, "bag", StringComparison.Ordinal))))
            return "item_no_longer_available";

        if (a.Items.FreeSlots(a.BagSlots) < offerB.ItemIds.Count
            || b.Items.FreeSlots(b.BagSlots) < offerA.ItemIds.Count)
            return "not_enough_bag_space";

        return null;
    }

    private static IReadOnlyList<InventoryItemPayload> ItemPayloads(PlayerBelongings owner, IReadOnlyList<string> itemIds) =>
        [.. itemIds
            .Select(id => owner.Items.Items.FirstOrDefault(item => item.Id == id))
            .Where(item => item is not null)
            .Select(item => new InventoryItemPayload(
                item!.Id, item.Entry, item.Count, null, owner.PrototypeFor(item.Entry)))];

}
