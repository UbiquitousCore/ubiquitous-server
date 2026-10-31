using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class QuestFinisherMarkTests
{
    private static readonly CreatureSpawnDefinition[] HubSpawns =
    [
        new(1101, 1, 9030, 2, 7, 63.5f, 0, WanderRadius: 4),
        new(1102, 1, 9032, -3.5f, 7, 62.5f, 0, WanderRadius: 4),
        new(1103, 1, 9031, 8, 7, 62.5f, 0, WanderRadius: 4),
        new(1104, 1, 9033, 5, 7, 64.5f, 0, WanderRadius: 4),
        new(1105, 1, 9034, -1, 7, 65, 0, WanderRadius: 4),
    ];

    private const string RangerGiver = "Creature:1";
    private const string MageLine = "Creature:2";
    private const string WarriorLine = "Creature:3";
    private const string RangerFinisher = "Creature:4";
    private const string MonkFinisher = "Creature:5";

    [Fact]
    public async Task Hub_OffersARangerExactlyOneQuestOutOfTwelve()
    {
        var session = await SessionAsync();

        Assert.Equal("offered", await StatusOf(session, RangerGiver));
        Assert.Equal("none", await StatusOf(session, MageLine));
        Assert.Equal("none", await StatusOf(session, WarriorLine));
        Assert.Equal("none", await StatusOf(session, MonkFinisher));
        Assert.Equal("none", await StatusOf(session, RangerFinisher));

        var opened = Assert.Single(await Send(session, WebMessageTypes.QuestList, new { networkId = RangerGiver }));
        Assert.Contains("\"type\":\"quest.details\"", opened);
        Assert.Contains("\"id\":7645", opened);
        Assert.DoesNotContain("\"id\":7019", opened);
        Assert.DoesNotContain("\"id\":7022", opened);
        Assert.DoesNotContain("\"id\":7646", opened);
    }

    [Fact]
    public async Task AcceptedQuest_MovesTheMarkFromItsGiverToItsFinisher()
    {
        var session = await SessionAsync();
        await Send(session, WebMessageTypes.QuestAccept, new { networkId = RangerGiver, questId = 7645u });

        Assert.Equal("none", await StatusOf(session, RangerGiver));
        Assert.Equal("complete", await StatusOf(session, RangerFinisher));
        Assert.Equal("none", await StatusOf(session, MageLine));
        Assert.Equal("none", await StatusOf(session, WarriorLine));
        Assert.Equal("none", await StatusOf(session, MonkFinisher));
    }

    [Fact]
    public async Task TheMarkedFinisherOpensTheHandInRatherThanAnEmptyDialog()
    {
        var session = await SessionAsync();
        await Send(session, WebMessageTypes.QuestAccept, new { networkId = RangerGiver, questId = 7645u });

        var opened = Assert.Single(await Send(session, WebMessageTypes.QuestList, new { networkId = RangerFinisher }));

        Assert.Contains("\"type\":\"quest.details\"", opened);
        Assert.Contains("\"id\":7645", opened);
        Assert.Contains("\"status\":\"complete\"", opened);
    }

    [Fact]
    public async Task HandingInAtTheFinisherUnlocksTheNextQuestInTheChain()
    {
        var session = await SessionAsync();
        await Send(session, WebMessageTypes.QuestAccept, new { networkId = RangerGiver, questId = 7645u });

        var handIn = await Send(session, WebMessageTypes.QuestComplete,
            new { networkId = RangerFinisher, questId = 7645u, rewardIndex = 0 });
        Assert.Contains(handIn, response => response.Contains("\"status\":\"complete\""));
        Assert.DoesNotContain(handIn, response => response.Contains("invalid_quest"));

        Assert.Contains(handIn, response =>
            response.Contains("\"type\":\"quest.removed\"") && response.Contains("\"questId\":7645"));

        Assert.Equal("offered", await StatusOf(session, RangerFinisher));
        Assert.Equal("none", await StatusOf(session, RangerGiver));
        Assert.Equal("none", await StatusOf(session, MageLine));
        Assert.Equal("none", await StatusOf(session, WarriorLine));
        Assert.Equal("none", await StatusOf(session, MonkFinisher));

        var opened = Assert.Single(await Send(session, WebMessageTypes.QuestList, new { networkId = RangerFinisher }));
        Assert.Contains("\"type\":\"quest.details\"", opened);
        Assert.Contains("\"id\":7620", opened);
        Assert.Contains("\"status\":\"offered\"", opened);
    }

    [Fact]
    public async Task HandingInIsStillRefusedAtTheQuestsOwnGiver()
    {
        var session = await SessionAsync();
        await Send(session, WebMessageTypes.QuestAccept, new { networkId = RangerGiver, questId = 7645u });

        var refused = await Send(session, WebMessageTypes.QuestComplete,
            new { networkId = RangerGiver, questId = 7645u, rewardIndex = 0 });

        Assert.Contains(refused, response => response.Contains("invalid_quest"));
    }

    [Fact]
    public async Task RefreshStatuses_MarksOnlyTheFinisherWhileTheQuestIsHeld()
    {
        var session = await SessionAsync();
        await Send(session, WebMessageTypes.QuestAccept, new { networkId = RangerGiver, questId = 7645u });

        var sweep = Assert.Single(await Send(session, WebMessageTypes.QuestRefreshStatuses, new { }));

        using var document = JsonDocument.Parse(sweep);
        var marked = document.RootElement.GetProperty("payload").GetProperty("statuses").EnumerateArray()
            .Where(row => row.GetProperty("status").GetString() != "none")
            .Select(row => row.GetProperty("networkId").GetString()!)
            .ToArray();

        Assert.Equal([RangerFinisher], marked);
    }

    [Fact]
    public async Task AFinisherWhoGivesNoQuestsLosesItsMarkOnceTheQuestIsHandedIn()
    {
        // Someone who only takes a quest in: once it is in, the mark over them
        // has to be cleared, or the client still shows it and asks them for
        // quests they do not have.
        var session = await SessionAsync(HubContent with
        {
            Npcs = new Dictionary<uint, NpcDefinition>(HubContent.Npcs)
            {
                [9033] = new(9033, NpcFlags.None),
            },
        });
        await Send(session, WebMessageTypes.QuestAccept, new { networkId = RangerGiver, questId = 7645u });
        Assert.Equal("complete", await StatusOf(session, RangerFinisher));

        await Send(session, WebMessageTypes.QuestComplete,
            new { networkId = RangerFinisher, questId = 7645u, rewardIndex = 0 });

        Assert.Equal("none", await StatusOf(session, RangerFinisher));
        var sweep = Assert.Single(await Send(session, WebMessageTypes.QuestRefreshStatuses, new { }));
        using var document = JsonDocument.Parse(sweep);
        var finisher = Assert.Single(document.RootElement.GetProperty("payload").GetProperty("statuses")
            .EnumerateArray(), row => row.GetProperty("networkId").GetString() == RangerFinisher);
        Assert.Equal("none", finisher.GetProperty("status").GetString());
    }

    private static async Task<string> StatusOf(PlaySession session, string networkId)
    {
        var response = Assert.Single(await Send(session, WebMessageTypes.QuestCheckMark, new { networkId }));
        using var document = JsonDocument.Parse(response);
        var statuses = document.RootElement.GetProperty("payload").GetProperty("statuses");
        return Assert.Single(statuses.EnumerateArray()).GetProperty("status").GetString()!;
    }

    private static int _sequence = 100;

    private static Task<IReadOnlyList<string>> Send(PlaySession session, string type, object payload) =>
        session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, Interlocked.Increment(ref _sequence), payload)));

    private static async Task<PlaySession> SessionAsync(GameContent? content = null)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, PlayerClassIds.Ranger, 4, 5, 6, 7, 8,
                MapId: 1, X: 2f, Y: 7f, Z: 63f, Level: 8, [100, 200])]),
            HubSpawns, null, content ?? HubContent);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static readonly GameContent HubContent = GameContent.Development with
    {
        Npcs = new Dictionary<uint, NpcDefinition>
        {
            [9030] = new(9030, NpcFlags.None, OffersQuests: true),
            [9031] = new(9031, NpcFlags.None, OffersQuests: true),
            [9032] = new(9032, NpcFlags.None, OffersQuests: true),
            [9033] = new(9033, NpcFlags.None, OffersQuests: true),
            [9034] = new(9034, NpcFlags.None, OffersQuests: true),
        },
        Creatures = new Dictionary<uint, CreatureDefinition>
        {
            [9030] = new(9030, "creature-9030", 8, 280, MinDamage: 25, MaxDamage: 35,
                RespawnTimeMinSeconds: 120, RespawnTimeMaxSeconds: 120),
            [9031] = new(9031, "creature-9031", 3, 140, MinDamage: 10, MaxDamage: 18,
                RespawnTimeMinSeconds: 120, RespawnTimeMaxSeconds: 120),
            [9032] = new(9032, "creature-9032", 6, 220, MinDamage: 20, MaxDamage: 30,
                RespawnTimeMinSeconds: 120, RespawnTimeMaxSeconds: 120),
            [9033] = new(9033, "creature-9033", 15, 650, MinDamage: 60, MaxDamage: 75,
                RespawnTimeMinSeconds: 120, RespawnTimeMaxSeconds: 120),
            [9034] = new(9034, "creature-9034", 15, 650, MinDamage: 60, MaxDamage: 75,
                RespawnTimeMinSeconds: 120, RespawnTimeMaxSeconds: 120),
        },
        Quests =
        [
            new QuestDefinition(7019, 9030, 20309, 20310, 1, AllowedClassMask: 2),
            new QuestDefinition(7022, 9030, 20342, 20343, 1, AllowedClassMask: 4),
            new QuestDefinition(7645, 9030, 27195, 27196, 0, AllowedClassMask: 8),
            new QuestDefinition(7646, 9030, 27206, 27207, 0, AllowedClassMask: 16),
            new QuestDefinition(7020, 9031, 20320, 20321, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Triggered, GoalCount: 1, ObjectiveStringId: 20327)],
                AllowedClassMask: 2, PrerequisitesAll: [7019]),
            new QuestDefinition(7025, 9031, 20375, 20376, 1, PrerequisitesAll: [7020]),
            new QuestDefinition(7021, 9032, 20331, 20332, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Triggered, GoalCount: 1, ObjectiveStringId: 20338)],
                AllowedClassMask: 4, PrerequisitesAll: [7022]),
            new QuestDefinition(7024, 9032, 20364, 20365, 1, PrerequisitesAll: [7021]),
            new QuestDefinition(7620, 9033, 26920, 26921, 0,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Triggered, GoalCount: 1, ObjectiveStringId: 26927)],
                PrerequisitesAll: [7645]),
            new QuestDefinition(7647, 9033, 27217, 27218, 0, PrerequisitesAll: [7620]),
            new QuestDefinition(7613, 9034, 26843, 26844, 0,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Triggered, GoalCount: 1, ObjectiveStringId: 26850)],
                PrerequisitesAll: [7646]),
            new QuestDefinition(7648, 9034, 27228, 27229, 0, PrerequisitesAll: [7613]),
        ],
        QuestRewards = new Dictionary<uint, QuestRewardDefinition>(),
        QuestFinishersByCreature =
        [
            new CreatureQuestBindingDefinition(9031, 7019),
            new CreatureQuestBindingDefinition(9031, 7020),
            new CreatureQuestBindingDefinition(9032, 7021),
            new CreatureQuestBindingDefinition(9032, 7022),
            new CreatureQuestBindingDefinition(9035, 7024),
            new CreatureQuestBindingDefinition(9035, 7025),
            new CreatureQuestBindingDefinition(9034, 7613),
            new CreatureQuestBindingDefinition(9033, 7620),
            new CreatureQuestBindingDefinition(9033, 7645),
            new CreatureQuestBindingDefinition(9034, 7646),
            new CreatureQuestBindingDefinition(9035, 7647),
            new CreatureQuestBindingDefinition(9035, 7648),
        ],
    };
}
