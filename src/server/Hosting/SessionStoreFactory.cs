using Microsoft.Extensions.Logging;
using Npgsql;
using Ubiquitous.Persistence;

namespace Ubiquitous.Server;

public static class SessionStoreFactory
{
    public const string AccountsFile = "accounts.json";
    public const string CharactersFile = "characters.json";

    public static SessionStores Create(ServerOptions options, NpgsqlDataSource? postgres, ILogger logger)
    {
        string Under(string name) => Path.Combine(options.ResolvedStateDirectory, name);

        var (accounts, characters) = postgres is not null
            ? (new AccountAuthenticator(new PostgresAccountStore(postgres)), (ICharacterStore)new PostgresCharacterStore(postgres))
            : LoadAccountsAndCharacters(options, logger);

        return new SessionStores(
            accounts,
            characters,
            Inventory: postgres is not null ? new PostgresInventoryStore(postgres) : new FileInventoryStore(Under("inventory")),
            QuestLog: postgres is not null ? new PostgresQuestLogStore(postgres) : new FileQuestLogStore(Under("quests")),
            Mail: new FileMailStore(Under("mail")),
            Auction: new FileAuctionStore(Under("auctions")),
            Guild: new FileGuildStore(Under("guilds")),
            Friend: new FileFriendStore(Under("friends")),
            Report: new FileReportStore(Under("reports")),
            Progress: postgres is not null ? new PostgresProgressStore(postgres) : null);
    }

    // Real accounts need both files: with either missing the server falls back to the development fixtures.
    public static IAccountAdmin OpenAccountAdmin(ServerOptions options, NpgsqlDataSource? postgres)
    {
        if (postgres is not null) return new PostgresAccountStore(postgres);
        var characters = Path.Combine(options.ResolvedStateDirectory, CharactersFile);
        return FileAccountStore.Open(Path.Combine(options.ResolvedStateDirectory, AccountsFile), () =>
        {
            Directory.CreateDirectory(options.ResolvedStateDirectory);
            if (!File.Exists(characters)) File.WriteAllText(characters, "[]");
        });
    }

    private static (IAccountAuthenticator, ICharacterStore) LoadAccountsAndCharacters(ServerOptions options, ILogger logger)
    {
        var accountPath = Path.Combine(options.ResolvedStateDirectory, AccountsFile);
        var characterPath = Path.Combine(options.ResolvedStateDirectory, CharactersFile);
        if (!File.Exists(accountPath) || !File.Exists(characterPath))
        {
            if (options.DevelopmentLogins().Count == 0)
            {
                logger.LogWarning(
                    "No {Accounts} and no development logins, so nobody can sign in. Set UBIQUITOUS_DEVELOPMENT_ACCOUNTS=true and UBIQUITOUS_DEVELOPMENT_PASS_HASH",
                    AccountsFile);
            }

            return (new AccountAuthenticator(DevelopmentFixtures.Accounts(options).WithHashedSecrets()),
                DevelopmentFixtures.Characters());
        }

        if (new FileInfo(accountPath).Length == 0)
            logger.LogInformation("{File} is empty; starting with no persisted accounts", AccountsFile);
        if (new FileInfo(characterPath).Length == 0)
            logger.LogInformation("{File} is empty; starting with no persisted characters", CharactersFile);
        var accounts = FileAccountStore.Load(accountPath);
        var characters = FileCharacterStore.Load(characterPath);
        if (options.DevelopmentLogins().Count == 0) return (new AccountAuthenticator(accounts), characters);

        // A local run keeps its development logins next to the real accounts, and their characters in the saves.
        SeedDevelopmentCharacters(characters, options);
        return (new AccountAuthenticator(new DevelopmentOverlayAccountStore(
            DevelopmentFixtures.Accounts(options).WithHashedSecrets(), accounts)), characters);
    }

    private static void SeedDevelopmentCharacters(ICharacterStore characters, ServerOptions options)
    {
        var accountIds = options.DevelopmentLogins().Select(login => login.AccountId).ToHashSet();
        foreach (var character in DevelopmentFixtures.CharacterRecords().Where(one => accountIds.Contains(one.AccountId)))
        {
            if (characters.GetByGuidAsync(character.Guid).GetAwaiter().GetResult() is not null) continue;
            try
            {
                characters.CreateAsync(character).GetAwaiter().GetResult();
            }
            catch (CharacterNameTakenException)
            {
                // A real character already has the name: that one stays, and this fixture is not added.
            }
        }
    }
}
