using Npgsql;
using Ubiquitous.Persistence;

namespace Ubiquitous.Admin.Stores;

// Opens the same saves the game server uses, from the same variables: Postgres
// when a connection string is set (the Docker stack), the JSON files otherwise.
// The game server owns the database's tables and migrates them; two processes
// migrating at once collide, so this side only reads and writes rows.
public sealed class ServerConnection : IAsyncDisposable
{
    public const string AccountsFile = "accounts.json";
    public const string CharactersFile = "characters.json";

    private readonly NpgsqlDataSource? _postgres;

    public IAccountAdmin Accounts { get; }

    public ICharacterStore Characters { get; }

    public Ubiquitous.Common.IInventoryStore Inventory { get; }

    public bool UsesFiles => _postgres is null;

    private ServerConnection(
        NpgsqlDataSource? postgres, IAccountAdmin accounts, ICharacterStore characters, Ubiquitous.Common.IInventoryStore inventory)
    {
        _postgres = postgres;
        Accounts = accounts;
        Characters = characters;
        Inventory = inventory;
    }

    public static async Task<ServerConnection> OpenAsync(
        string? connectionString, string? passwordFile, string stateDirectory, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrWhiteSpace(passwordFile)) builder.Password = (await File.ReadAllTextAsync(passwordFile, cancellationToken)).Trim();
            var postgres = NpgsqlDataSource.Create(builder.ConnectionString);
            return new ServerConnection(
                postgres, new PostgresAccountStore(postgres), new PostgresCharacterStore(postgres), new PostgresInventoryStore(postgres));
        }

        var characters = Path.Combine(stateDirectory, CharactersFile);
        var accounts = FileAccountStore.Open(Path.Combine(stateDirectory, AccountsFile), () =>
        {
            Directory.CreateDirectory(stateDirectory);
            if (!File.Exists(characters)) File.WriteAllText(characters, "[]");
        });
        ICharacterStore characterStore = File.Exists(characters) ? FileCharacterStore.Load(characters) : new InMemoryCharacterStore([]);
        return new ServerConnection(null, accounts, characterStore, new FileInventoryStore(Path.Combine(stateDirectory, "inventory")));
    }

    public async ValueTask DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
    }
}
