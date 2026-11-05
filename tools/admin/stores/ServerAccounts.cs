using Ubiquitous.Persistence;

namespace Ubiquitous.Admin.Stores;

public sealed class ServerAccounts(IAccountAdmin store, int iterations = PasswordHasher.DefaultIterations) : IAccountDirectory
{
    private readonly AccountAuthenticator _signIn = new(store, iterations: iterations);

    public async Task<IReadOnlyList<AdminAccount>> ListAsync(CancellationToken cancellationToken = default) =>
        [.. (await store.ListAsync(cancellationToken)).OrderBy(account => account.Id).Select(ToAdmin)];

    public async Task<AdminAccount?> FindAsync(string userId, CancellationToken cancellationToken = default) =>
        await store.FindByUserIdAsync(userId, cancellationToken) is { } account ? ToAdmin(account) : null;

    public async Task<bool> PasswordMatchesAsync(string userId, string password, CancellationToken cancellationToken = default) =>
        (await _signIn.AuthenticateAsync(userId, password, cancellationToken)).Ok;

    public async Task<AccountChange> CreateAsync(string userId, string password, CancellationToken cancellationToken = default)
    {
        if (!AccountRules.IsValidUserId(userId)) return AccountChange.BadUserId;
        if (!AccountRules.IsStrongEnough(password)) return AccountChange.WeakPassword;
        if (await store.FindByUserIdAsync(userId, cancellationToken) is not null) return AccountChange.Taken;
        return await store.CreateAsync(userId, PasswordHasher.Hash(password, iterations), cancellationToken) is null
            ? AccountChange.Taken
            : AccountChange.Done;
    }

    public async Task<AccountChange> SetPasswordAsync(int accountId, string password, CancellationToken cancellationToken = default)
    {
        if (!AccountRules.IsStrongEnough(password)) return AccountChange.WeakPassword;
        if (!await ExistsAsync(accountId, cancellationToken)) return AccountChange.NotFound;
        await store.SetPassHashAsync(accountId, PasswordHasher.Hash(password, iterations), cancellationToken);
        return AccountChange.Done;
    }

    public async Task<AccountChange> SetGmAsync(int accountId, bool isGm, CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(accountId, cancellationToken)) return AccountChange.NotFound;
        await store.SetAdminAsync(accountId, isGm, cancellationToken);
        return AccountChange.Done;
    }

    private async Task<bool> ExistsAsync(int accountId, CancellationToken cancellationToken) =>
        (await store.ListAsync(cancellationToken)).Any(account => account.Id == accountId);

    private static AdminAccount ToAdmin(AccountRecord account) => new(account.Id, account.UserId, account.IsAdmin);
}
