using Ubiquitous.Persistence;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class AccountCommandsTests : IDisposable
{
    private const int Iterations = TestSessions.TestPasswordIterations;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"accounts-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private IAccountAdmin OpenStore() =>
        SessionStoreFactory.OpenAccountAdmin(ServerOptions.Defaults with { StateDirectory = _directory }, postgres: null);

    private static async Task<(int Code, string Out, string Err)> Run(IAccountAdmin store, string[] args, params string[] typed)
    {
        var answers = new Queue<string>(typed);
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await AccountCommands.RunAsync(
            args, store, _ => answers.Count > 0 ? answers.Dequeue() : null, output, error, Iterations);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task ACreatedAccount_CanSignInWithThePasswordThatWasTyped()
    {
        var store = OpenStore();

        var created = await Run(store, ["create", "alice"], "correct horse", "correct horse");

        Assert.Equal(0, created.Code);
        var authenticator = new AccountAuthenticator(store, iterations: Iterations);
        Assert.True((await authenticator.AuthenticateAsync("alice", "correct horse")).Ok);
        Assert.False((await authenticator.AuthenticateAsync("alice", "wrong horse")).Ok);
    }

    [Fact]
    public async Task ARunningServer_SeesAnAccountCreatedAfterItStarted()
    {
        var server = new AccountAuthenticator(OpenStore(), iterations: Iterations);
        Assert.False((await server.AuthenticateAsync("bob", "hunter2hunter2")).Ok);

        await Run(OpenStore(), ["create", "bob"], "hunter2hunter2", "hunter2hunter2");

        Assert.True((await server.AuthenticateAsync("bob", "hunter2hunter2")).Ok);
    }

    [Fact]
    public async Task CreatingAnAccount_WritesBothFilesSoTheServerStopsUsingTheDevelopmentFixtures()
    {
        await Run(OpenStore(), ["create", "alice"], "correct horse", "correct horse");

        Assert.True(File.Exists(Path.Combine(_directory, SessionStoreFactory.AccountsFile)));
        Assert.True(File.Exists(Path.Combine(_directory, SessionStoreFactory.CharactersFile)));
    }

    [Theory]
    [InlineData("ab", "long enough", "long enough", "user id")]
    [InlineData("carol", "short", "short", "at least")]
    [InlineData("carol", "long enough", "different one", "did not match")]
    public async Task AnAccount_IsRefusedForABadNameOrPassword(string userId, string first, string second, string reason)
    {
        var store = OpenStore();

        var result = await Run(store, ["create", userId], first, second);

        Assert.Equal(1, result.Code);
        Assert.Contains(reason, result.Err);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task ATakenUserId_IsRefusedAndKeepsItsOwnPassword()
    {
        var store = OpenStore();
        await Run(store, ["create", "alice"], "first password", "first password");

        var again = await Run(store, ["create", "alice"], "second password", "second password");

        Assert.Equal(1, again.Code);
        Assert.Contains("already exists", again.Err);
        var authenticator = new AccountAuthenticator(store, iterations: Iterations);
        Assert.True((await authenticator.AuthenticateAsync("alice", "first password")).Ok);
    }

    [Fact]
    public async Task ChangingAPassword_ReplacesTheOldOneAndListShowsEveryAccount()
    {
        var store = OpenStore();
        await Run(store, ["create", "alice"], "first password", "first password");
        await Run(store, ["create", "bob"], "bobs password", "bobs password");

        var changed = await Run(store, ["password", "alice"], "second password", "second password");
        var listed = await Run(store, ["list"]);

        Assert.Equal(0, changed.Code);
        var authenticator = new AccountAuthenticator(store, iterations: Iterations);
        Assert.False((await authenticator.AuthenticateAsync("alice", "first password")).Ok);
        Assert.True((await authenticator.AuthenticateAsync("alice", "second password")).Ok);
        Assert.Equal($"{FileAccountStore.FirstAccountId}\talice{Environment.NewLine}{FileAccountStore.FirstAccountId + 1}\tbob{Environment.NewLine}", listed.Out);
    }

    [Fact]
    public async Task AnUnknownCommandOrAccount_IsRefusedWithoutChangingAnything()
    {
        var store = OpenStore();

        Assert.Equal(2, (await Run(store, ["delete", "alice"])).Code);
        Assert.Equal(1, (await Run(store, ["password", "nobody"], "long enough", "long enough")).Code);
    }

    [Fact]
    public async Task AGameMaster_IsMarkedInTheListAndCanBeTurnedOffAgain()
    {
        var store = OpenStore();
        await Run(store, ["create", "alice"], "first password", "first password");

        Assert.Equal(0, (await Run(store, ["gm", "alice", "on"])).Code);
        Assert.True((await store.FindByUserIdAsync("alice"))!.IsAdmin);
        Assert.Equal($"{FileAccountStore.FirstAccountId}\talice\tGM{Environment.NewLine}", (await Run(store, ["list"])).Out);

        await Run(store, ["gm", "alice", "off"]);
        Assert.False((await store.FindByUserIdAsync("alice"))!.IsAdmin);
    }

    [Fact]
    public async Task GameMasterStatus_SurvivesAPasswordChange()
    {
        var store = OpenStore();
        await Run(store, ["create", "alice"], "first password", "first password");
        await Run(store, ["gm", "alice", "on"]);

        await Run(store, ["password", "alice"], "second password", "second password");

        Assert.True((await store.FindByUserIdAsync("alice"))!.IsAdmin);
    }

    [Fact]
    public async Task MakingAnUnknownAccountAGameMaster_IsRefused()
    {
        var result = await Run(OpenStore(), ["gm", "nobody", "on"]);

        Assert.Equal(1, result.Code);
    }

    [Fact]
    public void DevelopmentAccounts_AreSwitchedOffWhenThePostgresStoreIsInUse()
    {
        var options = ServerOptions.Defaults with { DevelopmentAccountsEnabled = true };

        Assert.False(options.ForStore(usesPostgres: true).DevelopmentAccountsEnabled);
        Assert.True(options.ForStore(usesPostgres: false).DevelopmentAccountsEnabled);
    }

    [Fact]
    public async Task ListingAndFailedCommands_LeaveTheStateDirectoryAlone()
    {
        var store = OpenStore();

        await Run(store, ["list"]);
        await Run(store, ["gm", "nobody", "on"]);
        await Run(store, ["create", "ab"], "long enough", "long enough");

        Assert.False(Directory.Exists(_directory) && Directory.EnumerateFileSystemEntries(_directory).Any());
    }

    [Fact]
    public void TheCommandWords_StopAtTheFirstOption()
    {
        Assert.Equal(["create", "alice"], AccountCommands.Words(["create", "alice", "--config", "server.json"]));
        Assert.Equal(["list"], AccountCommands.Words(["list"]));
        Assert.Empty(AccountCommands.Words(["--config", "server.json"]));
    }
}
