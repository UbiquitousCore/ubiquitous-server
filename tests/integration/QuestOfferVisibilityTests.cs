using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class QuestOfferVisibilityTests
{
    private static readonly CreatureSpawnDefinition[] Spawns =
    [
        new(1001, 13, 9001, 2f, 2f, 4f, 0f),
    ];

    private static readonly CreatureSpawnDefinition[] TwoGiverSpawns =
    [
        new(1001, 13, 9001, 2f, 2f, 4f, 0f),
        new(1002, 13, 9010, 3f, 3f, 4f, 0f),
    ];

    private static readonly StaticObjectSpawnDefinition[] StaticSpawns =
    [
        new(2001, 13, 7001, 2f, 2f, 4f, 0f),
    ];

    private static readonly CreatureSpawnDefinition[] HubSpawns =
    [
        new(1001, 13, 9020, 2f, 2f, 4f, 0f),
        new(1002, 13, 9021, 3f, 2f, 4f, 0f),
        new(1003, 13, 9022, 4f, 2f, 4f, 0f),
        new(1004, 13, 9023, 5f, 2f, 4f, 0f),
        new(1005, 13, 9024, 6f, 2f, 4f, 0f),
        new(1006, 13, 9025, 7f, 2f, 4f, 0f),
        new(1007, 13, 9026, 8f, 2f, 4f, 0f),
    ];

    private static readonly Dictionary<uint, NpcDefinition> HubNpcs = new()
    {
        [9020] = new(9020, NpcFlags.Gossip, OffersQuests: true),
        [9021] = new(9021, NpcFlags.Gossip, OffersQuests: true),
        [9022] = new(9022, NpcFlags.Gossip, OffersQuests: true),
        [9023] = new(9023, NpcFlags.Gossip, OffersQuests: true),
        [9024] = new(9024, NpcFlags.Gossip, OffersQuests: true),
        [9025] = new(9025, NpcFlags.Gossip, OffersQuests: true),
        [9026] = new(9026, NpcFlags.Gossip, OffersQuests: true),
    };

    private static readonly QuestDefinition[] HubQuests =
    [
        new(7005, 9020, 20155, 20156, MinLevel: 1),
        new(7006, 9021, 20166, 20167, MinLevel: 1),
        new(7007, 9021, 20177, 20178, MinLevel: 1, PrerequisitesAll: [7006]),
        new(7008, 9022, 20188, 20189, MinLevel: 1, PrerequisitesAll: [7006]),
        new(7009, 9021, 20199, 20200, MinLevel: 1, PrerequisitesAll: [7007]),
        new(7010, 9027, 20210, 20211, MinLevel: 1, PrerequisitesAll: [7009]),
        new(7011, 9023, 20221, 20222, MinLevel: 1, PrerequisitesAll: [7010]),
        new(7012, 9021, 20232, 20233, MinLevel: 1, PrerequisitesAll: [7011]),
        new(7013, 9024, 20240, 20241, MinLevel: 3),
        new(7017, 9025, 20250, 20251, MinLevel: 2, PrerequisitesAll: [7005]),
        new(7018, 9026, 20260, 20261, MinLevel: 1),
    ];

    [Fact]
    public async Task QuestList_HidesAClassRestrictedQuestFromTheWrongClass()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5001, 9001, 12001, 12002, 1, AllowedClassMask: 0b0010)],
        };
        var session = await SessionAsync(content, Spawns);

        var list = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestList, 2, new { networkId = "Creature:1" }))));

        Assert.DoesNotContain("5001", list);
    }

    [Fact]
    public async Task QuestList_HidesAClassRestrictedStaticObjectQuestFromTheWrongClass()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5002, null, 12001, 12002, 1, AllowedClassMask: 0b0010)],
            QuestStartersByStaticObject = [new StaticObjectQuestBindingDefinition(7001, 5002)],
        };
        var session = await SessionAsync(content, [], StaticSpawns);

        var list = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestList, 2, new { networkId = "StaticObject:1" }))));

        Assert.DoesNotContain("5002", list);
    }

    // ubiquitous-client asks for a mark on every NPC it sees, vendors and teleporters included.
    [Fact]
    public async Task QuestStatus_ReportsNoneForAVisibleNpcThatGivesNoQuests()
    {
        var session = await SessionAsync(GameContent.Development, [new CreatureSpawnDefinition(1001, 13, 9002, 2f, 2f, 4f, 0f)]);

        var status = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestCheckMark, 2, new { networkId = "Creature:1" }))));

        Assert.Contains("quest.statuses", status);
        Assert.Contains("\"none\"", status);
    }

    [Fact]
    public async Task QuestStatus_ReportsNoneForAClassRestrictedGiverFromTheWrongClass()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5003, 9001, 12001, 12002, 1, AllowedClassMask: 0b0010)],
        };
        var session = await SessionAsync(content, Spawns);

        var status = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestCheckMark, 2, new { networkId = "Creature:1" }))));

        Assert.DoesNotContain("\"available\"", status);
        Assert.Contains("\"none\"", status);
    }

    [Fact]
    public async Task InventoryUse_RefusesAClassRestrictedQuestStarterItemFromTheWrongClass()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5004, null, 12001, 12002, 1, AllowedClassMask: 0b0010)],
            Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
            {
                [9999] = new(9999, "item-quest-starter", Class: 0, SubClass: 0, DisplayInfoId: 0, QuestId: 5004),
            },
            StartingItems = [new StartingItemStack(9999, 1)],
        };
        var session = await SessionAsync(content, []);

        var use = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 2,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item" }))));

        Assert.DoesNotContain("5004", use);
    }

    [Fact]
    public async Task QuestList_NoLongerOffersAQuestAlreadyInTheLog()
    {
        var content = GameContent.Development with
        {
            Quests =
            [
                new QuestDefinition(5005, 9001, 12001, 12002, 1),
                new QuestDefinition(5006, 9001, 13001, 13002, 1),
            ],
        };
        var session = await SessionAsync(content, Spawns);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5005u })));

        var list = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestList, 3, new { networkId = "Creature:1" }))));

        Assert.Contains("{\"id\":5005", list);
        Assert.DoesNotContain("{\"id\":5005,\"titleStringId\":12001,\"detailsStringId\":12002,\"status\":\"offered\"", list);
        Assert.Contains("{\"id\":5005,\"titleStringId\":12001,\"detailsStringId\":12002,\"status\":\"complete\"", list);
        Assert.Contains("{\"id\":5006,\"titleStringId\":13001,\"detailsStringId\":13002,\"status\":\"offered\"", list);
    }

    [Fact]
    public async Task QuestList_NoLongerOffersANonRepeatableQuestAlreadyTurnedIn()
    {
        var content = GameContent.Development with
        {
            Quests =
            [
                new QuestDefinition(5007, 9001, 12001, 12002, 1),
                new QuestDefinition(5008, 9001, 13001, 13002, 1),
            ],
            QuestRewards = new Dictionary<uint, QuestRewardDefinition>
            {
                [5007] = new(5007, 0, [], []),
            },
        };
        var session = await SessionAsync(content, Spawns);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5007u })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 3, new { networkId = "Creature:1", questId = 5007u, rewardIndex = 0 })));

        var list = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestList, 4, new { networkId = "Creature:1" }))));

        Assert.DoesNotContain("5007", list);
        Assert.Contains("5008", list);
    }

    [Fact]
    public async Task QuestAccept_RefusesTheSameQuestASecondTimeAfterItIsAlreadyTurnedIn()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5009, 9001, 12001, 12002, 1)],
            QuestRewards = new Dictionary<uint, QuestRewardDefinition>
            {
                [5009] = new(5009, 0, [], []),
            },
        };
        var session = await SessionAsync(content, Spawns);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5009u })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 3, new { networkId = "Creature:1", questId = 5009u, rewardIndex = 0 })));

        var reaccept = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 4, new { networkId = "Creature:1", questId = 5009u }))));

        Assert.Contains("\"type\":\"protocol.error\"", reaccept);
    }

    [Fact]
    public async Task QuestList_ScopesEachGiversOfferToItsOwnGiverIdAndDoesNotBleedBetweenGivers()
    {
        var content = GameContent.Development with
        {
            Quests =
            [
                new QuestDefinition(6001, 9001, 12001, 12002, 1),
                new QuestDefinition(6002, 9001, 13001, 13002, 1),
                new QuestDefinition(6003, 9010, 14001, 14002, 1),
                new QuestDefinition(6004, 9010, 15001, 15002, 1),
            ],
        };
        var session = await SessionAsync(content, TwoGiverSpawns);

        var listA = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestList, 2, new { networkId = "Creature:1" }))));
        var listB = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestList, 3, new { networkId = "Creature:2" }))));

        Assert.Contains("\"giverId\":\"Creature:1\"", listA);
        Assert.Contains("\"giverId\":\"Creature:2\"", listB);

        Assert.Contains("6001", listA);
        Assert.Contains("6002", listA);
        Assert.DoesNotContain("6003", listA);
        Assert.DoesNotContain("6004", listA);

        Assert.Contains("6003", listB);
        Assert.Contains("6004", listB);
        Assert.DoesNotContain("6001", listB);
        Assert.DoesNotContain("6002", listB);
    }

    [Fact]
    public async Task QuestList_HidesAQuestGatedBehindAnotherUntilThatOtherOneIsFinished()
    {
        var content = GameContent.Development with
        {
            Quests =
            [
                new QuestDefinition(6101, 9001, 12001, 12002, 1),
                new QuestDefinition(6102, 9001, 13001, 13002, 1),
                new QuestDefinition(6103, 9001, 14001, 14002, 1, PrerequisitesAll: [6101]),
            ],
            QuestRewards = new Dictionary<uint, QuestRewardDefinition>
            {
                [6101] = new(6101, 0, [], []),
            },
        };
        var session = await SessionAsync(content, Spawns);

        var beforeFinishing = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestList, 2, new { networkId = "Creature:1" }))));
        Assert.Contains("6101", beforeFinishing);
        Assert.Contains("6102", beforeFinishing);
        Assert.DoesNotContain("6103", beforeFinishing);

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 3, new { networkId = "Creature:1", questId = 6101u })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 4, new { networkId = "Creature:1", questId = 6101u, rewardIndex = 0 })));

        var afterFinishing = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestList, 5, new { networkId = "Creature:1" }))));
        Assert.DoesNotContain("6101", afterFinishing);
        Assert.Contains("6102", afterFinishing);
        Assert.Contains("6103", afterFinishing);
    }

    [Fact]
    public async Task QuestAccept_RefusesAQuestWhoseRequiredQuestIsNotYetFinishedEvenIfAskedForDirectly()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(6104, 9001, 12001, 12002, 1, PrerequisitesAll: [6105])],
        };
        var session = await SessionAsync(content, Spawns);

        var accept = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 6104u }))));

        Assert.Contains("\"type\":\"protocol.error\"", accept);
    }

    private static async Task<PlaySession> SessionAsync(
        GameContent content, IReadOnlyList<CreatureSpawnDefinition> spawns) =>
        await SessionAsync(content, spawns, []);

    private static async Task<PlaySession> SessionAsync(
        GameContent content, IReadOnlyList<CreatureSpawnDefinition> spawns,
        IReadOnlyList<StaticObjectSpawnDefinition> staticSpawns, int level = 4)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character(level)]), spawns, staticSpawns, content, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static CharacterRecord Character(int level = 4) => new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, level, [100, 200]);

    // Ayla is a ranger: the dummy quest asking for her own Rapid Shot is hers, the mage's Fireball one is not.
    [Fact]
    public async Task AQuestAskingForASpell_IsOfferedOnlyToThoseWhoHaveIt()
    {
        var content = GameContent.Development with
        {
            Quests =
            [
                new QuestDefinition(6501, 9001, 12001, 12002, 1,
                    [new QuestObjectiveDefinition(0, QuestObjectiveKind.Triggered, 1, ObjectiveSpell: 5692)]),
                new QuestDefinition(6502, 9001, 13001, 13002, 1,
                    [new QuestObjectiveDefinition(0, QuestObjectiveKind.Triggered, 1, ObjectiveSpell: 5000)]),
            ],
        };
        var session = await SessionAsync(content, Spawns);

        var list = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestList, 2, new { networkId = "Creature:1" }))));
        var refused = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 3, new { networkId = "Creature:1", questId = 6502u }))));

        Assert.Contains("\"id\":6501", list);
        Assert.DoesNotContain("\"id\":6502", list);
        Assert.Contains("invalid_quest", refused);
    }

    [Fact]
    public async Task QuestList_HidesAQuestAboveThePlayersLevel()
    {
        var content = GameContent.Development with
        {
            Quests =
            [
                new QuestDefinition(5010, 9001, 12001, 12002, MinLevel: 5),
                new QuestDefinition(5011, 9001, 13001, 13002, MinLevel: 1),
            ],
        };
        var session = await SessionAsync(content, Spawns);

        var list = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestList, 2, new { networkId = "Creature:1" }))));

        Assert.DoesNotContain("5010", list);
        Assert.Contains("5011", list);
    }

    [Fact]
    public async Task QuestStatus_ReportsNoneForAGiverWhoseOnlyOfferIsAboveThePlayersLevel()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5012, 9001, 12001, 12002, MinLevel: 5)],
        };
        var session = await SessionAsync(content, Spawns);

        var status = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestCheckMark, 2, new { networkId = "Creature:1" }))));

        Assert.DoesNotContain("\"available\"", status);
        Assert.Contains("\"none\"", status);
    }

    [Fact]
    public async Task InventoryUse_RefusesAQuestStarterItemAboveThePlayersLevel()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5013, null, 12001, 12002, MinLevel: 5)],
            Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
            {
                [9998] = new(9998, "item-quest-starter-high-level", Class: 0, SubClass: 0, DisplayInfoId: 0, QuestId: 5013),
            },
            StartingItems = [new StartingItemStack(9998, 1)],
        };
        var session = await SessionAsync(content, []);

        var use = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 2,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item" }))));

        Assert.DoesNotContain("5013", use);
    }

    [Fact]
    public async Task QuestList_ScopesAndGatesEachOfSevenNearbyHubGiversCorrectly()
    {
        var content = GameContent.Development with { Npcs = HubNpcs, Quests = HubQuests };
        var session = await SessionAsync(content, HubSpawns, [], level: 1);

        var giverToNetworkId = new Dictionary<uint, string>
        {
            [9020] = "Creature:1",
            [9021] = "Creature:2",
            [9022] = "Creature:3",
            [9023] = "Creature:4",
            [9024] = "Creature:5",
            [9025] = "Creature:6",
            [9026] = "Creature:7",
        };

        var replies = new Dictionary<uint, string>();
        uint seq = 2;
        foreach (var (giver, networkId) in giverToNetworkId)
        {
            replies[giver] = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
                WebMessageEnvelope.Create(WebMessageTypes.QuestList, seq++, new { networkId }))));
        }

        Assert.Contains("\"type\":\"quest.details\"", replies[9020]);
        Assert.Contains("\"id\":7005,", replies[9020]);

        Assert.Contains("\"type\":\"quest.details\"", replies[9021]);
        Assert.Contains("\"id\":7006,", replies[9021]);
        Assert.DoesNotContain("\"id\":7007,", replies[9021]);
        Assert.DoesNotContain("\"id\":7009,", replies[9021]);
        Assert.DoesNotContain("\"id\":7012,", replies[9021]);

        Assert.Contains("\"type\":\"quest.details\"", replies[9026]);
        Assert.Contains("\"id\":7018,", replies[9026]);

        foreach (var giver in new uint[] { 9022, 9023, 9024, 9025 })
        {
            Assert.Contains("\"type\":\"quest.listUpdated\"", replies[giver]);
            Assert.Contains("\"quests\":[]", replies[giver]);
        }
    }
}
