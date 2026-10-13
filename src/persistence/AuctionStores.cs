using Ubiquitous.Common;

namespace Ubiquitous.Persistence;

public sealed class FileAuctionStore(string directory) : IAuctionStore
{
    private readonly string _path = Path.Combine(directory, "auctions.json");

    public async Task<IReadOnlyList<PersistedAuctionListing>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await JsonFile.ReadAsync<List<PersistedAuctionListing>>(_path, cancellationToken) ?? [];

    public Task SaveAllAsync(IReadOnlyList<PersistedAuctionListing> listings, CancellationToken cancellationToken = default) =>
        JsonFile.WriteAsync(_path, listings, cancellationToken);
}

public sealed class InMemoryAuctionStore : IAuctionStore
{
    private List<PersistedAuctionListing> _listings = new();

    public Task<IReadOnlyList<PersistedAuctionListing>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PersistedAuctionListing> result = _listings.ToArray();
        return Task.FromResult(result);
    }

    public Task SaveAllAsync(IReadOnlyList<PersistedAuctionListing> listings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _listings = listings.ToList();
        return Task.CompletedTask;
    }
}
