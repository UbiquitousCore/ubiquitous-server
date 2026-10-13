using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class QuestObjectiveTests
{
    private static readonly CreatureSpawnDefinition[] Spawns =
    [
        new(1001, 13, 9001, 2f, 2f, 4f, 0f),
    ];

    private static GameContent KillQuestContent() => GameContent.Development with
    {
        Quests =
        [
            new QuestDefinition(5001, 9001, 12001, 12002, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 1, ObjectiveCreature: 9001)]),
        ],
    };

    [Fact]
    public async Task WorldEntry_SendsAnEmptyQuestLogOnceAndOnlyOnce()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character()]), Spawns);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var logMessages = responses.Where(message => message.Contains("\"type\":\"quest.log\"")).ToArray();
        var log = Assert.Single(logMessages);
        Assert.Contains("\"quests\":[]", log);
    }

    [Fact]
    public async Task QuestList_ShowsAnOfferedQuestsObjectivesAtZeroProgress()
    {
        var session = await SessionAsync();

        var response = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestList, 2, new { networkId = "Creature:1" }))));

        Assert.Contains("\"type\":\"quest.details\"", response);
        Assert.Contains("\"objectives\":[{\"index\":0", response);
        Assert.Contains("\"goalCount\":1", response);
        Assert.Contains("\"killed\":0", response);
    }

    [Fact]
    public async Task Accept_EmitsAnObjectiveStartingAtZeroWithItsSlot()
    {
        var session = await SessionAsync();

        var response = await AcceptAsync(session);

        Assert.Contains("\"type\":\"quest.updated\"", response);
        Assert.Contains("\"slot\":0", response);
        Assert.Contains("\"readyToHandIn\":false", response);
        Assert.Contains("\"goalCount\":1", response);
        Assert.Contains("\"killed\":0", response);
    }

    [Fact]
    public async Task KillingTheRequiredCreature_MovesTheObjectiveAndAllowsCompletion()
    {
        var session = await SessionAsync();
        await AcceptAsync(session);

        var tick = await KillAsync(session);

        var update = Assert.Single(tick, message => message.Contains("\"type\":\"quest.updated\""));
        Assert.Contains("\"readyToHandIn\":true", update);
        Assert.Contains("\"kind\":\"killed\"", update);
        Assert.Contains("\"count\":1", update);

        var complete = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 10, new { networkId = "Creature:1", questId = 5001u, rewardIndex = 0 })));
        Assert.Contains(complete, message => message.Contains("\"status\":\"complete\""));
    }

    [Fact]
    public async Task Complete_IsRefusedBeforeTheRequiredCreatureIsKilled()
    {
        var session = await SessionAsync();
        await AcceptAsync(session);

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 5, new { networkId = "Creature:1", questId = 5001u, rewardIndex = 0 })));

        Assert.Contains("invalid_quest", Assert.Single(response));
    }

    [Fact]
    public async Task KillingAnUnrelatedCreature_MovesNothing()
    {
        var session = await SessionAsync(KillQuestContent(),
            [new CreatureSpawnDefinition(1001, 13, 9001, 200f, 2f, 4f, 0f),
             new CreatureSpawnDefinition(1002, 13, 9003, 2.5f, 2f, 4f, 0f)]);
        await AcceptAsync(session);

        var tick = await KillAsync(session, "Creature:2");

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"quest.updated\""));
    }

    private static GameContent HeldQuestContent() => GameContent.Development with
    {
        Quests =
        [
            new QuestDefinition(5001, 9001, 12001, 12002, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Held, GoalCount: 3, ObjectiveItem: 300)]),
        ],
        VendorTabs = new Dictionary<uint, IReadOnlyList<VendorTab>>
        {
            [9001] = [new VendorTab(0, 0, 1, true, [new VendorItem(0, 300, 5301, 701, 10, 10, 1)])],
        },
    };

    [Fact]
    public async Task BuyingTheRequiredItem_MovesTheHeldObjectiveAndAllowsCompletion()
    {
        var session = await SessionAsync(HeldQuestContent(), Spawns);
        var accepted = await AcceptAsync(session);
        Assert.Contains("\"held\":0", accepted);
        Assert.Contains("\"readyToHandIn\":false", accepted);

        var buy = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorBuy, 3, new { networkId = "Creature:1", slot = 0, entry = 300, count = 3 })));

        var update = Assert.Single(buy, message => message.Contains("\"type\":\"quest.updated\""));
        Assert.Contains("\"held\":3", update);
        Assert.Contains("\"readyToHandIn\":true", update);

        var complete = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 4, new { networkId = "Creature:1", questId = 5001u, rewardIndex = 0 })));
        Assert.Contains(complete, message => message.Contains("\"status\":\"complete\""));
        Assert.Contains(complete, message => message.Contains("inventory.itemRemoved"));
    }

    [Fact]
    public async Task Accept_CreditsAnItemAlreadyBoughtBeforeAccepting()
    {
        var session = await SessionAsync(HeldQuestContent(), Spawns);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorBuy, 2, new { networkId = "Creature:1", slot = 0, entry = 300, count = 3 })));

        var accepted = await AcceptAsync(session);

        Assert.Contains("\"held\":3", accepted);
        Assert.Contains("\"readyToHandIn\":true", accepted);
    }

    [Fact]
    public async Task Complete_IsRefusedWithoutEnoughOfTheHeldItem()
    {
        var session = await SessionAsync(HeldQuestContent(), Spawns);
        await AcceptAsync(session);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorBuy, 3, new { networkId = "Creature:1", slot = 0, entry = 300, count = 2 })));

        var complete = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 4, new { networkId = "Creature:1", questId = 5001u, rewardIndex = 0 })));

        Assert.Contains("invalid_quest", Assert.Single(complete));
    }

    [Fact]
    public async Task DestroyingAHeldItem_MovesTheObjectiveBackDown()
    {
        var session = await SessionAsync(HeldQuestContent(), Spawns);
        await AcceptAsync(session);
        var buy = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorBuy, 3, new { networkId = "Creature:1", slot = 0, entry = 300, count = 3 })));
        var added = System.Text.Json.JsonDocument.Parse(
                buy.Single(message => message.Contains("inventory.itemAdded")))
            .RootElement.GetProperty("payload");
        var addedId = added.GetProperty("id").GetString()!;
        var addedSlot = added.GetProperty("slot").GetProperty("index").GetInt32();

        var destroy = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryDestroy, 5,
            new { itemId = addedId, from = new { area = "bag", index = addedSlot }, count = 3 })));

        var update = Assert.Single(destroy, message => message.Contains("\"type\":\"quest.updated\""));
        Assert.Contains("\"held\":0", update);
        Assert.Contains("\"readyToHandIn\":false", update);
    }

    [Fact]
    public async Task SellingAHeldItemBackToTheVendor_MovesTheObjectiveBackDownToo()
    {
        var session = await SessionAsync(HeldQuestContent(), Spawns);
        await AcceptAsync(session);
        var buy = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorBuy, 3, new { networkId = "Creature:1", slot = 0, entry = 300, count = 3 })));
        var added = System.Text.Json.JsonDocument.Parse(
                buy.Single(message => message.Contains("inventory.itemAdded")))
            .RootElement.GetProperty("payload");
        var addedId = added.GetProperty("id").GetString()!;
        var addedSlot = added.GetProperty("slot").GetProperty("index").GetInt32();

        var sell = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorSell, 5,
            new { networkId = "Creature:1", from = new { area = "bag", index = addedSlot }, itemId = addedId })));

        var update = Assert.Single(sell, message => message.Contains("\"type\":\"quest.updated\""));
        Assert.Contains("\"held\":0", update);
        Assert.Contains("\"readyToHandIn\":false", update);
    }

    private static async Task<string> AcceptAsync(PlaySession session) =>
        Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5001u }))));

    private static async Task<IReadOnlyList<string>> KillAsync(PlaySession session, string targetId = "Creature:1")
    {
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId })));
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        var all = new List<string>();
        for (var swing = 0; swing < 20; swing++)
        {
            all.AddRange(session.TickCombat(now));
            now += TimeSpan.FromSeconds(2);
        }

        return all;
    }

    private static Task<PlaySession> SessionAsync() =>
        SessionAsync(KillQuestContent(), Spawns);

    private static async Task<PlaySession> SessionAsync(GameContent content, IReadOnlyList<CreatureSpawnDefinition> spawns)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character()]), spawns, null, content, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static CharacterRecord Character() => new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);
}
