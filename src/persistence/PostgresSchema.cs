using Npgsql;

namespace Ubiquitous.Persistence;

public static class PostgresSchema
{
    public const string CharacterNameIndex = "characters_name_lower_idx";

    public sealed record Migration(string Name, string Sql);

    public static readonly IReadOnlyList<Migration> Migrations =
    [
        new Migration("0001_initial", """
            CREATE TABLE IF NOT EXISTS accounts (
                id INTEGER PRIMARY KEY,
                user_id TEXT NOT NULL UNIQUE,
                pass_hash TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS characters (
                guid TEXT PRIMARY KEY,
                account_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                gender INTEGER NOT NULL,
                race INTEGER NOT NULL,
                class INTEGER NOT NULL,
                display_info_id INTEGER NOT NULL,
                skin INTEGER NOT NULL,
                hair_style INTEGER NOT NULL,
                hair_colour INTEGER NOT NULL,
                face INTEGER NOT NULL,
                map_id INTEGER NOT NULL,
                x REAL NOT NULL,
                y REAL NOT NULL,
                z REAL NOT NULL,
                level INTEGER NOT NULL,
                equipment INTEGER[] NOT NULL
            );
            CREATE INDEX IF NOT EXISTS characters_account_id_idx ON characters (account_id);

            CREATE TABLE IF NOT EXISTS inventory_items (
                character_guid TEXT NOT NULL,
                id TEXT NOT NULL,
                entry INTEGER NOT NULL,
                count INTEGER NOT NULL,
                area TEXT NOT NULL,
                slot INTEGER NOT NULL,
                PRIMARY KEY (character_guid, id)
            );
            """),
        new Migration("0002_character_xp_currency", """
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS xp INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS currency INTEGER;
            """),
        new Migration("0003_quest_log", """
            CREATE TABLE IF NOT EXISTS quest_log_entries (
                character_guid TEXT NOT NULL,
                quest_id INTEGER NOT NULL,
                slot INTEGER NOT NULL,
                objective_indexes INTEGER[] NOT NULL,
                objective_counts INTEGER[] NOT NULL,
                PRIMARY KEY (character_guid, quest_id)
            );

            CREATE TABLE IF NOT EXISTS quest_log_completed (
                character_guid TEXT NOT NULL,
                quest_id INTEGER NOT NULL,
                PRIMARY KEY (character_guid, quest_id)
            );
            """),
        new Migration("0004_character_name_unique", $"""
            CREATE UNIQUE INDEX IF NOT EXISTS {CharacterNameIndex} ON characters (lower(name));
            """),
        new Migration("0005_character_state", """
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS bag_slots INTEGER;
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS bank_slots INTEGER;
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS health INTEGER;
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS talent_ranks JSONB;
            """),
        new Migration("0006_learned_spells", """
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS learned_spells INTEGER[];
            """),
        new Migration("0007_action_bar", """
            ALTER TABLE characters ADD COLUMN IF NOT EXISTS action_bar JSONB;
            """),
    ];

    public static async Task EnsureCreatedAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var createTrackingTable = connection.CreateCommand())
        {
            createTrackingTable.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    name TEXT PRIMARY KEY,
                    applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );
                """;
            await createTrackingTable.ExecuteNonQueryAsync(cancellationToken);
        }

        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using (var selectApplied = connection.CreateCommand())
        {
            selectApplied.CommandText = "SELECT name FROM schema_migrations";
            await using var reader = await selectApplied.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) applied.Add(reader.GetString(0));
        }

        foreach (var migration in Migrations)
        {
            if (applied.Contains(migration.Name)) continue;

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var apply = connection.CreateCommand())
            {
                apply.Transaction = transaction;
                apply.CommandText = migration.Sql;
                await apply.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO schema_migrations (name) VALUES ($1)";
                record.Parameters.AddWithValue(migration.Name);
                await record.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
    }
}
