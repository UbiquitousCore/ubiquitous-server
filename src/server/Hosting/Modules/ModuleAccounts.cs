using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Ubiquitous.Modules;
using Ubiquitous.Persistence;

namespace Ubiquitous.Server;

// Accounts a module signs in with. They live only in memory with a secret drawn
// at startup, so nothing outside the process can sign in as one; their ids are
// negative and derived from the user id, so a module's characters stay on the
// same account across restarts without touching the real account store.
public sealed class ModuleAccounts(IAccountAuthenticator inner) : IAccountAuthenticator, IAccountStore
{
    public const string UserIdPrefix = "module.";

    private readonly ConcurrentDictionary<string, (AccountRecord Account, byte[] Secret)> _reserved =
        new(StringComparer.Ordinal);

    public async Task<ModuleLogin> ReserveAsync(string module, string key, CancellationToken cancellationToken = default)
    {
        var userId = $"{UserIdPrefix}{module}.{key}";
        if (inner is IAccountStore store && await store.FindByUserIdAsync(userId, cancellationToken) is not null)
            throw new InvalidOperationException($"Account {userId} already exists in the account store; a module cannot take it.");

        var id = AccountIdFor(userId);
        if (_reserved.Values.Any(held => held.Account.Id == id && held.Account.UserId != userId))
            throw new InvalidOperationException($"Module account {userId} shares an id with another; choose another key.");

        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _reserved[userId] = (new AccountRecord(id, userId, ""), Encoding.UTF8.GetBytes(secret));
        return new ModuleLogin(userId, secret);
    }

    // Whether this login is one the module reserved, secret and all.
    public bool Holds(string module, ModuleLogin login) =>
        login.UserId.StartsWith($"{UserIdPrefix}{module}.", StringComparison.Ordinal)
        && _reserved.TryGetValue(login.UserId, out var held)
        && CryptographicOperations.FixedTimeEquals(held.Secret, Encoding.UTF8.GetBytes(login.Secret));

    public static int AccountIdFor(string userId)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(userId)) hash = (hash ^ b) * 16777619u;
        return -(int)(hash & 0x3FFF_FFFF) - 1;
    }

    public Task<LoginResult> AuthenticateAsync(string userId, string passHash, CancellationToken cancellationToken = default)
    {
        if (!_reserved.TryGetValue(userId, out var held)) return inner.AuthenticateAsync(userId, passHash, cancellationToken);

        var ok = CryptographicOperations.FixedTimeEquals(held.Secret, Encoding.UTF8.GetBytes(passHash ?? ""));
        return Task.FromResult(ok
            ? new LoginResult(true, LoginCode.Success)
            : new LoginResult(false, LoginCode.InvalidCredentials, "Invalid credentials."));
    }

    public Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (_reserved.TryGetValue(userId, out var held)) return Task.FromResult<AccountRecord?>(held.Account);
        return inner is IAccountStore store ? store.FindByUserIdAsync(userId, cancellationToken) : Task.FromResult<AccountRecord?>(null);
    }

    public Task SetPassHashAsync(int accountId, string passHash, CancellationToken cancellationToken = default) =>
        inner is IAccountStore store && accountId > 0 ? store.SetPassHashAsync(accountId, passHash, cancellationToken) : Task.CompletedTask;
}
