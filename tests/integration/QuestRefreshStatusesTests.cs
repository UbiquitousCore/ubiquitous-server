using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class QuestRefreshStatusesTests
{
    private static readonly CreatureSpawnDefinition[] Spawns =
    [
        new(1001, 13, 9001, 2, 2, 4, 0),
    ];

    [Fact]
    public async Task RefreshStatuses_ReportsEveryVisibleGiverAtOnce()
    {
        var session = await SessionAsync(GameContent.Development);

        var response = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestRefreshStatuses, 5, new { }))));

        Assert.Contains("\"type\":\"quest.statuses\"", response);
        Assert.Contains("\"networkId\":\"Creature:1\"", response);
        Assert.Contains("\"status\":\"offered\"", response);
    }

    [Fact]
    public async Task RefreshStatuses_TurnsFinishedOnceTheHeldQuestsObjectiveIsMet()
    {
        var content = GameContent.Development with
        {
            Quests =
            [
                new QuestDefinition(5001, 9001, 12001, 12002, 1,
                    [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 1, ObjectiveCreature: 9001)]),
            ],
        };
        var session = await SessionAsync(content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5001u })));

        var beforeKill = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestRefreshStatuses, 3, new { }))));
        Assert.Contains("\"status\":\"underway\"", beforeKill);

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 4, new { targetId = "Creature:1" })));
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        for (var swing = 0; swing < 20; swing++)
        {
            session.TickCombat(now);
            now += TimeSpan.FromSeconds(2);
        }

        var afterKill = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestRefreshStatuses, 6, new { }))));
        Assert.Contains("\"status\":\"complete\"", afterKill);
    }

    [Fact]
    public async Task RefreshStatuses_NeverReportsAPlainCreatureNotBoundAsAnNpcAtAll()
    {
        var content = GameContent.Development with
        {
            Npcs = new Dictionary<uint, NpcDefinition>(),
        };
        var session = await SessionAsync(content);

        var response = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestRefreshStatuses, 5, new { }))));

        Assert.Contains("\"statuses\":[]", response);
    }

    [Fact]
    public async Task RefreshStatuses_IsEmptyWithNothingVisible()
    {
        var session = await SessionAsync(GameContent.Development, spawns: []);

        var response = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestRefreshStatuses, 5, new { }))));

        Assert.Contains("\"statuses\":[]", response);
    }

    private static async Task<PlaySession> SessionAsync(GameContent content, IReadOnlyList<CreatureSpawnDefinition>? spawns = null)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            spawns ?? Spawns, null, content, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
