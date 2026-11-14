using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

// A build without loot tables: creature 9001's table holds item 100, made a quest item of quest 7001.
public sealed class GeneratedLootTests
{
    private const uint QuestItem = 100;
    private const uint Quest = 7001;

    private static GameContent Content => GameContent.Development with
    {
        Quests =
        [
            .. GameContent.Development.Quests,
            new QuestDefinition(Quest, 9010, 1, 2, 1,
                Objectives: [new QuestObjectiveDefinition(0, QuestObjectiveKind.Held, 5, ObjectiveItem: QuestItem)]),
        ],
        GeneratedQuestItems = new Dictionary<uint, IReadOnlySet<uint>> { [QuestItem] = new HashSet<uint> { Quest } },
    };

    [Fact]
    public async Task AQuestItem_DoesNotDropForAKillerWithoutItsQuest()
    {
        var session = await SessionAsync(holdsQuest: false);
        await KillAsync(session);

        var contents = (await OpenLootAsync(session)).Single(message => message.Contains("\"type\":\"loot.contents\""));

        Assert.DoesNotContain($"\"entry\":{QuestItem}", contents);
    }

    [Fact]
    public async Task AQuestItem_DropsForAKillerWhoseQuestStillNeedsIt()
    {
        var session = await SessionAsync(holdsQuest: true);
        await KillAsync(session);

        var contents = (await OpenLootAsync(session)).Single(message => message.Contains("\"type\":\"loot.contents\""));

        Assert.Contains($"\"entry\":{QuestItem}", contents);
    }

    private sealed class HeldQuest(bool holds) : IQuestLogStore
    {
        public Task<PersistedQuestLog> GetAsync(string characterGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(holds
                ? new PersistedQuestLog([new PersistedQuestLogEntry(Quest, 0, [new PersistedQuestObjective(0, 0)])], [])
                : PersistedQuestLog.Empty);

        public Task SaveAsync(string characterGuid, PersistedQuestLog log, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static Task<IReadOnlyList<string>> OpenLootAsync(PlaySession session) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.LootOpen, 40, new { networkId = "Creature:1" })));

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

    private static async Task<PlaySession> SessionAsync(bool holdsQuest)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            content: Content, combatRollUnit: () => 1.0, questLogStore: new HeldQuest(holdsQuest));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
