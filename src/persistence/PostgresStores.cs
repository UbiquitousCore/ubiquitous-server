using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Ubiquitous.Persistence;

public sealed class PostgresAccountStore : IAccountStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAccountStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public async Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, user_id, pass_hash FROM accounts WHERE user_id = $1";
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new AccountRecord(reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
    }

    public async Task SetPassHashAsync(int accountId, string passHash, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE accounts SET pass_hash = $2 WHERE id = $1";
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(passHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class PostgresCharacterStore : ICharacterStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresCharacterStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    private const string SelectColumns =
        "guid, account_id, name, gender, race, class, display_info_id, skin, hair_style, hair_colour, face, "
        + "map_id, x, y, z, level, equipment, xp, currency, bag_slots, bank_slots, health, talent_ranks, learned_spells";

    public async Task<CharacterRecord?> GetByGuidAsync(string guid, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM characters WHERE guid = $1";
        command.Parameters.AddWithValue(guid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCharacter(reader) : null;
    }

    public async Task<IReadOnlyList<CharacterRecord>> ListForAccountAsync(int accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM characters WHERE account_id = $1 ORDER BY guid";
        command.Parameters.AddWithValue(accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var characters = new List<CharacterRecord>();
        while (await reader.ReadAsync(cancellationToken)) characters.Add(ReadCharacter(reader));
        return characters;
    }

    public async Task<CharacterRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM characters WHERE lower(name) = lower($1) LIMIT 1";
        command.Parameters.AddWithValue(name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCharacter(reader) : null;
    }

    private static CharacterRecord ReadCharacter(NpgsqlDataReader reader) => new(
        Guid: reader.GetString(0),
        AccountId: reader.GetInt32(1),
        Name: reader.GetString(2),
        Gender: reader.GetInt32(3),
        Race: reader.GetInt32(4),
        Class: reader.GetInt32(5),
        DisplayInfoId: reader.GetInt32(6),
        Skin: reader.GetInt32(7),
        HairStyle: reader.GetInt32(8),
        HairColour: reader.GetInt32(9),
        Face: reader.GetInt32(10),
        MapId: (ushort)reader.GetInt32(11),
        X: reader.GetFloat(12),
        Y: reader.GetFloat(13),
        Z: reader.GetFloat(14),
        Level: reader.GetInt32(15),
        Equipment: reader.GetFieldValue<int[]>(16),
        Xp: reader.GetInt32(17),
        Currency: reader.IsDBNull(18) ? null : reader.GetInt32(18),
        BagSlots: reader.IsDBNull(19) ? null : reader.GetInt32(19),
        BankSlots: reader.IsDBNull(20) ? null : reader.GetInt32(20),
        Health: reader.IsDBNull(21) ? null : reader.GetInt32(21),
        TalentRanks: reader.IsDBNull(22)
            ? null
            : JsonSerializer.Deserialize<Dictionary<uint, int>>(reader.GetString(22)),
        LearnedSpells: reader.IsDBNull(23)
            ? null
            : [.. reader.GetFieldValue<int[]>(23).Select(id => (uint)id)]);

    private static void AddLearnedSpells(NpgsqlCommand command, CharacterRecord character) =>
        command.Parameters.AddWithValue(character.LearnedSpells is null
            ? DBNull.Value
            : character.LearnedSpells.Select(id => (int)id).ToArray());

    private static void AddTalentRanks(NpgsqlCommand command, CharacterRecord character)
    {
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = character.TalentRanks is null
                ? DBNull.Value
                : JsonSerializer.Serialize(character.TalentRanks),
        });
    }

    public async Task UpdateAsync(CharacterRecord character, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await UpdateAsync(connection, null, character, cancellationToken);
    }

    internal static async Task UpdateAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, CharacterRecord character,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE characters SET map_id = $2, x = $3, y = $4, z = $5, level = $6, xp = $7, currency = $8,
                bag_slots = $9, bank_slots = $10, health = $11, talent_ranks = $12, learned_spells = $13
            WHERE guid = $1
            """;
        command.Parameters.AddWithValue(character.Guid);
        command.Parameters.AddWithValue((int)character.MapId);
        command.Parameters.AddWithValue(character.X);
        command.Parameters.AddWithValue(character.Y);
        command.Parameters.AddWithValue(character.Z);
        command.Parameters.AddWithValue(character.Level);
        command.Parameters.AddWithValue(character.Xp);
        command.Parameters.AddWithValue((object?)character.Currency ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)character.BagSlots ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)character.BankSlots ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)character.Health ?? DBNull.Value);
        AddTalentRanks(command, character);
        AddLearnedSpells(command, character);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException($"No character with GUID '{character.Guid}' exists to update.");
    }

    public async Task CreateAsync(CharacterRecord character, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO characters (
                guid, account_id, name, gender, race, class, display_info_id,
                skin, hair_style, hair_colour, face, map_id, x, y, z, level, equipment, xp, currency,
                bag_slots, bank_slots, health, talent_ranks, learned_spells)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19,
                $20, $21, $22, $23, $24)
            """;
        command.Parameters.AddWithValue(character.Guid);
        command.Parameters.AddWithValue(character.AccountId);
        command.Parameters.AddWithValue(character.Name);
        command.Parameters.AddWithValue(character.Gender);
        command.Parameters.AddWithValue(character.Race);
        command.Parameters.AddWithValue(character.Class);
        command.Parameters.AddWithValue(character.DisplayInfoId);
        command.Parameters.AddWithValue(character.Skin);
        command.Parameters.AddWithValue(character.HairStyle);
        command.Parameters.AddWithValue(character.HairColour);
        command.Parameters.AddWithValue(character.Face);
        command.Parameters.AddWithValue((int)character.MapId);
        command.Parameters.AddWithValue(character.X);
        command.Parameters.AddWithValue(character.Y);
        command.Parameters.AddWithValue(character.Z);
        command.Parameters.AddWithValue(character.Level);
        command.Parameters.AddWithValue(character.Equipment.ToArray());
        command.Parameters.AddWithValue(character.Xp);
        command.Parameters.AddWithValue((object?)character.Currency ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)character.BagSlots ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)character.BankSlots ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)character.Health ?? DBNull.Value);
        AddTalentRanks(command, character);
        AddLearnedSpells(command, character);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation
            && ex.ConstraintName == PostgresSchema.CharacterNameIndex)
        {
            throw new CharacterNameTakenException(character.Name);
        }
    }

    public async Task<bool> DeleteAsync(string guid, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM characters WHERE guid = $1";
        command.Parameters.AddWithValue(guid);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }
}

public sealed class PostgresInventoryStore : Ubiquitous.Common.IInventoryStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresInventoryStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public async Task<IReadOnlyList<Ubiquitous.Common.PersistedInventoryItem>> ListAsync(
        string characterGuid, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, entry, count, area, slot FROM inventory_items WHERE character_guid = $1 ORDER BY slot";
        command.Parameters.AddWithValue(characterGuid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<Ubiquitous.Common.PersistedInventoryItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new Ubiquitous.Common.PersistedInventoryItem(
                reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3), reader.GetInt32(4)));
        }

        return items;
    }

    public async Task SaveAsync(
        string characterGuid, IReadOnlyList<Ubiquitous.Common.PersistedInventoryItem> items, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ReplaceAsync(connection, transaction, characterGuid, items, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task ReplaceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string characterGuid,
        IReadOnlyList<Ubiquitous.Common.PersistedInventoryItem> items, CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM inventory_items WHERE character_guid = $1";
            delete.Parameters.AddWithValue(characterGuid);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var item in items)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO inventory_items (character_guid, id, entry, count, area, slot) VALUES ($1, $2, $3, $4, $5, $6)";
            insert.Parameters.AddWithValue(characterGuid);
            insert.Parameters.AddWithValue(item.Id);
            insert.Parameters.AddWithValue(item.Entry);
            insert.Parameters.AddWithValue(item.Count);
            insert.Parameters.AddWithValue(item.Area);
            insert.Parameters.AddWithValue(item.Slot);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

public sealed class PostgresQuestLogStore : Ubiquitous.Common.IQuestLogStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresQuestLogStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public async Task<Ubiquitous.Common.PersistedQuestLog> GetAsync(
        string characterGuid, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var accepted = new List<Ubiquitous.Common.PersistedQuestLogEntry>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT quest_id, slot, objective_indexes, objective_counts FROM quest_log_entries WHERE character_guid = $1";
            command.Parameters.AddWithValue(characterGuid);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var indexes = reader.GetFieldValue<int[]>(2);
                var counts = reader.GetFieldValue<int[]>(3);
                if (indexes.Length != counts.Length)
                    throw new InvalidDataException(
                        $"Quest {reader.GetInt32(0)} for character '{characterGuid}' has {indexes.Length} objective indexes and {counts.Length} counts.");
                var objectives = indexes.Zip(counts, (index, count) =>
                    new Ubiquitous.Common.PersistedQuestObjective(index, count)).ToArray();
                accepted.Add(new Ubiquitous.Common.PersistedQuestLogEntry(
                    (uint)reader.GetInt32(0), reader.GetInt32(1), objectives));
            }
        }

        var completed = new List<uint>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT quest_id FROM quest_log_completed WHERE character_guid = $1";
            command.Parameters.AddWithValue(characterGuid);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) completed.Add((uint)reader.GetInt32(0));
        }

        return new Ubiquitous.Common.PersistedQuestLog(accepted, completed);
    }

    public async Task SaveAsync(
        string characterGuid, Ubiquitous.Common.PersistedQuestLog log, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ReplaceAsync(connection, transaction, characterGuid, log, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task ReplaceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string characterGuid,
        Ubiquitous.Common.PersistedQuestLog log, CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM quest_log_entries WHERE character_guid = $1";
            delete.Parameters.AddWithValue(characterGuid);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM quest_log_completed WHERE character_guid = $1";
            delete.Parameters.AddWithValue(characterGuid);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var entry in log.Accepted)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO quest_log_entries (character_guid, quest_id, slot, objective_indexes, objective_counts)
                VALUES ($1, $2, $3, $4, $5)
                """;
            insert.Parameters.AddWithValue(characterGuid);
            insert.Parameters.AddWithValue((int)entry.QuestId);
            insert.Parameters.AddWithValue(entry.Slot);
            insert.Parameters.AddWithValue(entry.Objectives.Select(o => o.Index).ToArray());
            insert.Parameters.AddWithValue(entry.Objectives.Select(o => o.Count).ToArray());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var questId in log.Completed)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO quest_log_completed (character_guid, quest_id) VALUES ($1, $2)";
            insert.Parameters.AddWithValue(characterGuid);
            insert.Parameters.AddWithValue((int)questId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

public sealed class PostgresProgressStore : IProgressStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresProgressStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public async Task SaveAsync(
        CharacterRecord character,
        IReadOnlyList<Ubiquitous.Common.PersistedInventoryItem> inventory,
        Ubiquitous.Common.PersistedQuestLog questLog,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostgresInventoryStore.ReplaceAsync(connection, transaction, character.Guid, inventory, cancellationToken);
        await PostgresQuestLogStore.ReplaceAsync(connection, transaction, character.Guid, questLog, cancellationToken);
        await PostgresCharacterStore.UpdateAsync(connection, transaction, character, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
