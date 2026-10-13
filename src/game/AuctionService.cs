using Ubiquitous.Common;

namespace Ubiquitous.Game;

public enum AuctionResultCode
{
    Ok,
    InvalidPrice,
    InvalidDuration,
    InvalidItem,
    NotFound,
    NotSeller,
    SelfBid,
    BidTooLow,
    Expired,
    NoBuyout,
    BidStale,
}

public sealed record AuctionListingSummary(
    string Id, string SellerGuid, string SellerName, int ItemEntry, int ItemCount,
    int StartPrice, long ListedAtUnixSeconds, long ExpiresAtUnixSeconds,
    int? BuyoutPrice = null, int CurrentBid = 0, string? HighestBidderName = null,
    string? HighestBidderGuid = null);

public sealed record AuctionQuery(
    string? Name = null,
    int? MinLevel = null,
    int? MaxLevel = null,
    IReadOnlyList<AuctionKind>? Kinds = null,
    int? Quality = null,
    AuctionOrder OrderBy = AuctionOrder.Newest,
    bool Ascending = false,
    int Offset = 0,
    int Limit = 0);

public sealed record AuctionKind(
    int? ItemClass = null,
    int? SubClass = null,
    int? SubClassFrom = null,
    int? SubClassTo = null,
    int? InventoryType = null)
{
    public bool Matches(AuctionItemFacts facts)
    {
        if (ItemClass is int wanted && facts.ItemClass != wanted) return false;
        if (SubClass is int exactly && facts.SubClass != exactly) return false;
        if (SubClassFrom is int least && facts.SubClass < least) return false;
        if (SubClassTo is int most && facts.SubClass > most) return false;
        if (InventoryType is int worn && facts.InventoryType != worn) return false;
        return ItemClass is not null || SubClass is not null || SubClassFrom is not null
            || SubClassTo is not null || InventoryType is not null;
    }
}

public enum AuctionOrder
{
    Newest = 0,
    Name,
    Level,
    Quality,
    Seller,
    Price,
    Remaining,
}

public sealed record AuctionItemFacts(
    int ItemClass, int SubClass, int RequiredLevel, int? Quality, int InventoryType = 0);

public sealed record AuctionPage(
    IReadOnlyList<AuctionListingSummary> Listings, int Total, int Offset);

public sealed record AuctionListResult(AuctionResultCode Code, AuctionListingSummary? Listing = null);

public sealed record AuctionCancelResult(AuctionResultCode Code, AuctionListingSummary? Listing = null);

public sealed record AuctionBidResult(
    AuctionResultCode Code, AuctionListingSummary? Listing = null,
    string? RefundedBidderName = null, int RefundedAmount = 0);

public sealed record AuctionBuyoutResult(AuctionResultCode Code, AuctionListingSummary? Listing = null);

public sealed class AuctionService
{
    public const int MinDurationSeconds = 3600;

    public const int MaxDurationSeconds = 48 * 3600;

    public const string AuctionHouseSenderGuid = "auction-house";

    public const string AuctionHouseSenderName = "Auction House";

    private readonly ServiceGate _gate = new();
    private readonly IAuctionStore _store;
    private readonly MailService _mail;
    private readonly Func<long> _clock;
    private int _nextId;

    public AuctionService(IAuctionStore store, MailService mail, Func<long>? clock = null)
    {
        _store = store;
        _mail = mail;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public Task<AuctionListResult> ListAsync(string sellerName, string sellerGuid, int itemEntry, int itemCount, int startPrice, int durationSeconds, int? buyoutPrice = null, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => ListAsyncCore(sellerName, sellerGuid, itemEntry, itemCount, startPrice, durationSeconds, buyoutPrice, cancellationToken), cancellationToken);

    private async Task<AuctionListResult> ListAsyncCore(string sellerName, string sellerGuid, int itemEntry, int itemCount, int startPrice, int durationSeconds, int? buyoutPrice, CancellationToken cancellationToken)
    {
        if (itemEntry <= 0 || itemCount <= 0)
            return new AuctionListResult(AuctionResultCode.InvalidItem);
        if (startPrice < 1)
            return new AuctionListResult(AuctionResultCode.InvalidPrice);
        if (buyoutPrice.HasValue && buyoutPrice.Value <= startPrice)
            return new AuctionListResult(AuctionResultCode.InvalidPrice);
        if (durationSeconds < 1)
            return new AuctionListResult(AuctionResultCode.InvalidDuration);

        var now = _clock();
        var listing = new PersistedAuctionListing(
            Id: NextId(),
            SellerGuid: sellerGuid,
            SellerName: sellerName,
            ItemEntry: itemEntry,
            ItemCount: itemCount,
            StartPrice: startPrice,
            ListedAtUnixSeconds: now,
            ExpiresAtUnixSeconds: now + Math.Clamp(durationSeconds, MinDurationSeconds, MaxDurationSeconds),
            BuyoutPrice: buyoutPrice);

        var existing = await _store.ListAllAsync(cancellationToken);
        await _store.SaveAllAsync(existing.Append(listing).ToArray(), cancellationToken);
        return new AuctionListResult(AuctionResultCode.Ok, ToSummary(listing));
    }

    public Task<AuctionListingSummary?> TryGetAsync(string listingId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => TryGetAsyncCore(listingId, cancellationToken), cancellationToken);

    private async Task<AuctionListingSummary?> TryGetAsyncCore(string listingId, CancellationToken cancellationToken)
    {
        await SettleExpiredAsync(cancellationToken);
        var listings = await _store.ListAllAsync(cancellationToken);
        var found = listings.FirstOrDefault(listing =>
            string.Equals(listing.Id, listingId, StringComparison.Ordinal));
        return found is null ? null : ToSummary(found);
    }

    public Task<AuctionPage> SearchAsync(AuctionQuery query, Func<int, string?>? resolveItemName = null, Func<int, AuctionItemFacts?>? describeItem = null, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => SearchAsyncCore(query, resolveItemName, describeItem, cancellationToken), cancellationToken);

    private async Task<AuctionPage> SearchAsyncCore(AuctionQuery query, Func<int, string?>? resolveItemName, Func<int, AuctionItemFacts?>? describeItem, CancellationToken cancellationToken)
    {
        await SettleExpiredAsync(cancellationToken);
        var now = _clock();
        var listings = await _store.ListAllAsync(cancellationToken);
        var matching = listings
            .Where(listing => listing.ExpiresAtUnixSeconds > now)
            .Where(listing => Matches(listing, query, resolveItemName, describeItem))
            .ToList();

        var ordered = Order(matching, query, resolveItemName, describeItem);
        var offset = Math.Max(0, query.Offset);
        var page = query.Limit > 0 ? ordered.Skip(offset).Take(query.Limit) : ordered.Skip(offset);
        return new AuctionPage([.. page.Select(ToSummary)], matching.Count, offset);
    }

    private static bool Matches(PersistedAuctionListing listing, AuctionQuery query,
        Func<int, string?>? resolveItemName, Func<int, AuctionItemFacts?>? describeItem)
    {
        if (!string.IsNullOrWhiteSpace(query.Name)
            && !MatchesQuery(listing, query.Name.Trim(), resolveItemName))
            return false;

        var facts = describeItem?.Invoke(listing.ItemEntry);
        if (facts is null) return true;

        if (query.Kinds is { Count: > 0 } kinds && !kinds.Any(kind => kind.Matches(facts))) return false;
        if (query.MinLevel is int least && facts.RequiredLevel < least) return false;
        if (query.MaxLevel is int most && facts.RequiredLevel > most) return false;
        if (query.Quality is int rank && facts.Quality is int known && known != rank) return false;
        return true;
    }

    private static IEnumerable<PersistedAuctionListing> Order(List<PersistedAuctionListing> rows,
        AuctionQuery query, Func<int, string?>? resolveItemName,
        Func<int, AuctionItemFacts?>? describeItem)
    {
        var up = query.Ascending;
        IOrderedEnumerable<PersistedAuctionListing> by<TKey>(Func<PersistedAuctionListing, TKey> key) =>
            up ? rows.OrderBy(key) : rows.OrderByDescending(key);

        return query.OrderBy switch
        {
            AuctionOrder.Name => by(row => resolveItemName?.Invoke(row.ItemEntry) ?? string.Empty),
            AuctionOrder.Level => by(row => describeItem?.Invoke(row.ItemEntry)?.RequiredLevel ?? 0),
            AuctionOrder.Quality => by(row => describeItem?.Invoke(row.ItemEntry)?.Quality ?? -1),
            AuctionOrder.Seller => by(row => row.SellerName),
            AuctionOrder.Price => by(row => row.CurrentBid > 0 ? row.CurrentBid : row.StartPrice),
            AuctionOrder.Remaining => by(row => row.ExpiresAtUnixSeconds),
            _ => rows.OrderByDescending(row => row.ListedAtUnixSeconds),
        };
    }

    public Task<AuctionBidResult> PlaceBidAsync(string bidderName, string bidderGuid, string listingId, int amount, int expectedCurrentBid, string? expectedHighestBidderGuid, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => PlaceBidAsyncCore(bidderName, bidderGuid, listingId, amount, expectedCurrentBid, expectedHighestBidderGuid, cancellationToken), cancellationToken);

    private async Task<AuctionBidResult> PlaceBidAsyncCore(string bidderName, string bidderGuid, string listingId, int amount, int expectedCurrentBid, string? expectedHighestBidderGuid, CancellationToken cancellationToken)
    {
        await SettleExpiredAsync(cancellationToken);
        var listings = (await _store.ListAllAsync(cancellationToken)).ToList();
        var index = listings.FindIndex(listing =>
            string.Equals(listing.Id, listingId, StringComparison.Ordinal));
        if (index < 0)
            return new AuctionBidResult(AuctionResultCode.NotFound);

        var found = listings[index];
        if (found.ExpiresAtUnixSeconds <= _clock())
            return new AuctionBidResult(AuctionResultCode.Expired);
        if (string.Equals(found.SellerGuid, bidderGuid, StringComparison.Ordinal))
            return new AuctionBidResult(AuctionResultCode.SelfBid);

        var floor = found.CurrentBid > 0 ? found.CurrentBid : found.StartPrice;
        if (amount <= floor)
            return new AuctionBidResult(AuctionResultCode.BidTooLow);
        if (found.CurrentBid != expectedCurrentBid
            || !string.Equals(found.HighestBidderGuid, expectedHighestBidderGuid, StringComparison.Ordinal))
            return new AuctionBidResult(AuctionResultCode.BidStale);

        string? refundedName = null;
        var refundedAmount = 0;
        var outbidGuid = found.CurrentBid > 0 && found.HighestBidderGuid is not null
            && !string.Equals(found.HighestBidderGuid, bidderGuid, StringComparison.Ordinal)
            ? found.HighestBidderGuid
            : null;
        if (outbidGuid is not null)
        {
            refundedName = found.HighestBidderName;
            refundedAmount = found.CurrentBid;
        }

        var updated = found with
        {
            CurrentBid = amount,
            HighestBidderGuid = bidderGuid,
            HighestBidderName = bidderName,
        };
        listings[index] = updated;
        await _store.SaveAllAsync(listings, cancellationToken);
        if (outbidGuid is not null)
        {
            await _mail.SendAsync(AuctionHouseSenderName, AuctionHouseSenderGuid, outbidGuid,
                "Outbid refund", $"You were outbid on {found.SellerName}'s auction. Your bid has been returned.",
                refundedAmount, cancellationToken: cancellationToken);
        }

        return new AuctionBidResult(AuctionResultCode.Ok, ToSummary(updated), refundedName, refundedAmount);
    }

    public Task<AuctionBuyoutResult> BuyoutAsync(string bidderName, string bidderGuid, string listingId, int expectedCurrentBid, string? expectedHighestBidderGuid, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => BuyoutAsyncCore(bidderName, bidderGuid, listingId, expectedCurrentBid, expectedHighestBidderGuid, cancellationToken), cancellationToken);

    private async Task<AuctionBuyoutResult> BuyoutAsyncCore(string bidderName, string bidderGuid, string listingId, int expectedCurrentBid, string? expectedHighestBidderGuid, CancellationToken cancellationToken)
    {
        await SettleExpiredAsync(cancellationToken);
        var listings = (await _store.ListAllAsync(cancellationToken)).ToList();
        var index = listings.FindIndex(listing =>
            string.Equals(listing.Id, listingId, StringComparison.Ordinal));
        if (index < 0)
            return new AuctionBuyoutResult(AuctionResultCode.NotFound);

        var found = listings[index];
        if (found.ExpiresAtUnixSeconds <= _clock())
            return new AuctionBuyoutResult(AuctionResultCode.Expired);
        if (string.Equals(found.SellerGuid, bidderGuid, StringComparison.Ordinal))
            return new AuctionBuyoutResult(AuctionResultCode.SelfBid);
        if (!found.BuyoutPrice.HasValue)
            return new AuctionBuyoutResult(AuctionResultCode.NoBuyout);
        if (found.CurrentBid != expectedCurrentBid
            || !string.Equals(found.HighestBidderGuid, expectedHighestBidderGuid, StringComparison.Ordinal))
            return new AuctionBuyoutResult(AuctionResultCode.BidStale);

        listings.RemoveAt(index);
        await _store.SaveAllAsync(listings, cancellationToken);

        if (found.CurrentBid > 0 && found.HighestBidderGuid is not null
            && !string.Equals(found.HighestBidderGuid, bidderGuid, StringComparison.Ordinal))
        {
            await _mail.SendAsync(AuctionHouseSenderName, AuctionHouseSenderGuid, found.HighestBidderGuid,
                "Outbid refund", $"You were outbid on {found.SellerName}'s auction. Your bid has been returned.",
                found.CurrentBid, cancellationToken: cancellationToken);
        }

        await _mail.SendAsync(AuctionHouseSenderName, AuctionHouseSenderGuid, bidderGuid,
            "Auction won", $"You bought {found.SellerName}'s listing outright. Your item is attached.",
            0, [new MailItemAttachment(found.ItemEntry, found.ItemCount)],
            cancellationToken);
        await _mail.SendAsync(AuctionHouseSenderName, AuctionHouseSenderGuid, found.SellerGuid,
            "Auction sold", $"{bidderName} bought your listing for {CoinWords.Of(found.BuyoutPrice.Value)}.",
            found.BuyoutPrice.Value, cancellationToken: cancellationToken);

        return new AuctionBuyoutResult(AuctionResultCode.Ok, ToSummary(found));
    }

    public Task<AuctionCancelResult> CancelAsync(string sellerGuid, string listingId, CancellationToken cancellationToken = default) =>
        _gate.RunAsync(() => CancelAsyncCore(sellerGuid, listingId, cancellationToken), cancellationToken);

    private async Task<AuctionCancelResult> CancelAsyncCore(string sellerGuid, string listingId, CancellationToken cancellationToken)
    {
        await SettleExpiredAsync(cancellationToken);
        var listings = (await _store.ListAllAsync(cancellationToken)).ToList();
        var index = listings.FindIndex(listing =>
            string.Equals(listing.Id, listingId, StringComparison.Ordinal));
        if (index < 0)
            return new AuctionCancelResult(AuctionResultCode.NotFound);
        if (!string.Equals(listings[index].SellerGuid, sellerGuid, StringComparison.Ordinal))
            return new AuctionCancelResult(AuctionResultCode.NotSeller);

        var removed = listings[index];
        listings.RemoveAt(index);
        await _store.SaveAllAsync(listings, cancellationToken);

        if (removed.CurrentBid > 0 && removed.HighestBidderGuid is not null
            && !string.Equals(removed.HighestBidderGuid, sellerGuid, StringComparison.Ordinal))
        {
            await _mail.SendAsync(AuctionHouseSenderName, AuctionHouseSenderGuid, removed.HighestBidderGuid,
                "Auction cancelled", $"{removed.SellerName} cancelled the auction you were bidding on. Your bid has been returned.",
                removed.CurrentBid, cancellationToken: cancellationToken);
        }

        return new AuctionCancelResult(AuctionResultCode.Ok, ToSummary(removed));
    }

    private async Task SettleExpiredAsync(CancellationToken cancellationToken)
    {
        var now = _clock();
        var listings = (await _store.ListAllAsync(cancellationToken)).ToList();
        var remaining = new List<PersistedAuctionListing>(listings.Count);
        var settled = new List<(PersistedAuctionListing Listing, string WinnerGuid)>();
        foreach (var listing in listings)
        {
            if (listing.ExpiresAtUnixSeconds > now || listing.CurrentBid <= 0 || listing.HighestBidderGuid is not { } winnerGuid)
                remaining.Add(listing);
            else
                settled.Add((listing, winnerGuid));
        }

        if (settled.Count == 0) return;

        await _store.SaveAllAsync(remaining, cancellationToken);
        foreach (var (listing, winnerGuid) in settled)
        {
            await _mail.SendAsync(AuctionHouseSenderName, AuctionHouseSenderGuid, winnerGuid,
                "Auction won", $"You won {listing.SellerName}'s auction. Your item is attached.",
                0, [new MailItemAttachment(listing.ItemEntry, listing.ItemCount)],
                cancellationToken);
            await _mail.SendAsync(AuctionHouseSenderName, AuctionHouseSenderGuid, listing.SellerGuid,
                "Auction sold", $"{listing.HighestBidderName} won your auction for {CoinWords.Of(listing.CurrentBid)}.",
                listing.CurrentBid, cancellationToken: cancellationToken);
        }
    }

    private static bool MatchesQuery(PersistedAuctionListing listing, string needle, Func<int, string?>? resolveItemName)
    {
        var name = resolveItemName?.Invoke(listing.ItemEntry);
        if (!string.IsNullOrEmpty(name) &&
            name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            return true;
        return listing.ItemEntry.ToString().Contains(needle, StringComparison.Ordinal);
    }

    private string NextId() => $"auction-{Guid.NewGuid():N}-{_nextId++}";

    private static AuctionListingSummary ToSummary(PersistedAuctionListing listing) =>
        new(listing.Id, listing.SellerGuid, listing.SellerName, listing.ItemEntry, listing.ItemCount,
            listing.StartPrice, listing.ListedAtUnixSeconds, listing.ExpiresAtUnixSeconds,
            listing.BuyoutPrice, listing.CurrentBid, listing.HighestBidderName, listing.HighestBidderGuid);
}
