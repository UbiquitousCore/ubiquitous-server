namespace Ubiquitous.Common;

public sealed record PersistedAuctionListing(
    string Id,
    string SellerGuid,
    string SellerName,
    int ItemEntry,
    int ItemCount,
    int StartPrice,
    long ListedAtUnixSeconds,
    long ExpiresAtUnixSeconds,
    int? BuyoutPrice = null,
    int CurrentBid = 0,
    string? HighestBidderGuid = null,
    string? HighestBidderName = null);

public interface IAuctionStore
{
    Task<IReadOnlyList<PersistedAuctionListing>> ListAllAsync(CancellationToken cancellationToken = default);
    Task SaveAllAsync(IReadOnlyList<PersistedAuctionListing> listings, CancellationToken cancellationToken = default);
}
