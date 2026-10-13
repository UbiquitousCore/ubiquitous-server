using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class QuestGuideTests
{
    private static readonly CreatureSpawnDefinition[] KillSpawns =
    [
        new(1001, 13, 9001, 2f, 2f, 4f, 0f),
        new(1002, 13, 9002, 2f, 2f, 4f, 0f),
        new(1003, 13, 9002, 500f, 2f, 4f, 0f),
    ];

    private static readonly StaticObjectSpawnDefinition[] StaticSpawns =
    [
        new(2001, 13, 7001, 6f, 2f, 4f, 0f),
    ];

    private static GameContent KillQuestContent() => GameContent.Development with
    {
        Quests =
        [
            new QuestDefinition(5001, 9001, 12001, 12002, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 1, ObjectiveCreature: 9002)]),
        ],
    };

    [Fact]
    public async Task Accept_CarriesTheNearestSpawnOfTheRequiredCreatureAsAGuideZone()
    {
        var session = await SessionAsync(KillQuestContent(), KillSpawns);

        var accepted = await AcceptAsync(session);

        Assert.Contains("\"guideZones\"", accepted);
        Assert.Contains("\"id\":1002", accepted);
        Assert.Contains("\"mapId\":13", accepted);
        Assert.DoesNotContain("\"id\":1003", accepted);
    }

    [Fact]
    public async Task KillingTheRequiredCreature_ReplacesTheGuideZoneWithAFinishZone()
    {
        var content = KillQuestContent() with
        {
            QuestFinishersByCreature = [new CreatureQuestBindingDefinition(9001, 5001)],
        };
        var session = await SessionAsync(content, KillSpawns);
        await AcceptAsync(session);

        var tick = await KillAsync(session, "Creature:2");

        var update = Assert.Single(tick, message => message.Contains("\"type\":\"quest.updated\""));
        Assert.Contains("\"readyToHandIn\":true", update);
        Assert.Contains("\"handInZone\"", update);
        Assert.Contains("\"id\":1001", update);
        Assert.DoesNotContain("\"guideZones\"", update);
    }

    [Fact]
    public async Task AFinishableQuestWithNoFinisherBinding_CarriesNoFinishZone()
    {
        var session = await SessionAsync(KillQuestContent(), KillSpawns);
        await AcceptAsync(session);

        var tick = await KillAsync(session, "Creature:2");

        var update = Assert.Single(tick, message => message.Contains("\"type\":\"quest.updated\""));
        Assert.Contains("\"readyToHandIn\":true", update);
        Assert.DoesNotContain("\"handInZone\":{", update);
    }

    [Fact]
    public async Task ATriggeredObjective_CarriesTheNearestStaticObjectSpawnAsAGuideZone()
    {
        var content = GameContent.Development with
        {
            Quests =
            [
                new QuestDefinition(5001, 9001, 12001, 12002, 1,
                    [new QuestObjectiveDefinition(0, QuestObjectiveKind.Triggered, GoalCount: 1, ObjectiveObject: 7001)]),
            ],
        };
        var session = await SessionAsync(content, KillSpawns, StaticSpawns);

        var accepted = await AcceptAsync(session);

        Assert.Contains("\"guideZones\"", accepted);
        Assert.Contains("\"id\":2001", accepted);
    }

    [Fact]
    public async Task AHeldObjective_CarriesNoGuideZoneAtAll()
    {
        var content = GameContent.Development with
        {
            Quests =
            [
                new QuestDefinition(5001, 9001, 12001, 12002, 1,
                    [new QuestObjectiveDefinition(0, QuestObjectiveKind.Held, GoalCount: 1, ObjectiveItem: 300)]),
            ],
        };
        var session = await SessionAsync(content, KillSpawns);

        var accepted = await AcceptAsync(session);

        Assert.DoesNotContain("\"guideZones\":[", accepted);
    }

    [Fact]
    public async Task AnObjectiveWithNoSpawnOnTheCurrentMap_CarriesNoGuideZone()
    {
        var content = GameContent.Development with
        {
            Quests =
            [
                new QuestDefinition(5001, 9001, 12001, 12002, 1,
                    [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 1, ObjectiveCreature: 9999)]),
            ],
        };
        var session = await SessionAsync(content, KillSpawns);

        var accepted = await AcceptAsync(session);

        Assert.DoesNotContain("\"guideZones\":[", accepted);
    }

    [Fact]
    public async Task ABusJourneyAcrossMaps_RecomputesTheHeldQuestsGuideZoneForTheNewMap()
    {
        var content = KillQuestContent();
        var spawns = new[]
        {
            new CreatureSpawnDefinition(1001, 13, 9001, 2f, 2f, 4f, 0f),
            new CreatureSpawnDefinition(1002, 13, 9002, 2f, 2f, 4f, 0f),
            new CreatureSpawnDefinition(1003, 14, 9002, 101f, 0f, 101f, 0f),
        };
        var session = await SessionAsync(content, spawns);
        var accepted = await AcceptAsync(session);
        Assert.Contains("\"id\":1002", accepted);
        Assert.Contains("\"mapId\":13", accepted);

        var journey = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.BusConfirm, 5, new { from = 1, to = 3 })));

        var update = Assert.Single(journey, message => message.Contains("\"type\":\"quest.updated\""));
        Assert.Contains("\"guideZones\"", update);
        Assert.Contains("\"id\":1003", update);
        Assert.Contains("\"mapId\":14", update);
    }

    [Fact]
    public async Task AcceptingTheFirstTutorialQuest_CarriesAFinishZoneAtItsFinisher()
    {
        var spawns = new[]
        {
            new CreatureSpawnDefinition(1010, 1, 9010, 24, 3, 4, 3.8f),
            new CreatureSpawnDefinition(1011, 1, 9011, 27, 3, -30, 3.1f),
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([StarterCharacter()]), spawns, [], GameContent.Development,
            combatRollUnit: () => 1.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-ayla" })));

        var accepted = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 1, new { networkId = "Creature:1", questId = 5010u }))));

        Assert.Contains("\"readyToHandIn\":true", accepted);
        Assert.Contains("\"handInZone\"", accepted);
        Assert.Contains("\"id\":1011", accepted);
        Assert.DoesNotContain("\"guideZones\":[", accepted);
    }

    private static CharacterRecord StarterCharacter() => new(
        "char-ayla", 1, "Ayla", 1, 0, 3, 1, 1, 1, 1, 1, 1, 24f, 3f, 4f, 1, [100, 200]);

    private static async Task<string> AcceptAsync(PlaySession session) =>
        Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5001u }))));

    private static async Task<IReadOnlyList<string>> KillAsync(PlaySession session, string targetId)
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

    private static Task<PlaySession> SessionAsync(GameContent content, IReadOnlyList<CreatureSpawnDefinition> spawns) =>
        SessionAsync(content, spawns, []);

    private static async Task<PlaySession> SessionAsync(
        GameContent content, IReadOnlyList<CreatureSpawnDefinition> spawns,
        IReadOnlyList<StaticObjectSpawnDefinition> staticSpawns)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character()]), spawns, staticSpawns, content, combatRollUnit: () => 1.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static CharacterRecord Character() => new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);
}
