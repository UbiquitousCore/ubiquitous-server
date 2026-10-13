using System.Text.Json;
using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class InventoryEquipFlowTests
{
    private const int BowEntry = 4149;
    private const int BucklerEntry = 1213;
    private const int ChestEntry = 1053;
    private const int PotionEntry = 3008;
    private const int StarterEntry = 100;

    private const string ItemId = "bag-item";
    private const int BagSlot = 0;

    private sealed class FixedInventoryStore(IReadOnlyList<PersistedInventoryItem> items) : IInventoryStore
    {
        public Task<IReadOnlyList<PersistedInventoryItem>> ListAsync(
            string characterGuid, CancellationToken cancellationToken = default) => Task.FromResult(items);

        public Task SaveAsync(string characterGuid, IReadOnlyList<PersistedInventoryItem> items,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task<PlaySession> SessionAsync(
        int entry,
        IReadOnlyList<int>? equipment = null,
        int playerClass = PlayerClassIds.Ranger,
        int level = 6,
        IReadOnlyList<PersistedInventoryItem>? bag = null,
        CreatureSpawnDefinition? spawn = null)
    {
        var character = new CharacterRecord(
            "char-a", 1, "Ayla", 1, PlayerRaceIds.Human, playerClass, 4, 5, 6, 7, 8, 13, 1, 2, 3,
            level, equipment ?? []);
        var store = new FixedInventoryStore(
            bag ?? [new PersistedInventoryItem(ItemId, entry, 1, "bag", BagSlot)]);
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([character]),
            spawn is null ? null : [spawn],
            inventoryStore: store);
        await Send(session, WebMessageTypes.AccountLogin, new LoginRequest("developer", "dev-secret"));
        await Send(session, WebMessageTypes.CharactersChoose, new ChooseCharacterRequest("Ayla"));
        return session;
    }

    private static Task<IReadOnlyList<string>> Send(PlaySession session, string type, object payload) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 9, payload)));

    private static Task<IReadOnlyList<string>> EquipAsync(PlaySession session, int? to = null) =>
        Send(session, WebMessageTypes.InventoryEquip, to is null
            ? new { from = new { area = "bag", index = BagSlot }, itemId = ItemId, equip = true }
            : new
            {
                from = new { area = "bag", index = BagSlot },
                itemId = ItemId,
                equip = true,
                to = new { area = "equipment", index = to.Value },
            });

    private static Task<IReadOnlyList<string>> UnequipAsync(PlaySession session, int wornSlot) =>
        Send(session, WebMessageTypes.InventoryEquip, new
        {
            from = new { area = "equipment", index = wornSlot },
            itemId = ItemId,
            equip = false,
        });

    private static JsonElement PayloadOf(IReadOnlyList<string> responses, string type)
    {
        var message = responses.First(candidate => candidate.Contains($"\"{type}\""));
        return JsonDocument.Parse(message).RootElement.GetProperty("payload").Clone();
    }

    private static int[] AppearanceIn(IReadOnlyList<string> responses) =>
        [.. PayloadOf(responses, WebMessageTypes.EntityAppearanceChanged).GetProperty("equipment")
            .EnumerateArray().Select(entry => entry.GetInt32())];

    private static async Task<JsonElement> SnapshotAsync(PlaySession session)
    {
        var responses = await Send(session, WebMessageTypes.InventorySort, new { area = "bag" });
        return PayloadOf(responses, WebMessageTypes.InventoryUpdated);
    }

    private static JsonElement ItemIn(JsonElement snapshot, string itemId) =>
        snapshot.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == itemId);

    [Fact]
    public async Task EquippingABowFromTheBagWearsItInTheRangedSlot()
    {
        var session = await SessionAsync(BowEntry);

        var responses = await EquipAsync(session);

        Assert.Contains(responses, message => message.Contains("\"inventory.itemsSwapped\""));
        var worn = ItemIn(await SnapshotAsync(session), ItemId).GetProperty("slot");
        Assert.Equal("equipment", worn.GetProperty("area").GetString());
        Assert.Equal(EquipmentSlots.Ranged, worn.GetProperty("index").GetInt32());
    }

    [Fact]
    public async Task TheAppearanceBroadcastIsPositionalAndPutsTheBowInSlotFifteen()
    {
        var session = await SessionAsync(BowEntry);

        var equipment = AppearanceIn(await EquipAsync(session));

        Assert.Equal(EquipmentSlots.Count, equipment.Length);
        Assert.Equal(BowEntry, equipment[EquipmentSlots.Ranged]);
        Assert.Equal(0, equipment[EquipmentSlots.Head]);
        Assert.Equal(1, equipment.Count(entry => entry != 0));
    }

    [Fact]
    public async Task TheSwapNamesTheSlotEachSideCameFrom()
    {
        var session = await SessionAsync(BowEntry);

        var payload = PayloadOf(await EquipAsync(session), WebMessageTypes.InventoryItemsSwapped);

        Assert.Equal(ItemId, payload.GetProperty("first").GetProperty("itemId").GetString());
        Assert.Equal(BagSlot, payload.GetProperty("first").GetProperty("from").GetProperty("index").GetInt32());
        Assert.False(payload.GetProperty("second").TryGetProperty("itemId", out _));
        Assert.Equal(EquipmentSlots.Ranged,
            payload.GetProperty("second").GetProperty("from").GetProperty("index").GetInt32());
    }

    [Fact]
    public async Task GearBarredToTheCharactersClassIsRefused()
    {
        var session = await SessionAsync(BucklerEntry);

        var refusal = Assert.Single(await EquipAsync(session));

        Assert.Contains("\"inventory.equipResult\"", refusal);
        Assert.Contains($"\"code\":\"{ItemErrors.CantEquip}\"", refusal);
        Assert.Equal("bag",
            ItemIn(await SnapshotAsync(session), ItemId).GetProperty("slot").GetProperty("area").GetString());
    }

    [Fact]
    public async Task TheSameGearIsAcceptedByAClassItsMaskAllows()
    {
        var session = await SessionAsync(BucklerEntry, playerClass: PlayerClassIds.Warrior);

        var equipment = AppearanceIn(await EquipAsync(session));

        Assert.Equal(BucklerEntry, equipment[EquipmentSlots.OffHand]);
    }

    [Fact]
    public async Task GearAboveTheCharactersLevelIsRefused()
    {
        var session = await SessionAsync(BowEntry, level: 0);

        Assert.Contains($"\"code\":\"{ItemErrors.CantEquip}\"", Assert.Single(await EquipAsync(session)));
    }

    [Fact]
    public async Task AConsumableCannotBeWorn()
    {
        var session = await SessionAsync(StarterEntry);

        Assert.Contains($"\"code\":\"{ItemErrors.WrongSlot}\"", Assert.Single(await EquipAsync(session)));
    }

    [Fact]
    public async Task ABowNamedOntoTheHeadIsRefused()
    {
        var session = await SessionAsync(BowEntry);

        var refusal = Assert.Single(await EquipAsync(session, to: EquipmentSlots.Head));

        Assert.Contains($"\"code\":\"{ItemErrors.WrongSlot}\"", refusal);
    }

    [Fact]
    public async Task TakingSomethingOffReturnsItToTheBag()
    {
        var session = await SessionAsync(BowEntry);
        await EquipAsync(session);

        var equipment = AppearanceIn(await UnequipAsync(session, EquipmentSlots.Ranged));

        Assert.Equal(0, equipment[EquipmentSlots.Ranged]);
        Assert.Equal("bag",
            ItemIn(await SnapshotAsync(session), ItemId).GetProperty("slot").GetProperty("area").GetString());
    }

    [Fact]
    public async Task GearWornAtLoginSurvivesEquippingSomethingElse()
    {
        var session = await SessionAsync(
            BowEntry, EquipmentSlots.Build((EquipmentSlots.Chest, ChestEntry)));

        var equipment = AppearanceIn(await EquipAsync(session));

        Assert.Equal(ChestEntry, equipment[EquipmentSlots.Chest]);
        Assert.Equal(BowEntry, equipment[EquipmentSlots.Ranged]);
    }

    [Fact]
    public async Task WornGearAppearsInTheSnapshotWithTheRowThatSaysWhereItGoes()
    {
        var session = await SessionAsync(
            StarterEntry, EquipmentSlots.Build((EquipmentSlots.Ranged, BowEntry)));

        var worn = (await SnapshotAsync(session)).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("slot").GetProperty("area").GetString() == "equipment");

        Assert.Equal(BowEntry, worn.GetProperty("entry").GetInt32());
        Assert.Equal(13, worn.GetProperty("details").GetProperty("inventoryType").GetInt32());
        Assert.Equal(1, worn.GetProperty("details").GetProperty("requiredLevel").GetInt32());
    }

    [Fact]
    public async Task AUsableItemCarriesTheOnUseSpellThatMakesItUsable()
    {
        var session = await SessionAsync(StarterEntry);

        var prototype = ItemIn(await SnapshotAsync(session), ItemId).GetProperty("details");
        var spell = Assert.Single(prototype.GetProperty("spells").EnumerateArray());

        Assert.Equal(100, spell.GetProperty("id").GetInt32());
        Assert.Equal(1, spell.GetProperty("trigger").GetInt32());
        Assert.Equal(0, prototype.GetProperty("inventoryType").GetInt32());
    }

    [Fact]
    public async Task UsingAnItemBarredToTheCharactersClassIsRefused()
    {
        var session = await SessionAsync(PotionEntry);

        var responses = await Send(session, WebMessageTypes.InventoryUse, new
        {
            from = new { area = "bag", index = BagSlot },
            itemId = ItemId,
        });

        Assert.Contains("cannot_use", Assert.Single(responses));
    }

    [Fact]
    public async Task ABowEquippedMidSessionMakesADistantTargetAttackable()
    {
        var spawn = new CreatureSpawnDefinition(1001, 13, 9001, 1 + 12, 2, 3, 0);
        var session = await SessionAsync(BowEntry, spawn: spawn);

        var beforeEquipping = await Send(session, WebMessageTypes.AttackEngage,
            new { targetId = "Creature:1" });
        Assert.Contains("invalid_target", Assert.Single(beforeEquipping));

        await EquipAsync(session);

        var afterEquipping = await Send(session, WebMessageTypes.AttackEngage,
            new { targetId = "Creature:1" });
        Assert.Contains(afterEquipping, message => message.Contains("attack.meleeStart"));
    }
}
