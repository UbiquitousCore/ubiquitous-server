using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class BagFullTests
{
    private const string BagFull = ItemErrors.InventoryFull;

    [Fact]
    public async Task LootTake_WithNoRoomInTheBag_SaysTheBagIsFull()
    {
        var session = await SessionAsync(FullBag);
        await KillAsync(session);
        await OpenLootAsync(session);

        var response = await TakeLootAsync(session, slot: 0);

        Assert.Contains("\"type\":\"item.refused\"", response.Single());
        Assert.Contains($"\"code\":\"{BagFull}\"", response.Single());
    }

    [Fact]
    public async Task LootTake_WithNoRoomInTheBag_LeavesTheRowOnTheBody()
    {
        var session = await SessionAsync(FullBag);
        await KillAsync(session);
        await OpenLootAsync(session);
        await TakeLootAsync(session, slot: 0);

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryDestroy, 20,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item", count = 1 })));
        var retry = await TakeLootAsync(session, slot: 0);

        Assert.Contains(retry, message => message.Contains("\"type\":\"loot.slotEmptied\""));
        Assert.Contains(retry, message => message.Contains("\"type\":\"inventory.itemAdded\""));
        Assert.DoesNotContain(retry, message => message.Contains("\"type\":\"item.refused\""));
    }

    [Fact]
    public async Task LootTake_WithRoomInTheBag_SaysNothingAboutErrors()
    {
        var session = await SessionAsync(GameContent.Development);
        await KillAsync(session);
        await OpenLootAsync(session);

        var response = await TakeLootAsync(session, slot: 0);

        Assert.DoesNotContain(response, message => message.Contains("\"type\":\"item.refused\""));
    }

    [Fact]
    public async Task LootTake_OutOfRange_IsStillSilent()
    {
        var session = await SessionAsync(FullBag);
        await KillAsync(session);
        await OpenLootAsync(session);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 10,
            new PlayerMovePayload(16, 2, 3, 0, true, MovementState.Walking))));

        var response = await TakeLootAsync(session, slot: 0);

        Assert.Empty(response);
    }

    [Fact]
    public async Task VendorBuy_WithNoRoomInTheBag_SendsTheRefusalAndTheStorageError()
    {
        var session = await SessionAsync(FullBag);

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorBuy, 30, new { networkId = "Creature:1", slot = 0, entry = 100, count = 1 })));

        Assert.Equal(2, response.Count);
        Assert.Contains("\"type\":\"npc.buyResult\"", response[0]);
        Assert.Contains($"\"code\":\"{BagFull}\"", response[0]);
        Assert.Contains("\"type\":\"item.refused\"", response[1]);
        Assert.Contains($"\"code\":\"{BagFull}\"", response[1]);
    }

    [Fact]
    public async Task VendorBuy_WithNoRoomInTheBag_ChargesNothing()
    {
        var session = await SessionAsync(FullBag);

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorBuy, 30, new { networkId = "Creature:1", slot = 0, entry = 100, count = 1 })));

        Assert.DoesNotContain(response, message => message.Contains("\"type\":\"player.updated\""));
    }

    [Fact]
    public async Task QuestComplete_WithNoRoomForTheReward_SaysTheBagIsFull()
    {
        var session = await QuestSessionAsync();

        var response = await CompleteQuestAsync(session, 3);

        Assert.Contains("\"type\":\"item.refused\"", response.Single());
        Assert.Contains($"\"code\":\"{BagFull}\"", response.Single());
    }

    [Fact]
    public async Task QuestComplete_WithNoRoomForTheReward_LeavesTheQuestHeld()
    {
        var session = await QuestSessionAsync();
        await CompleteQuestAsync(session, 3);

        for (var slot = 0; slot < 3; slot++)
        {
            var id = slot == 0 ? "starter-item" : $"starter-item-{slot}";
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.InventoryDestroy, 20 + slot,
                new { from = new { area = "bag", index = slot }, itemId = id, count = 1 })));
        }

        var retry = await CompleteQuestAsync(session, 4);

        Assert.Contains(retry, message => message.Contains("\"type\":\"quest.updated\""));
        Assert.Contains(retry, message => message.Contains("\"type\":\"inventory.itemAdded\""));
    }

    private static Task<IReadOnlyList<string>> CompleteQuestAsync(PlaySession session, int sequence) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, sequence,
            new { networkId = "Creature:1", questId = 5001, rewardIndex = 0 })));

    private static async Task<PlaySession> QuestSessionAsync()
    {
        var session = await SessionAsync(FullBag);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5001 })));
        return session;
    }

    private static GameContent FullBag => GameContent.Development with
    {
        StartingItems = [.. Enumerable.Range(0, 12).Select(index =>
            new StartingItemStack(checked((uint)(100 + index)), 1))],
    };

    private static Task<IReadOnlyList<string>> TakeLootAsync(PlaySession session, int slot) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.LootTake, 12, new { networkId = "Creature:1", slot })));

    private static Task<IReadOnlyList<string>> OpenLootAsync(PlaySession session) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.LootOpen, 11, new { networkId = "Creature:1" })));

    private static async Task KillAsync(PlaySession session)
    {
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        for (var swing = 0; swing < 20; swing++)
        {
            session.TickCombat(now);
            now += TimeSpan.FromSeconds(2);
        }
    }

    private static async Task<PlaySession> SessionAsync(GameContent content)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            content: content, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
