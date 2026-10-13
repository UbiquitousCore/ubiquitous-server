using Microsoft.Extensions.Time.Testing;
using Ubiquitous.Persistence;

namespace Ubiquitous.Integration.Tests;

public sealed class AccountAuthenticatorTests
{
    private const int Iterations = TestSessions.TestPasswordIterations;

    [Fact]
    public void AHashedPassword_VerifiesOnlyTheSecretItWasMadeFrom()
    {
        var stored = PasswordHasher.Hash("secret", Iterations);

        Assert.StartsWith($"{PasswordHasher.Scheme}${Iterations}$", stored);
        Assert.True(PasswordHasher.Verify("secret", stored));
        Assert.False(PasswordHasher.Verify("Secret", stored));
        Assert.NotEqual(stored, PasswordHasher.Hash("secret", Iterations));
    }

    [Fact]
    public async Task ALegacyStoredValue_IsAcceptedOnceAndThenReplacedByAHash()
    {
        var store = new DevelopmentAccountStore("developer", "legacy-secret");
        var authenticator = new AccountAuthenticator(store, iterations: Iterations);

        Assert.True((await authenticator.AuthenticateAsync("developer", "legacy-secret")).Ok);

        var stored = (await store.FindByUserIdAsync("developer"))!.PassHash;
        Assert.True(PasswordHasher.IsHashed(stored));
        Assert.True((await authenticator.AuthenticateAsync("developer", "legacy-secret")).Ok);
        Assert.False((await authenticator.AuthenticateAsync("developer", "wrong")).Ok);
    }

    [Fact]
    public async Task AWrongPasswordOrUnknownUser_IsRefusedWithInvalidCredentials()
    {
        var authenticator = new AccountAuthenticator(
            new DevelopmentAccountStore("developer", PasswordHasher.Hash("secret", Iterations)), iterations: Iterations);

        var wrong = await authenticator.AuthenticateAsync("developer", "nope");
        var unknown = await authenticator.AuthenticateAsync("nobody", "secret");

        Assert.Equal(LoginCode.InvalidCredentials, wrong.Code);
        Assert.Equal(LoginCode.InvalidCredentials, unknown.Code);
    }

    [Fact]
    public async Task FiveFailuresInAMinute_LockTheUserOutForAMinute()
    {
        var clock = new FakeTimeProvider();
        var authenticator = new AccountAuthenticator(
            new DevelopmentAccountStore("developer", PasswordHasher.Hash("secret", Iterations)), clock, Iterations);

        for (var attempt = 0; attempt < AccountAuthenticator.MaxFailures; attempt++)
            Assert.False((await authenticator.AuthenticateAsync("developer", "wrong")).Ok);

        Assert.False((await authenticator.AuthenticateAsync("developer", "secret")).Ok);

        clock.Advance(AccountAuthenticator.LockoutDuration + TimeSpan.FromSeconds(1));
        Assert.True((await authenticator.AuthenticateAsync("developer", "secret")).Ok);
    }

    [Fact]
    public async Task FailuresSpreadOverMoreThanAMinute_DoNotLockTheUserOut()
    {
        var clock = new FakeTimeProvider();
        var authenticator = new AccountAuthenticator(
            new DevelopmentAccountStore("developer", PasswordHasher.Hash("secret", Iterations)), clock, Iterations);

        for (var attempt = 0; attempt < AccountAuthenticator.MaxFailures * 2; attempt++)
        {
            await authenticator.AuthenticateAsync("developer", "wrong");
            clock.Advance(AccountAuthenticator.FailureWindow / 2);
        }

        Assert.True((await authenticator.AuthenticateAsync("developer", "secret")).Ok);
    }

    [Fact]
    public async Task TheFileStore_RewritesAccountsJsonWithTheUpgradedHash()
    {
        var directory = Directory.CreateTempSubdirectory("ubiquitous-accounts-");
        try
        {
            var path = Path.Combine(directory.FullName, "accounts.json");
            File.WriteAllText(path, "[{\"id\":1,\"userId\":\"developer\",\"passHash\":\"legacy-secret\"}]");
            var authenticator = new AccountAuthenticator(FileAccountStore.Load(path), iterations: Iterations);

            Assert.True((await authenticator.AuthenticateAsync("developer", "legacy-secret")).Ok);

            var reloaded = new AccountAuthenticator(FileAccountStore.Load(path), iterations: Iterations);
            Assert.DoesNotContain("legacy-secret", File.ReadAllText(path));
            Assert.True((await reloaded.AuthenticateAsync("developer", "legacy-secret")).Ok);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DevelopmentSecretsFromTheEnvironment_AreHashedAtStartup()
    {
        var store = new DevelopmentAccountStore("developer", "dev-secret").WithHashedSecrets();

        var stored = (await store.FindByUserIdAsync("developer"))!.PassHash;

        Assert.True(PasswordHasher.IsHashed(stored));
        Assert.DoesNotContain("dev-secret", stored);
    }
}
