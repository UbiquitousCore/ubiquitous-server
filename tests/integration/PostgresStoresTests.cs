using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Npgsql;
using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PostgresStoresTests : IAsyncLifetime
{
    private static readonly string? ConnectionString =
        Environment.GetEnvironmentVariable("UBIQUITOUS_TEST_POSTGRES_CONNECTION_STRING");

    private NpgsqlDataSource? _dataSource;

    public async Task InitializeAsync()
    {
        if (ConnectionString is null) return;
        _dataSource = NpgsqlDataSource.Create(ConnectionString);
        await PostgresSchema.EnsureCreatedAsync(_dataSource);
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "TRUNCATE accounts, characters, inventory_items";
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null) await _dataSource.DisposeAsync();
    }

    [Fact]
    public async Task PostgresAccountStore_FindsAnInsertedAccountAndAuthenticatesIt()
    {
        if (_dataSource is null) return;
        await using (var connection = await _dataSource.OpenConnectionAsync())
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO accounts (id, user_id, pass_hash) VALUES (1, 'developer', 'dev-secret')";
            await insert.ExecuteNonQueryAsync();
        }

        var accountStore = new PostgresAccountStore(_dataSource);
        var store = new AccountAuthenticator(accountStore, iterations: TestSessions.TestPasswordIterations);

        var found = await store.FindByUserIdAsync("developer");
        Assert.NotNull(found);
        Assert.Equal(1, found!.Id);

        var ok = await store.AuthenticateAsync("developer", "dev-secret");
        Assert.True(ok.Ok);
        var bad = await store.AuthenticateAsync("developer", "wrong");
        Assert.False(bad.Ok);

        var missing = await store.FindByUserIdAsync("nobody");
        Assert.Null(missing);
    }

    [Fact]
    public async Task PostgresCharacterStore_UpdatingACharacterThatDoesNotExist_Throws()
    {
        if (_dataSource is null) return;
        var store = new PostgresCharacterStore(_dataSource);
        var missing = new CharacterRecord("nobody", 1, "Nobody", 1, 2, 3, 4, 5, 6, 7, 8, 13, 0, 0, 0, 1, []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(missing));
    }

    [Fact]
    public async Task PostgresCharacterStore_RoundTripsACharacterRow()
    {
        if (_dataSource is null) return;
        var character = new CharacterRecord(
            "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 100.5f, 2.5f, -3.25f, 10, [100, 200, 300]);
        await using (var connection = await _dataSource.OpenConnectionAsync())
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO characters
                    (guid, account_id, name, gender, race, class, display_info_id, skin, hair_style, hair_colour,
                     face, map_id, x, y, z, level, equipment)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17)
                """;
            insert.Parameters.AddWithValue(character.Guid);
            insert.Parameters.AddWithValue(character.AccountId);
            insert.Parameters.AddWithValue(character.Name);
            insert.Parameters.AddWithValue(character.Gender);
            insert.Parameters.AddWithValue(character.Race);
            insert.Parameters.AddWithValue(character.Class);
            insert.Parameters.AddWithValue(character.DisplayInfoId);
            insert.Parameters.AddWithValue(character.Skin);
            insert.Parameters.AddWithValue(character.HairStyle);
            insert.Parameters.AddWithValue(character.HairColour);
            insert.Parameters.AddWithValue(character.Face);
            insert.Parameters.AddWithValue((int)character.MapId);
            insert.Parameters.AddWithValue(character.X);
            insert.Parameters.AddWithValue(character.Y);
            insert.Parameters.AddWithValue(character.Z);
            insert.Parameters.AddWithValue(character.Level);
            insert.Parameters.AddWithValue(character.Equipment.ToArray());
            await insert.ExecuteNonQueryAsync();
        }

        var store = new PostgresCharacterStore(_dataSource);

        void AssertSameCharacter(CharacterRecord? actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(character with { Equipment = [] }, actual! with { Equipment = [] });
            Assert.Equal(character.Equipment, actual.Equipment);
        }

        AssertSameCharacter(await store.GetByGuidAsync("char-a"));

        var forAccount = await store.ListForAccountAsync(1);
        AssertSameCharacter(Assert.Single(forAccount));

        Assert.Null(await store.GetByGuidAsync("no-such-character"));
    }

    [Fact]
    public async Task PostgresCharacterStore_RoundTripsHealthStorageSizesTalentRanksAndLearnedSpells()
    {
        if (_dataSource is null) return;
        var store = new PostgresCharacterStore(_dataSource);
        var created = new CharacterRecord("char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 0, 0, 0, 1, []);
        await store.CreateAsync(created);
        var saved = created with
        {
            Health = 77,
            BagSlots = 24,
            BankSlots = 30,
            TalentRanks = new Dictionary<uint, int> { [5] = 2, [9] = 1 },
            LearnedSpells = [6978, 7047],
        };

        await store.UpdateAsync(saved);

        var loaded = await store.GetByGuidAsync("char-a");
        Assert.NotNull(loaded);
        Assert.Equal(77, loaded!.Health);
        Assert.Equal(24, loaded.BagSlots);
        Assert.Equal(30, loaded.BankSlots);
        Assert.Equal(saved.TalentRanks, loaded.TalentRanks);
        Assert.Equal(saved.LearnedSpells, loaded.LearnedSpells);
    }

    [Fact]
    public async Task PostgresCharacterStore_FindByName_TreatsTheNameAsTextNotAPattern()
    {
        if (_dataSource is null) return;
        var store = new PostgresCharacterStore(_dataSource);
        await store.CreateAsync(new CharacterRecord("char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 0, 0, 0, 1, []));

        Assert.NotNull(await store.FindByNameAsync("aYLA"));
        Assert.Null(await store.FindByNameAsync("%"));
        Assert.Null(await store.FindByNameAsync("Ayl_"));
    }

    [Fact]
    public async Task PostgresCharacterStore_TwoCharactersWithOneNameIgnoringCase_SecondIsRefusedAsTaken()
    {
        if (_dataSource is null) return;
        var store = new PostgresCharacterStore(_dataSource);
        await store.CreateAsync(new CharacterRecord("char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 0, 0, 0, 1, []));

        await Assert.ThrowsAsync<CharacterNameTakenException>(() => store.CreateAsync(
            new CharacterRecord("char-b", 2, "AYLA", 1, 2, 3, 4, 5, 6, 7, 8, 13, 0, 0, 0, 1, [])));
    }

    [Fact]
    public async Task PostgresProgressStore_AFailedCharacterUpdate_LeavesNoInventoryBehind()
    {
        if (_dataSource is null) return;
        var progress = new PostgresProgressStore(_dataSource);
        var missing = new CharacterRecord("ghost", 1, "Ghost", 1, 2, 3, 4, 5, 6, 7, 8, 13, 0, 0, 0, 1, []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => progress.SaveAsync(
            missing, [new PersistedInventoryItem("item-1", 100, 2, "bag", 0)], PersistedQuestLog.Empty));

        Assert.Empty(await new PostgresInventoryStore(_dataSource).ListAsync("ghost"));
    }

    [Fact]
    public async Task PostgresInventoryStore_SaveReplacesRatherThanMergesTheStoredSet()
    {
        if (_dataSource is null) return;
        var store = new PostgresInventoryStore(_dataSource);

        await store.SaveAsync("char-a", [new PersistedInventoryItem("item-1", 100, 2, "bag", 0)]);
        var first = await store.ListAsync("char-a");
        Assert.Equal("item-1", Assert.Single(first).Id);

        await store.SaveAsync("char-a", [new PersistedInventoryItem("item-2", 200, 1, "bag", 3)]);
        var second = await store.ListAsync("char-a");
        var only = Assert.Single(second);
        Assert.Equal("item-2", only.Id);
        Assert.Equal(3, only.Slot);

        Assert.Empty(await store.ListAsync("no-such-character"));
    }

    [Fact]
    public async Task FullGateway_PersistsAPurchaseAcrossTwoIndependentGatewaysBackedByPostgres()
    {
        if (_dataSource is null) return;
        await using (var connection = await _dataSource.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO accounts (id, user_id, pass_hash) VALUES (1, 'developer', 'dev-secret')";
            await command.ExecuteNonQueryAsync();
        }

        await using (var connection = await _dataSource.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO characters
                    (guid, account_id, name, gender, race, class, display_info_id, skin, hair_style, hair_colour,
                     face, map_id, x, y, z, level, equipment)
                VALUES ('char-a', 1, 'Ayla', 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, ARRAY[100, 200])
                """;
            await command.ExecuteNonQueryAsync();
        }

        var spawns = new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) };
        var port = FreeLoopbackPort();
        var options = ServerOptions.Defaults with { BindAddress = "127.0.0.1", WebSocketPort = port };

        await using (var gatewayA = new WebSocketGateway(options, Sessions(
            new PostgresAccountStore(_dataSource), new PostgresCharacterStore(_dataSource),
            spawns, new PostgresInventoryStore(_dataSource))))
        {
            gatewayA.Start();
            using var clientA = new ClientWebSocket();
            using var ctsA = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await clientA.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), ctsA.Token);

            await SendAsync(clientA, WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))), ctsA.Token);
            await ReceiveOneMessageAsync(clientA, ctsA.Token);
            await SendAsync(clientA, WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))), ctsA.Token);
            await ReceiveUntilAsync(clientA, "world.entered", ctsA.Token);

            await SendAndReceiveUntilAsync(clientA, WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.VendorBuy, 2, new { networkId = "Creature:1", slot = 0, entry = 100, count = 2 })),
                "inventory.itemAdded", ctsA.Token);

            await clientA.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ctsA.Token);
            await Task.Delay(500);
        }

        var freshPort = FreeLoopbackPort();
        var freshOptions = ServerOptions.Defaults with { BindAddress = "127.0.0.1", WebSocketPort = freshPort };
        await using var gatewayB = new WebSocketGateway(freshOptions, Sessions(
            new PostgresAccountStore(_dataSource), new PostgresCharacterStore(_dataSource),
            spawns, new PostgresInventoryStore(_dataSource)));
        gatewayB.Start();
        using var clientB = new ClientWebSocket();
        using var ctsB = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await clientB.ConnectAsync(new Uri($"ws://127.0.0.1:{freshPort}/"), ctsB.Token);

        await SendAsync(clientB, WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))), ctsB.Token);
        await ReceiveOneMessageAsync(clientB, ctsB.Token);
        var entered = await SendAndReceiveUntilAsync(clientB, WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))), "inventory.updated", ctsB.Token);

        Assert.Contains("\"entry\":100", entered);
        Assert.Contains("\"count\":2", entered);
    }

    private static int FreeLoopbackPort()
    {
        using var probe = new TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static Task SendAsync(ClientWebSocket client, string json, CancellationToken cancellationToken) =>
        client.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancellationToken);

    private static async Task<string> ReceiveOneMessageAsync(ClientWebSocket client, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var result = await client.ReceiveAsync(buffer, cancellationToken);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static async Task<string> ReceiveUntilAsync(ClientWebSocket client, string marker, CancellationToken cancellationToken)
    {
        for (int i = 0; i < 10; i++)
        {
            var message = await ReceiveOneMessageAsync(client, cancellationToken);
            if (message.Contains(marker, StringComparison.Ordinal)) return message;
        }

        throw new TimeoutException($"No message containing \"{marker}\" arrived within 10 frames.");
    }

    private static async Task<string> SendAndReceiveUntilAsync(
        ClientWebSocket client, string json, string marker, CancellationToken cancellationToken)
    {
        await SendAsync(client, json, cancellationToken);
        return await ReceiveUntilAsync(client, marker, cancellationToken);
    }

    private static PlaySessionFactory Sessions(
        IAccountStore accounts,
        ICharacterStore characters,
        IReadOnlyList<CreatureSpawnDefinition>? creatureSpawns = null,
        Ubiquitous.Common.IInventoryStore? inventory = null) =>
        new(new SessionStores(new AccountAuthenticator(accounts, iterations: TestSessions.TestPasswordIterations), characters, Inventory: inventory),
            ServerContent.Empty with { CreatureSpawns = creatureSpawns ?? [] },
            new SharedWorld());
}

public sealed class PostgresMigrationsTests
{
    [Fact]
    public void Migrations_NamesAreAllUnique()
    {
        var names = PostgresSchema.Migrations.Select(m => m.Name).ToList();

        Assert.Equal(names.Distinct(StringComparer.Ordinal).Count(), names.Count);
    }

    [Fact]
    public void Migrations_IsNeverEmpty()
    {
        Assert.NotEmpty(PostgresSchema.Migrations);
    }

}
