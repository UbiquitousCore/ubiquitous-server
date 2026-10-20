using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

internal sealed class AuctionHandler(ISessionPlayer player, SessionOutbox outbox, PlayerBelongings belongings, GameContent content, AuctionService auctions)
{
    public async Task<IReadOnlyList<string>> List(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Reply(WebMessageTypes.AuctionListed, new AuctionListedPayload(false, null,
                "Character selection is required before listing an item."))];

        var request = WebMessageJson.DeserializePayload<AuctionListPayload>(payload);
        var item = belongings.Items.Items.FirstOrDefault(candidate => candidate.Id == request.ItemId);
        if (item is null || !string.Equals(request.From.Area, "bag", StringComparison.Ordinal)
            || request.From.Index != item.Slot)
            return [outbox.Reply(WebMessageTypes.AuctionListed, new AuctionListedPayload(false, null,
                "That item is not in your bag."))];

        var result = await auctions.ListAsync(player.Character.Name, player.Character.Guid,
            item.Entry, item.Count, request.StartPrice, request.DurationHours * 3600, request.BuyoutPrice);
        if (result.Code != AuctionResultCode.Ok || result.Listing is null)
            return [outbox.Reply(WebMessageTypes.AuctionListed, new AuctionListedPayload(false, null,
                AuctionRefusalReason(result.Code)))];

        belongings.Items.Destroy(item.Id, new StorageSlotState(request.From.Area, request.From.Index), item.Count);
        await player.SaveInventoryAsync();
        return
        [
            outbox.Reply(WebMessageTypes.AuctionListed, new AuctionListedPayload(true, result.Listing.Id, null)),
            outbox.Reply(WebMessageTypes.InventoryItemRemoved, new InventoryItemRemovedPayload(item.Id)),
        ];
    }

    public async Task<IReadOnlyList<string>> Browse(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before browsing the auction house.")];

        var request = WebMessageJson.DeserializePayload<AuctionBrowsePayload>(payload);
        var page = await auctions.SearchAsync(
            new AuctionQuery(
                request.Query, request.MinLevel, request.MaxLevel,
                request.Kinds is null
                    ? null
                    : [.. request.Kinds.Select(kind => new AuctionKind(
                        kind.ItemClass, kind.SubClass, kind.SubClassFrom,
                        kind.SubClassTo, kind.InventoryType))],
                request.Quality,
                AuctionOrderOf(request.OrderBy), request.Ascending,
                request.Offset, request.Limit),
            ResolveAuctionItemName,
            ResolveAuctionItemFacts);
        return
        [
            outbox.Reply(WebMessageTypes.AuctionListings, new AuctionListingsPayload(
                [.. page.Listings.Select(listing => new AuctionListingEntryPayload(
                    listing.Id, listing.SellerName, listing.ItemEntry, listing.ItemCount,
                    listing.StartPrice, listing.ListedAtUnixSeconds, listing.ExpiresAtUnixSeconds,
                    listing.BuyoutPrice, listing.CurrentBid, listing.HighestBidderName))],
                page.Total, page.Offset)),
        ];
    }

    private static AuctionOrder AuctionOrderOf(string? name) =>
        Enum.TryParse<AuctionOrder>(name, ignoreCase: true, out var order) ? order : AuctionOrder.Newest;

    public async Task<IReadOnlyList<string>> Cancel(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Reply(WebMessageTypes.AuctionCancelled, new AuctionCancelledPayload(false, string.Empty,
                "Character selection is required before cancelling a listing."))];

        var request = WebMessageJson.DeserializePayload<AuctionCancelPayload>(payload);
        if (belongings.Items.FreeSlots(belongings.BagSlots) < 1)
            return [outbox.Reply(WebMessageTypes.AuctionCancelled, new AuctionCancelledPayload(false, request.Id,
                "Your bags are full."))];

        var result = await auctions.CancelAsync(player.Character.Guid, request.Id);
        if (result.Code != AuctionResultCode.Ok || result.Listing is null)
            return [outbox.Reply(WebMessageTypes.AuctionCancelled, new AuctionCancelledPayload(false, request.Id,
                AuctionRefusalReason(result.Code)))];

        var added = belongings.Receive(result.Listing.ItemEntry, result.Listing.ItemCount);
        if (added is null)
        {
            var remainingSeconds = (int)(result.Listing.ExpiresAtUnixSeconds - player.Now.ToUnixTimeSeconds());
            await auctions.ListAsync(player.Character.Name, player.Character.Guid,
                result.Listing.ItemEntry, result.Listing.ItemCount,
                result.Listing.StartPrice, Math.Max(1, remainingSeconds));
            return [outbox.Reply(WebMessageTypes.AuctionCancelled, new AuctionCancelledPayload(false, request.Id,
                "Your bags are full."))];
        }

        await player.SaveInventoryAsync();
        return
        [
            outbox.Reply(WebMessageTypes.AuctionCancelled, new AuctionCancelledPayload(true, request.Id, null)),
            .. added.Select(item => outbox.Reply(WebMessageTypes.InventoryItemAdded, belongings.PayloadFor(item))),
        ];
    }

    private string? ResolveAuctionItemName(int entry) =>
        entry >= 0 && content.Items.TryGetValue((uint)entry, out var definition) ? definition.NameKey : null;

    private AuctionItemFacts? ResolveAuctionItemFacts(int entry) =>
        entry >= 0 && content.Items.TryGetValue((uint)entry, out var definition)
            ? new AuctionItemFacts(definition.Class, definition.SubClass,
                checked((int)definition.RequiredLevel),
                definition.Quality is uint rank ? checked((int)rank) : null,
                checked((int)definition.InventoryType))
            : null;

    public async Task<IReadOnlyList<string>> Bid(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Reply(WebMessageTypes.AuctionBidPlaced, new AuctionBidPlacedPayload(false, string.Empty, 0,
                "Character selection is required before bidding."))];

        var request = WebMessageJson.DeserializePayload<AuctionBidPayload>(payload);
        var peek = await auctions.TryGetAsync(request.Id);
        if (peek is null)
            return [outbox.Reply(WebMessageTypes.AuctionBidPlaced, new AuctionBidPlacedPayload(false, request.Id, 0,
                AuctionRefusalReason(AuctionResultCode.NotFound)))];
        if (string.Equals(peek.SellerGuid, player.Character.Guid, StringComparison.Ordinal))
            return [outbox.Reply(WebMessageTypes.AuctionBidPlaced, new AuctionBidPlacedPayload(false, request.Id, peek.CurrentBid,
                AuctionRefusalReason(AuctionResultCode.SelfBid)))];

        var floor = peek.CurrentBid > 0 ? peek.CurrentBid : peek.StartPrice;
        if (request.Amount <= floor)
            return [outbox.Reply(WebMessageTypes.AuctionBidPlaced, new AuctionBidPlacedPayload(false, request.Id, peek.CurrentBid,
                AuctionRefusalReason(AuctionResultCode.BidTooLow)))];

        var heldByMe = string.Equals(peek.HighestBidderGuid, player.Character.Guid, StringComparison.Ordinal)
            ? peek.CurrentBid : 0;
        if (request.Amount - heldByMe > belongings.Currency)
            return [outbox.Reply(WebMessageTypes.AuctionBidPlaced, new AuctionBidPlacedPayload(false, request.Id, peek.CurrentBid,
                "You do not have that much gold."))];

        var result = await auctions.PlaceBidAsync(player.Character.Name, player.Character.Guid,
            request.Id, request.Amount, peek.CurrentBid, peek.HighestBidderGuid);
        if (result.Code != AuctionResultCode.Ok || result.Listing is null)
            return [outbox.Reply(WebMessageTypes.AuctionBidPlaced, new AuctionBidPlacedPayload(false, request.Id, peek.CurrentBid,
                AuctionRefusalReason(result.Code)))];

        belongings.Currency -= request.Amount - heldByMe;
        return
        [
            outbox.Reply(WebMessageTypes.AuctionBidPlaced, new AuctionBidPlacedPayload(true, request.Id, result.Listing.CurrentBid, null)),
            outbox.Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
                new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = belongings.Currency })),
        ];
    }

    public async Task<IReadOnlyList<string>> Buyout(JsonElement payload)
    {
        if (player.Character is null)
            return [outbox.Reply(WebMessageTypes.AuctionBought, new AuctionBoughtPayload(false, string.Empty, 0,
                "Character selection is required before buying out a listing."))];

        var request = WebMessageJson.DeserializePayload<AuctionBuyoutPayload>(payload);
        var peek = await auctions.TryGetAsync(request.Id);
        if (peek is null)
            return [outbox.Reply(WebMessageTypes.AuctionBought, new AuctionBoughtPayload(false, request.Id, 0,
                AuctionRefusalReason(AuctionResultCode.NotFound)))];
        if (string.Equals(peek.SellerGuid, player.Character.Guid, StringComparison.Ordinal))
            return [outbox.Reply(WebMessageTypes.AuctionBought, new AuctionBoughtPayload(false, request.Id, 0,
                AuctionRefusalReason(AuctionResultCode.SelfBid)))];
        if (!peek.BuyoutPrice.HasValue)
            return [outbox.Reply(WebMessageTypes.AuctionBought, new AuctionBoughtPayload(false, request.Id, 0,
                AuctionRefusalReason(AuctionResultCode.NoBuyout)))];

        var heldByMe = string.Equals(peek.HighestBidderGuid, player.Character.Guid, StringComparison.Ordinal)
            ? peek.CurrentBid : 0;
        if (peek.BuyoutPrice.Value - heldByMe > belongings.Currency)
            return [outbox.Reply(WebMessageTypes.AuctionBought, new AuctionBoughtPayload(false, request.Id, 0,
                "You do not have that much gold."))];

        var result = await auctions.BuyoutAsync(player.Character.Name, player.Character.Guid,
            request.Id, peek.CurrentBid, peek.HighestBidderGuid);
        if (result.Code != AuctionResultCode.Ok || result.Listing?.BuyoutPrice is null)
            return [outbox.Reply(WebMessageTypes.AuctionBought, new AuctionBoughtPayload(false, request.Id, 0,
                AuctionRefusalReason(result.Code)))];

        belongings.Currency -= result.Listing.BuyoutPrice.Value - heldByMe;
        return
        [
            outbox.Reply(WebMessageTypes.AuctionBought, new AuctionBoughtPayload(true, request.Id, result.Listing.BuyoutPrice.Value, null)),
            outbox.Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
                new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = belongings.Currency })),
        ];
    }

    private static string AuctionRefusalReason(AuctionResultCode code) => code switch
    {
        AuctionResultCode.InvalidPrice => "The starting price must be at least 1 copper, and a buyout must clear it.",
        AuctionResultCode.InvalidDuration => "The listing duration must be at least 1 hour.",
        AuctionResultCode.InvalidItem => "That item cannot be listed.",
        AuctionResultCode.NotFound => "That listing does not exist.",
        AuctionResultCode.NotSeller => "You can only cancel your own listings.",
        AuctionResultCode.SelfBid => "You cannot bid on your own listing.",
        AuctionResultCode.BidTooLow => "That bid does not clear the current price.",
        AuctionResultCode.Expired => "That listing has already expired.",
        AuctionResultCode.NoBuyout => "That listing has no buyout price.",
        AuctionResultCode.BidStale => "Someone just bid on that listing — check the new price and bid again.",
        _ => "Auction request refused.",
    };
}
