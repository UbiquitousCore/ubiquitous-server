using Ubiquitous.Common;

namespace Ubiquitous.Game;

public enum SocialListKind { Friend, Ignore }

public sealed record SocialEntry(string CharacterGuid, string Name);

public sealed record SocialListState(IReadOnlyList<SocialEntry> Friends, IReadOnlyList<SocialEntry> Ignored);

public enum SocialAddOutcome { Ok, SelfTarget }

public sealed record SocialAddResult(SocialAddOutcome Outcome, SocialListState? List = null);

public sealed class FriendService
{
    private readonly IFriendStore _store;

    public FriendService(IFriendStore store) => _store = store;

    public async Task<SocialListState> ListAsync(string ownerGuid, CancellationToken cancellationToken = default) =>
        ToState(await _store.LoadAsync(ownerGuid, cancellationToken));

    public async Task<SocialAddResult> AddAsync(string ownerGuid, SocialListKind kind, string targetGuid, string targetName,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(ownerGuid, targetGuid, StringComparison.Ordinal))
            return new SocialAddResult(SocialAddOutcome.SelfTarget);

        var persisted = await _store.LoadAsync(ownerGuid, cancellationToken);
        var list = Select(persisted, kind);
        if (list.Any(entry => string.Equals(entry.CharacterGuid, targetGuid, StringComparison.Ordinal)))
            return new SocialAddResult(SocialAddOutcome.Ok, ToState(persisted));

        var updated = With(persisted, kind, [.. list, new PersistedSocialEntry(targetGuid, targetName)]);
        await _store.SaveAsync(ownerGuid, updated, cancellationToken);
        return new SocialAddResult(SocialAddOutcome.Ok, ToState(updated));
    }

    public async Task<SocialListState> RemoveAsync(string ownerGuid, SocialListKind kind, string targetGuid,
        CancellationToken cancellationToken = default)
    {
        var persisted = await _store.LoadAsync(ownerGuid, cancellationToken);
        var list = Select(persisted, kind);
        var filtered = list.Where(entry => !string.Equals(entry.CharacterGuid, targetGuid, StringComparison.Ordinal)).ToArray();
        if (filtered.Length == list.Count) return ToState(persisted);

        var updated = With(persisted, kind, filtered);
        await _store.SaveAsync(ownerGuid, updated, cancellationToken);
        return ToState(updated);
    }

    private static IReadOnlyList<PersistedSocialEntry> Select(PersistedSocialList list, SocialListKind kind) =>
        kind == SocialListKind.Friend ? list.Friends : list.Ignored;

    private static PersistedSocialList With(PersistedSocialList list, SocialListKind kind, IReadOnlyList<PersistedSocialEntry> entries) =>
        kind == SocialListKind.Friend ? list with { Friends = entries } : list with { Ignored = entries };

    private static SocialListState ToState(PersistedSocialList list) =>
        new([.. list.Friends.Select(ToEntry)], [.. list.Ignored.Select(ToEntry)]);

    private static SocialEntry ToEntry(PersistedSocialEntry entry) => new(entry.CharacterGuid, entry.Name);
}
