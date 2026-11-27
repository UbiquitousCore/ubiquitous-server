// Kept in the Persistence namespace beside InMemoryCharacterStore, so tests use the pair the same way.
namespace Ubiquitous.Persistence;

public sealed class InMemoryAccountStore : IAccountStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AccountRecord> _accounts;

    public InMemoryAccountStore(string userId, string passHash, int accountId = 1, bool isAdmin = false)
        : this([BuildAccount(userId, passHash, accountId, isAdmin)])
    {
    }

    public InMemoryAccountStore(IReadOnlyList<AccountRecord> accounts) =>
        _accounts = new(accounts.ToDictionary(account => account.UserId, StringComparer.Ordinal), StringComparer.Ordinal);

    private static AccountRecord BuildAccount(string userId, string passHash, int accountId, bool isAdmin)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User ID is required.", nameof(userId));
        return new AccountRecord(accountId, userId, passHash ?? string.Empty, isAdmin);
    }

    public InMemoryAccountStore WithHashedSecrets() =>
        new([.. _accounts.Values.Select(account => PasswordHasher.IsHashed(account.PassHash)
            ? account
            : account with { PassHash = PasswordHasher.Hash(account.PassHash) })]);

    public Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_accounts.TryGetValue(userId, out var account) ? account : null);
    }

    public Task SetPassHashAsync(int accountId, string passHash, CancellationToken cancellationToken = default)
    {
        foreach (var (userId, account) in _accounts)
        {
            if (account.Id == accountId) _accounts[userId] = account with { PassHash = passHash };
        }

        return Task.CompletedTask;
    }
}
