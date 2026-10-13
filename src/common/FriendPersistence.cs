namespace Ubiquitous.Common;

public sealed record PersistedSocialEntry(string CharacterGuid, string Name);

public sealed record PersistedSocialList(
    IReadOnlyList<PersistedSocialEntry> Friends,
    IReadOnlyList<PersistedSocialEntry> Ignored)
{
    public static readonly PersistedSocialList Empty = new([], []);
}

public interface IFriendStore
{
    Task<PersistedSocialList> LoadAsync(string ownerGuid, CancellationToken cancellationToken = default);
    Task SaveAsync(string ownerGuid, PersistedSocialList list, CancellationToken cancellationToken = default);
}
