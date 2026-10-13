using System.Text.Json;
using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PersistenceStoreTests
{
    [Fact]
    public async Task FileCharacterStore_UpdateAsyncPersistsAcrossAFreshLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-character-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "characters.json");
        Directory.CreateDirectory(directory);
        try
        {
            var original = Character("char-a", 1, "Ayla");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
                new[] { original }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            var store = FileCharacterStore.Load(path);
            var moved = original with { MapId = 20, X = 111f, Y = 222f, Z = 333f, Level = 5, Xp = 42, Currency = 250 };
            await store.UpdateAsync(moved);

            var reloaded = FileCharacterStore.Load(path);
            var found = await reloaded.GetByGuidAsync("char-a");
            Assert.NotNull(found);
            Assert.Equal((ushort)20, found!.MapId);
            Assert.Equal(111f, found.X);
            Assert.Equal(222f, found.Y);
            Assert.Equal(333f, found.Z);
            Assert.Equal(5, found.Level);
            Assert.Equal(42, found.Xp);
            Assert.Equal(250, found.Currency);

            Assert.Equal(original.Name, found.Name);
            Assert.Equal(original.AccountId, found.AccountId);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FileInventoryStore_ReplacingASaveLeavesOnlyTheNewFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-inventory-{Guid.NewGuid():N}");
        try
        {
            var store = new FileInventoryStore(directory);
            await store.SaveAsync("char-a", [new PersistedInventoryItem("old", 100, 2, "bag", 3)]);
            await store.SaveAsync("char-a", [new PersistedInventoryItem("new", 101, 1, "bag", 4)]);

            Assert.Equal("new", Assert.Single(await store.ListAsync("char-a")).Id);
            Assert.Equal(["inventory-char-a.json"], Directory.GetFiles(directory).Select(Path.GetFileName));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FileInventoryStore_RoundTripsSyntheticItems()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-inventory-{Guid.NewGuid():N}");
        try
        {
            var store = new FileInventoryStore(directory);
            await store.SaveAsync("char-a", [new PersistedInventoryItem("item", 100, 2, "bag", 3)]);
            var items = await store.ListAsync("char-a");
            Assert.Equal("item", Assert.Single(items).Id);
            Assert.Equal(3, items[0].Slot);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task PlaySession_InventoryPersistsAcrossSessionsForTheSameCharacter()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-inventory-session-{Guid.NewGuid():N}");
        try
        {
            var store = new FileInventoryStore(directory);
            var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
            var spawns = new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) };

            var sessionA = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, spawns, inventoryStore: store);
            await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
            await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
            var bought = await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.VendorBuy, 2, new { networkId = "Creature:1", slot = 0, entry = 100, count = 2 })));
            Assert.Contains("inventory.itemAdded", bought[1]);
            await sessionA.SaveInventoryAsync();

            var sessionB = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, spawns, inventoryStore: store);
            await sessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
            var entered = await sessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

            var snapshot = Assert.Single(entered, message => message.Contains("inventory.updated"));
            Assert.Contains("\"entry\":100", snapshot);
            Assert.Contains("\"count\":2", snapshot);
            Assert.DoesNotContain("starter-item", snapshot);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task PlaySession_CharacterPositionAndCurrencyPersistAcrossSessionsForTheSameCharacter()
    {
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var spawns = new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) };

        var sessionA = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters, spawns);
        await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        var moved = await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 2, new PlayerMovePayload(2, 2, 3, 0.5, true, MovementState.Walking))));
        Assert.Contains("entity.moved", moved.Single());
        var bought = await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorBuy, 3, new { networkId = "Creature:1", slot = 0, entry = 100, count = 2 })));
        Assert.Contains("inventory.itemAdded", bought[1]);
        await sessionA.SaveCharacterAsync();

        var savedDirectly = await characters.GetByGuidAsync("char-a");
        Assert.Equal(TestSessions.TestPurse - 100, savedDirectly!.Currency);
        Assert.Equal(2f, savedDirectly.X);
        Assert.Equal(2f, savedDirectly.Y);
        Assert.Equal(3f, savedDirectly.Z);

        var sessionB = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters, spawns);
        await sessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        var entered = await sessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var worldEntered = Assert.Single(entered, message => message.Contains("world.entered"));
        Assert.Contains("\"x\":2", worldEntered);
        Assert.Contains("\"y\":2", worldEntered);
        Assert.Contains("\"z\":3", worldEntered);
    }

    [Fact]
    public async Task FileQuestLogStore_RoundTripsAcceptedAndCompletedQuests()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-quests-{Guid.NewGuid():N}");
        try
        {
            var store = new FileQuestLogStore(directory);
            var log = new PersistedQuestLog(
                [new PersistedQuestLogEntry(5001, Slot: 0, [new PersistedQuestObjective(0, 2)])],
                [5002]);
            await store.SaveAsync("char-a", log);

            var reloaded = await store.GetAsync("char-a");

            var entry = Assert.Single(reloaded.Accepted);
            Assert.Equal(5001u, entry.QuestId);
            Assert.Equal(2, Assert.Single(entry.Objectives).Count);
            Assert.Contains(5002u, reloaded.Completed);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FileQuestLogStore_ReadsAnEmptyLogForACharacterNeverSaved()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-quests-{Guid.NewGuid():N}");
        var store = new FileQuestLogStore(directory);

        var log = await store.GetAsync("char-never-saved");

        Assert.Empty(log.Accepted);
        Assert.Empty(log.Completed);
    }

    [Fact]
    public async Task PlaySession_QuestLogPersistsAcrossSessionsForTheSameCharacter()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-quests-session-{Guid.NewGuid():N}");
        try
        {
            var store = new FileQuestLogStore(directory);
            var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
            var spawns = new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) };

            var sessionA = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, spawns, questLogStore: store);
            await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
            await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
            var accepted = await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5001u })));
            Assert.Contains(accepted, message => message.Contains("quest.updated"));
            await sessionA.SaveQuestLogAsync();

            var sessionB = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, spawns, questLogStore: store);
            await sessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
            var entered = await sessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

            var questLog = Assert.Single(entered, message => message.Contains("\"type\":\"quest.log\""));
            Assert.Contains("\"id\":5001", questLog);

            var reoffer = await sessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5001u })));
            Assert.Contains("\"type\":\"protocol.error\"", Assert.Single(reoffer));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    public async Task FileCharacterStore_LoadTreatsAnEmptyFileAsAnEmptyStore(string contents)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-character-empty-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "characters.json");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(path, contents);

            var store = FileCharacterStore.Load(path);

            Assert.Null(await store.GetByGuidAsync("char-a"));
            Assert.Empty(await store.ListForAccountAsync(1));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    public async Task FileAccountStore_LoadTreatsAnEmptyFileAsAnEmptyStore(string contents)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-account-empty-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "accounts.json");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(path, contents);

            var store = FileAccountStore.Load(path);

            Assert.Null(await store.FindByUserIdAsync("developer"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FileCharacterStore_UpdateAsyncLeavesNoTempSidecarBehind()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-character-tmp-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "characters.json");
        Directory.CreateDirectory(directory);
        try
        {
            var original = Character("char-a", 1, "Ayla");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
                new[] { original }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            var store = FileCharacterStore.Load(path);
            await store.UpdateAsync(original with { Level = 9 });

            Assert.False(File.Exists(path + ".tmp"));
            var reloaded = FileCharacterStore.Load(path);
            Assert.Equal(9, (await reloaded.GetByGuidAsync("char-a"))!.Level);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static CharacterRecord Character(string guid, int accountId, string name) => new(
        guid, accountId, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, [100, 200]);
}
