using Microsoft.Extensions.Logging.Abstractions;
using Ubiquitous.Persistence;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class DevelopmentLoginsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"devlogins-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private ServerOptions Options() => ServerOptions.Defaults with
    {
        StateDirectory = _directory,
        DevelopmentAccountsEnabled = true,
        DevelopmentUserId = "dev",
        DevelopmentPassHash = "dev-secret",
    };

    private async Task MakeRealAccountAsync(string userId, string password)
    {
        var admin = SessionStoreFactory.OpenAccountAdmin(Options(), postgres: null);
        await admin.CreateAsync(userId, PasswordHasher.Hash(password, TestSessions.TestPasswordIterations));
    }

    private SessionStores Open() => SessionStoreFactory.Create(Options(), postgres: null, NullLogger.Instance);

    [Fact]
    public async Task TheDevelopmentLogin_StillSignsIn_AfterARealAccountExists()
    {
        await MakeRealAccountAsync("alice", "correct horse battery");
        var stores = Open();

        Assert.True((await stores.Accounts.AuthenticateAsync("dev", "dev-secret")).Ok);
        Assert.True((await stores.Accounts.AuthenticateAsync("alice", "correct horse battery")).Ok);
        Assert.False((await stores.Accounts.AuthenticateAsync("alice", "wrong")).Ok);
    }

    [Fact]
    public async Task TheDevelopmentLogin_IsAGameMaster_AndARealAccountIsNot()
    {
        await MakeRealAccountAsync("alice", "correct horse battery");
        var accounts = (IAccountStore)Open().Accounts;

        Assert.True((await accounts.FindByUserIdAsync("dev"))!.IsAdmin);
        Assert.False((await accounts.FindByUserIdAsync("alice"))!.IsAdmin);
    }

    [Fact]
    public async Task ARealAccount_NeverSharesAnIdWithADevelopmentLogin()
    {
        await MakeRealAccountAsync("alice", "correct horse battery");
        var accounts = (IAccountStore)Open().Accounts;

        var dev = (await accounts.FindByUserIdAsync("dev"))!;
        var alice = (await accounts.FindByUserIdAsync("alice"))!;

        Assert.NotEqual(dev.Id, alice.Id);
        Assert.True(alice.Id >= FileAccountStore.FirstAccountId);
    }

    [Fact]
    public async Task TheDevelopmentCharacters_AreSeededOnce_AndKeepWhatWasSavedSince()
    {
        await MakeRealAccountAsync("alice", "correct horse battery");

        var first = await Open().Characters.ListForAccountAsync(1);
        var changed = first[0] with { Level = 12 };
        await Open().Characters.UpdateAsync(changed);
        var again = await Open().Characters.ListForAccountAsync(1);

        Assert.Equal(2, first.Count);
        Assert.Equal(2, again.Count);
        Assert.Equal(12, again.Single(one => one.Guid == changed.Guid).Level);
    }

    [Fact]
    public async Task WithDevelopmentLoginsOff_OnlyTheRealAccountsSignIn()
    {
        await MakeRealAccountAsync("alice", "correct horse battery");
        var off = Options() with { DevelopmentAccountsEnabled = false };

        var stores = SessionStoreFactory.Create(off, postgres: null, NullLogger.Instance);

        Assert.False((await stores.Accounts.AuthenticateAsync("dev", "dev-secret")).Ok);
        Assert.True((await stores.Accounts.AuthenticateAsync("alice", "correct horse battery")).Ok);
    }
}
