using Ubiquitous.Persistence;

namespace Ubiquitous.Server;

// The development logins stand in front of the real accounts, so they are there whether or not anyone has made an account.
public sealed class DevelopmentOverlayAccountStore(IAccountStore development, IAccountStore real) : IAccountStore
{
    public async Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        await development.FindByUserIdAsync(userId, cancellationToken)
        ?? await real.FindByUserIdAsync(userId, cancellationToken);

    // Real accounts are numbered from `FileAccountStore.FirstAccountId`, above every development id.
    public Task SetPassHashAsync(int accountId, string passHash, CancellationToken cancellationToken = default) =>
        accountId < FileAccountStore.FirstAccountId
            ? development.SetPassHashAsync(accountId, passHash, cancellationToken)
            : real.SetPassHashAsync(accountId, passHash, cancellationToken);
}
