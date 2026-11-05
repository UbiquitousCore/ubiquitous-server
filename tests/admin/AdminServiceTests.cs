using Ubiquitous.Admin;
using Ubiquitous.Admin.Stores;
using Ubiquitous.Data;
using Ubiquitous.Persistence;

namespace Ubiquitous.Admin.Tests;

public sealed class AdminServiceTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("ubiquitous-admin-");
    private readonly ServerAccounts _accounts;
    private readonly CountingControl _control = new();
    private readonly FixedOnline _online = new();
    private readonly InMemoryCharacterStore _characters;
    private readonly FileInventoryStore _inventory;
    private readonly AdminService _admin;

    public AdminServiceTests()
    {
        var store = FileAccountStore.Open(Path.Combine(_folder.FullName, "accounts.json"), () => { });
        _accounts = new ServerAccounts(store, iterations: 1);
        _characters = new InMemoryCharacterStore([Character("c1", 100, "Ayla"), Character("c2", 101, "Borin")]);
        _inventory = new FileInventoryStore(Path.Combine(_folder.FullName, "inventory"));
        var package = Path.Combine(_folder.FullName, "package");
        DataPackageWriter.WriteSyntheticPackage(package);
        DataPackageWriter.WriteDataset(package, "items", new[]
        {
            new ItemDefinition(Tunic, "Test Tunic", 4, 1, 1),
            new ItemDefinition(Tonic, "Test Tonic", 0, 0, 2, MaxStack: 20),
        });
        var items = new PackageItems(package);
        _admin = new AdminService(
            _accounts, new ServerCharacters(_characters, _inventory, items),
            new EnvFileSettings(Path.Combine(_folder.FullName, ".env")), new FixedStatus(), _control, items, _online);
    }

    public void Dispose() => _folder.Delete(recursive: true);

    [Fact]
    public async Task OnlyAGmSignsIn()
    {
        await _admin.CreateAccountAsync("player", "password1");
        await _admin.CreateAccountAsync("keeper", "password2");
        var keeper = Assert.Single(await _admin.AccountsAsync(), account => account.UserId == "keeper");
        await _admin.SetGmAsync(keeper.Id, true);

        Assert.Null(await _admin.SignInAsync("player", "password1"));
        Assert.Null(await _admin.SignInAsync("keeper", "wrong-password"));
        Assert.Equal("keeper", (await _admin.SignInAsync("keeper", "password2"))?.UserId);
    }

    [Fact]
    public async Task AGmTakenAwayIsNoLongerLetIn()
    {
        await _admin.CreateAccountAsync("keeper", "password2");
        var keeper = Assert.Single(await _admin.AccountsAsync());
        await _admin.SetGmAsync(keeper.Id, true);
        Assert.True(await _admin.IsStillGmAsync("keeper"));

        await _admin.SetGmAsync(keeper.Id, false);

        Assert.False(await _admin.IsStillGmAsync("keeper"));
    }

    [Fact]
    public async Task AccountsFollowTheCommandLinesRules()
    {
        Assert.Equal(AccountChange.BadUserId, await _admin.CreateAccountAsync("a!", "password1"));
        Assert.Equal(AccountChange.WeakPassword, await _admin.CreateAccountAsync("alice", "short"));
        Assert.Equal(AccountChange.Done, await _admin.CreateAccountAsync("alice", "password1"));
        Assert.Equal(AccountChange.Taken, await _admin.CreateAccountAsync("alice", "password1"));
        Assert.Equal(AccountChange.NotFound, await _admin.SetPasswordAsync(9999, "password1"));
    }

    [Fact]
    public async Task ANewPasswordIsTheOneThatSignsIn()
    {
        await _admin.CreateAccountAsync("keeper", "password2");
        var keeper = Assert.Single(await _admin.AccountsAsync());
        await _admin.SetGmAsync(keeper.Id, true);

        await _admin.SetPasswordAsync(keeper.Id, "password3");

        Assert.NotNull(await _admin.SignInAsync("keeper", "password3"));
    }

    [Fact]
    public async Task CharactersAreListedByAccountWithReadableNamesAndCanBeDeleted()
    {
        var ayla = Assert.Single(await _admin.CharactersAsync(100));
        Assert.Equal(("Ayla", "Elf", "Mage"), (ayla.Name, ayla.Race, ayla.Class));

        Assert.Equal(CharacterChange.Done, await _admin.DeleteCharacterAsync("c1"));

        Assert.Empty(await _admin.CharactersAsync(100));
        Assert.Single(await _admin.CharactersAsync(101));
    }

    [Fact]
    public void BadSettingsWriteNothing()
    {
        var result = _admin.SaveSettings(new Dictionary<string, string?>
        {
            ["UBIQUITOUS_XP_RATE"] = "2",
            ["UBIQUITOUS_MODULE_PLAYERBOTS_RANDOM_BOT_COUNT"] = "900",
            ["UBIQUITOUS_POSTGRES_CONNECTION_STRING"] = "Host=elsewhere",
        });

        Assert.False(result.Saved);
        Assert.Equal(["UBIQUITOUS_MODULE_PLAYERBOTS_RANDOM_BOT_COUNT", "UBIQUITOUS_POSTGRES_CONNECTION_STRING"], result.Problems.Keys.Order());
        Assert.False(File.Exists(Path.Combine(_folder.FullName, ".env")));
    }

    [Fact]
    public void GoodSettingsAreSavedAndReadBack()
    {
        Assert.True(_admin.SaveSettings(new Dictionary<string, string?> { ["UBIQUITOUS_XP_RATE"] = "1.5", ["UBIQUITOUS_DISABLED_RACES"] = "none" }).Saved);

        var views = _admin.Settings().ToDictionary(view => view.Setting.Name, view => view.Value);
        Assert.Equal("1.5", views["UBIQUITOUS_XP_RATE"]);
        Assert.Equal("none", views["UBIQUITOUS_DISABLED_RACES"]);
        Assert.Null(views["UBIQUITOUS_SITE"]);
    }

    [Fact]
    public async Task ACharacterInTheWorldIsLeftAlone()
    {
        _online.Names.Add("ayla");

        Assert.Equal(CharacterChange.Online, await _admin.EditCharacterAsync("c1", new CharacterEdit(Level: 10)));
        Assert.Equal(CharacterChange.Online, await _admin.GiveItemAsync("c1", Tonic, 1));
        Assert.Equal(CharacterChange.Online, await _admin.DeleteCharacterAsync("c1"));
        Assert.True((await _admin.CharacterAsync("c1"))!.Value.Online);
        Assert.Equal(1, (await _characters.GetByGuidAsync("c1"))!.Level);
    }

    [Fact]
    public async Task LevelGoldAndNameChangeByTheGamesRules()
    {
        Assert.Equal(CharacterChange.BadLevel, await _admin.EditCharacterAsync("c1", new CharacterEdit(Level: 61)));
        Assert.Equal(CharacterChange.BadName, await _admin.EditCharacterAsync("c1", new CharacterEdit(Name: "A1")));
        Assert.Equal(CharacterChange.NameTaken, await _admin.EditCharacterAsync("c1", new CharacterEdit(Name: "borin")));

        Assert.Equal(CharacterChange.Done, await _admin.EditCharacterAsync("c1", new CharacterEdit(Name: "Aylin", Level: 30, Gold: 12)));

        var saved = (await _characters.GetByGuidAsync("c1"))!;
        Assert.Equal(("Aylin", 30, 0, 12 * Ubiquitous.Game.CoinWords.Gold), (saved.Name, saved.Level, saved.Xp, saved.Currency));
        Assert.Equal(12, (await _admin.CharacterAsync("c1"))!.Value.Detail.Gold);
    }

    [Fact]
    public async Task GivenItemsStackAndStopWhenTheBagIsFull()
    {
        Assert.Equal(CharacterChange.NoSuchItem, await _admin.GiveItemAsync("c1", 999, 1));
        Assert.Equal(CharacterChange.Done, await _admin.GiveItemAsync("c1", Tonic, 30));

        var bag = (await _admin.CharacterAsync("c1"))!.Value.Detail.Bag;
        Assert.Equal([("Test Tonic", 20), ("Test Tonic", 10)], bag.Select(item => (item.Name, item.Count)));

        for (var i = 0; i < 10; i++) Assert.Equal(CharacterChange.Done, await _admin.GiveItemAsync("c1", Tunic, 1));
        Assert.Equal(CharacterChange.BagFull, await _admin.GiveItemAsync("c1", Tunic, 1));
    }

    [Fact]
    public void ItemsAreFoundByNameOrId()
    {
        Assert.Equal(["Test Tonic", "Test Tunic"], _admin.SearchItems("test t").Select(item => item.Name));
        Assert.Equal("Test Tonic", Assert.Single(_admin.SearchItems(Tonic.ToString())).Name);
    }

    private const int Tunic = 3;
    private const int Tonic = 50;

    private sealed class FixedOnline : IOnlinePlayers
    {
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlySet<string>?> NamesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>?>(Names);
    }

    private static CharacterRecord Character(string guid, int account, string name) =>
        new(guid, account, name, 0, Ubiquitous.Data.PlayerRaceIds.Elf, Ubiquitous.Data.PlayerClassIds.Mage, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, []);

    [Fact]
    public async Task RestartAsksTheServerOnce()
    {
        Assert.True(await _admin.RestartServerAsync());

        Assert.Equal(1, _control.Restarts);
    }

    private sealed class CountingControl : IServerControl
    {
        public int Restarts { get; private set; }

        public Task<bool> RestartAsync(CancellationToken cancellationToken = default)
        {
            Restarts++;
            return Task.FromResult(true);
        }
    }

    private sealed class FixedStatus : IServerStatus
    {
        public Task<ServerStatus> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ServerStatus(ServerState.Running));
    }
}
