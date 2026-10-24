namespace Ubiquitous.Persistence;

public sealed class AccountAuthenticator : IAccountAuthenticator, IAccountStore
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(1);

    private const int MostTrackedUsers = 10_000;

    private sealed class Attempts
    {
        public readonly List<DateTimeOffset> Failures = [];
        public DateTimeOffset? LockedUntil;
    }

    private readonly IAccountStore _store;
    private readonly TimeProvider _time;
    private readonly int _iterations;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Attempts> _attempts = new(StringComparer.Ordinal);

    public AccountAuthenticator(IAccountStore store, TimeProvider? time = null, int iterations = PasswordHasher.DefaultIterations)
    {
        _store = store;
        _time = time ?? TimeProvider.System;
        _iterations = iterations;
    }

    public Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        _store.FindByUserIdAsync(userId, cancellationToken);

    public Task SetPassHashAsync(int accountId, string passHash, CancellationToken cancellationToken = default) =>
        _store.SetPassHashAsync(accountId, passHash, cancellationToken);

    public async Task<LoginResult> AuthenticateAsync(string userId, string passHash, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        if (IsLockedOut(userId, now)) return Refused();

        var account = await _store.FindByUserIdAsync(userId, cancellationToken);
        if (account is null || !PasswordHasher.Verify(passHash, account.PassHash))
        {
            RecordFailure(userId, now);
            return Refused();
        }

        ForgetFailures(userId);
        if (!PasswordHasher.IsHashed(account.PassHash))
            await _store.SetPassHashAsync(account.Id, PasswordHasher.Hash(passHash, _iterations), cancellationToken);
        return new LoginResult(true, LoginCode.Success);
    }

    public const string RefusedReason = "This username does not exist, or the password is incorrect.";

    private static LoginResult Refused() => new(false, LoginCode.InvalidCredentials, RefusedReason);

    private bool IsLockedOut(string userId, DateTimeOffset now)
    {
        lock (_gate)
            return _attempts.TryGetValue(userId, out var attempts) && attempts.LockedUntil is { } until && until > now;
    }

    private void RecordFailure(string userId, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_attempts.Count >= MostTrackedUsers) Prune(now);
            if (!_attempts.TryGetValue(userId, out var attempts)) _attempts[userId] = attempts = new Attempts();
            attempts.Failures.RemoveAll(at => now - at > FailureWindow);
            attempts.Failures.Add(now);
            if (attempts.Failures.Count < MaxFailures) return;
            attempts.LockedUntil = now + LockoutDuration;
            attempts.Failures.Clear();
        }
    }

    private void ForgetFailures(string userId)
    {
        lock (_gate) _attempts.Remove(userId);
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (user, attempts) in _attempts.ToArray())
        {
            attempts.Failures.RemoveAll(at => now - at > FailureWindow);
            if (attempts.Failures.Count == 0 && (attempts.LockedUntil is null || attempts.LockedUntil <= now))
                _attempts.Remove(user);
        }
    }
}
