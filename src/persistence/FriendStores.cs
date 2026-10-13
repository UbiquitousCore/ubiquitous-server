using Ubiquitous.Common;

namespace Ubiquitous.Persistence;

public sealed class FileFriendStore(string directory) : IFriendStore
{
    public async Task<PersistedSocialList> LoadAsync(string ownerGuid, CancellationToken cancellationToken = default) =>
        await JsonFile.ReadAsync<PersistedSocialList>(PathFor(ownerGuid), cancellationToken) ?? PersistedSocialList.Empty;

    public Task SaveAsync(string ownerGuid, PersistedSocialList list, CancellationToken cancellationToken = default) =>
        JsonFile.WriteAsync(PathFor(ownerGuid), list, cancellationToken);

    private string PathFor(string ownerGuid) => Path.Combine(directory, $"friends-{ownerGuid}.json");
}

public sealed class InMemoryFriendStore : IFriendStore
{
    private readonly Dictionary<string, PersistedSocialList> _byOwner = new(StringComparer.Ordinal);

    public Task<PersistedSocialList> LoadAsync(string ownerGuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_byOwner.TryGetValue(ownerGuid, out var list) ? list : PersistedSocialList.Empty);
    }

    public Task SaveAsync(string ownerGuid, PersistedSocialList list, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _byOwner[ownerGuid] = list;
        return Task.CompletedTask;
    }
}
