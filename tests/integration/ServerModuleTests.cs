using Ubiquitous.Modules;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class ServerModuleTests
{
    [Fact]
    public void TheServer_FindsTheModulesItWasBuiltWith()
    {
        var modules = ServerModules.BuiltIn();

        Assert.Contains(modules, module => module.Name == "playerbots");
    }

    [Fact]
    public async Task AModuleLogin_SignsInLikeAnyAccountAndOnlyWithItsSecret()
    {
        var accounts = new ModuleAccounts(new AccountAuthenticator(new DevelopmentAccountStore("hero", "secret"), iterations: 1_000));
        var login = await accounts.ReserveAsync("playerbots", "random0");

        Assert.True((await LoginAsync(accounts, login.UserId, login.Secret)).Ok);
        Assert.False((await LoginAsync(accounts, login.UserId, "guess")).Ok);
        Assert.True((await LoginAsync(accounts, "hero", "secret")).Ok);
        Assert.True((await accounts.FindByUserIdAsync(login.UserId))!.Id < 0);
    }

    [Fact]
    public async Task AModuleLogin_KeepsItsAccountIdAcrossRestarts()
    {
        var first = new ModuleAccounts(new AccountAuthenticator(new DevelopmentAccountStore([])));
        var second = new ModuleAccounts(new AccountAuthenticator(new DevelopmentAccountStore([])));

        var before = await first.FindByUserIdAsync((await first.ReserveAsync("playerbots", "random3")).UserId);
        var after = await second.FindByUserIdAsync((await second.ReserveAsync("playerbots", "random3")).UserId);

        Assert.Equal(before!.Id, after!.Id);
    }

    [Fact]
    public async Task AModule_CannotTakeAnAccountThatAlreadyExists()
    {
        var accounts = new ModuleAccounts(new AccountAuthenticator(
            new DevelopmentAccountStore($"{ModuleAccounts.UserIdPrefix}playerbots.random0", "secret")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.ReserveAsync("playerbots", "random0"));
    }

    [Fact]
    public async Task AModule_RaisesOnlyAFreshCharacterOfItsOwnLogin()
    {
        var accounts = new ModuleAccounts(new AccountAuthenticator(new DevelopmentAccountStore([])));
        var login = await accounts.ReserveAsync("playerbots", "random0");
        var other = await accounts.ReserveAsync("other", "random0");
        var account = ModuleAccounts.AccountIdFor(login.UserId);
        CharacterRecord Made(string name, int level = 1, int xp = 0, int? accountId = null) =>
            new(name, accountId ?? account, name, 0, 0, 1, 1684, 1, 1, 1, 1, 1, 0, 0, 0, level, [], Xp: xp, Health: 40);
        var characters = new InMemoryCharacterStore(
            [Made("Fresh"), Made("Played", xp: 30), Made("Elsewhere", accountId: ModuleAccounts.AccountIdFor(other.UserId))]);
        var stores = new SessionStores(accounts, characters);
        var world = new SharedWorld(stores);
        var modules = new ServerModules(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var host = new ModuleHost("playerbots", new ModuleSettings(new Dictionary<string, string>()), modules,
            new PlaySessionFactory(stores, ServerContent.Empty, world), new PlaySessionHub(), accounts, 30,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        Assert.True(await host.StartAtLevelAsync(login, "Fresh", 12));
        Assert.False(await host.StartAtLevelAsync(login, "Played", 12));
        Assert.False(await host.StartAtLevelAsync(login, "Elsewhere", 12));
        Assert.False(await host.StartAtLevelAsync(other, "Fresh", 12));
        Assert.False(await host.StartAtLevelAsync(login with { Secret = "guess" }, "Fresh", 12));

        var raised = (await characters.FindByNameAsync("Fresh"))!;
        Assert.Equal((12, (int?)null), (raised.Level, raised.Health));
        Assert.Equal(1, (await characters.FindByNameAsync("Played"))!.Level);
    }

    [Fact]
    public void ModuleSettings_ComeFromTheConfigFileAndEnvironmentOverridesThem()
    {
        var config = Path.Combine(Path.GetTempPath(), $"ubiquitous-modules-{Guid.NewGuid():N}.json");
        File.WriteAllText(config, """{ "modules": { "playerbots": { "enabled": true, "randomBotCount": 3, "loginsPerTick": 2 } } }""");
        try
        {
            var options = ServerOptions.Load(["--config", config],
                new Dictionary<string, string?> { ["UBIQUITOUS_MODULE_PLAYERBOTS_RANDOM_BOT_COUNT"] = "8" });
            var settings = new ModuleSettings(options.ModuleSettings("playerbots"));

            Assert.True(settings.Flag("enabled", false));
            Assert.Equal(8, settings.Number("randomBotCount", 0));
            Assert.Equal(2, settings.Number("loginsPerTick", 0));
        }
        finally
        {
            File.Delete(config);
        }
    }

    private static async Task<ResultPayload> LoginAsync(ModuleAccounts accounts, string userId, string secret)
    {
        var session = new PlaySession(new SessionStores(accounts, new InMemoryCharacterStore([])), ServerContent.Empty, new SharedWorld());
        var reply = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.AccountLogin, 0, new LoginRequest(userId, secret)))));
        return WebMessageJson.DeserializePayload<ResultPayload>(WebMessageJson.Deserialize(reply).Payload);
    }
}
