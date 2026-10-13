using System.Text.Json.Serialization;

namespace Ubiquitous.Protocol.Web;

public sealed record AuctionOpenedPayload(
    [property: JsonPropertyName("networkId")] string NetworkId);

public sealed record AuctionListPayload(
    [property: JsonPropertyName("from")] StorageSlotPayload From,
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("startPrice")] int StartPrice,
    [property: JsonPropertyName("durationHours")] int DurationHours,
    [property: JsonPropertyName("buyoutPrice")] int? BuyoutPrice = null);

public sealed record AuctionListedPayload(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record AuctionBrowsePayload(
    [property: JsonPropertyName("query")] string? Query = null,
    [property: JsonPropertyName("minLevel")] int? MinLevel = null,
    [property: JsonPropertyName("maxLevel")] int? MaxLevel = null,
    [property: JsonPropertyName("kinds")] IReadOnlyList<AuctionKindPayload>? Kinds = null,
    [property: JsonPropertyName("quality")] int? Quality = null,
    [property: JsonPropertyName("orderBy")] string? OrderBy = null,
    [property: JsonPropertyName("ascending")] bool Ascending = false,
    [property: JsonPropertyName("offset")] int Offset = 0,
    [property: JsonPropertyName("limit")] int Limit = 0);

public sealed record AuctionKindPayload(
    [property: JsonPropertyName("itemClass")] int? ItemClass = null,
    [property: JsonPropertyName("subClass")] int? SubClass = null,
    [property: JsonPropertyName("subClassFrom")] int? SubClassFrom = null,
    [property: JsonPropertyName("subClassTo")] int? SubClassTo = null,
    [property: JsonPropertyName("inventoryType")] int? InventoryType = null);

public sealed record AuctionListingEntryPayload(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("sellerName")] string SellerName,
    [property: JsonPropertyName("itemEntry")] int ItemEntry,
    [property: JsonPropertyName("itemCount")] int ItemCount,
    [property: JsonPropertyName("startPrice")] int StartPrice,
    [property: JsonPropertyName("listedAt")] long ListedAtUnixSeconds,
    [property: JsonPropertyName("expiresAt")] long ExpiresAtUnixSeconds,
    [property: JsonPropertyName("buyoutPrice")] int? BuyoutPrice = null,
    [property: JsonPropertyName("currentBid")] int CurrentBid = 0,
    [property: JsonPropertyName("highestBidder")] string? HighestBidderName = null);

public sealed record AuctionListingsPayload(
    [property: JsonPropertyName("listings")] IReadOnlyList<AuctionListingEntryPayload> Listings,
    [property: JsonPropertyName("total")] int Total = 0,
    [property: JsonPropertyName("offset")] int Offset = 0);

public sealed record AuctionCancelPayload(
    [property: JsonPropertyName("id")] string Id);

public sealed record AuctionCancelledPayload(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record AuctionBidPayload(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("amount")] int Amount);

public sealed record AuctionBidPlacedPayload(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("currentBid")] int CurrentBid,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record AuctionBuyoutPayload(
    [property: JsonPropertyName("id")] string Id);

public sealed record AuctionBoughtPayload(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("price")] int Price,
    [property: JsonPropertyName("reason")] string? Reason);
