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

    public static IAccountAdmin OpenAccountAdmin(ServerOptions options, NpgsqlDataSource? postgres)
    {
        if (postgres is not null) return new PostgresAccountStore(postgres);
        return FileAccountStore.Open(Path.Combine(options.ResolvedStateDirectory, AccountsFile), () => EnsureSaveFiles(options));
    }

    // A first run starts with no accounts rather than with stand-ins, so nothing it saves is ever thrown away.
    private static (IAccountAuthenticator, ICharacterStore) LoadAccountsAndCharacters(ServerOptions options, ILogger logger)
    {
        EnsureSaveFiles(options);
        var accounts = FileAccountStore.Load(Path.Combine(options.ResolvedStateDirectory, AccountsFile));
        if (!accounts.ListAsync().GetAwaiter().GetResult().Any())
            logger.LogWarning("No accounts yet, so nobody can sign in. Make one with: account create <name>, then account gm <name> on");
        return (new AccountAuthenticator(accounts),
            FileCharacterStore.Load(Path.Combine(options.ResolvedStateDirectory, CharactersFile)));
    }

    private static void EnsureSaveFiles(ServerOptions options)
    {
        Directory.CreateDirectory(options.ResolvedStateDirectory);
        foreach (var name in new[] { AccountsFile, CharactersFile })
        {
            var path = Path.Combine(options.ResolvedStateDirectory, name);
            if (!File.Exists(path)) File.WriteAllText(path, "[]");
        }
    }
}
