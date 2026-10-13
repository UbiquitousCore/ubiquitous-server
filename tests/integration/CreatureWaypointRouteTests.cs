using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class CreatureWaypointRouteTests
{
    private const uint RouteId = 77;

    private static readonly WaypointRouteDefinition Route = new(RouteId, 13,
    [
        new(6, 2, 33, 2000),
        new(1, 2, 33, 2000),
    ]);

    [Fact]
    public async Task TickCombat_SetsARouteFollowingCreatureWalkingWithoutAnOpeningPause()
    {
        var session = await SessionAsync(WithRoute(), WaypointDriven(1001, 1, 2, 33));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var tick = session.TickCombat(now);

        Assert.Contains(tick, message =>
            message.Contains("\"type\":\"entity.moved\"") && message.Contains("\"target\""));
    }

    [Fact]
    public async Task TickCombat_WalksToTheRoutesOwnFirstStop()
    {
        var session = await SessionAsync(WithRoute(), WaypointDriven(1001, 1, 2, 33));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var tick = session.TickCombat(now);

        Assert.Contains(tick, message =>
            message.Contains("\"type\":\"entity.moved\"") && message.Contains("\"x\":6"));
    }

    [Fact]
    public async Task TickCombat_WalksTheGeneratedPatrolForARouteWithNoPlacedStops()
    {
        var spawn = UnknownRouteSpawn(1001, 1, 2, 33);
        var patrol = PatrolRoutes.Generate(spawn);
        var session = await SessionAsync(WithRoute(), spawn);
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);
        session.TickCombat(now);

        var tick = session.TickCombat(now + TimeSpan.FromMilliseconds(patrol.Stops[0].PauseMilliseconds));

        var moved = Assert.Single(tick, message => message.Contains("\"type\":\"entity.moved\""));
        Assert.Contains($"\"target\":{{\"x\":{((double)patrol.Stops[1].X).ToString(System.Globalization.CultureInfo.InvariantCulture)}", moved);
    }

    [Fact]
    public async Task TickCombat_TakesPriorityOverWanderWhenASpawnCarriesBoth()
    {
        var both = new CreatureSpawnDefinition(1001, 13, 9001, 1, 2, 33, 0, IdleBehaviour: 1, PatrolRouteId: RouteId, WanderRadius: 4);
        var session = await SessionAsync(WithRoute(), both);
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var tick = session.TickCombat(now);

        var moves = tick.Count(message => message.Contains("\"type\":\"entity.moved\""));
        Assert.Equal(1, moves);
    }

    [Fact]
    public async Task TickCombat_NeverWalksACreatureThatIsCurrentlyTheCombatTarget()
    {
        var session = await SessionAsync(WithRoute(WithSightRange(5)), WaypointDriven(1001, 2, 2, 4));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var tick = session.TickCombat(now);

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"entity.moved\""));
    }

    private static CreatureSpawnDefinition WaypointDriven(uint id, float x, float y, float z) =>
        new(id, 13, 9001, x, y, z, 0, PatrolRouteId: RouteId, WanderRadius: 4);

    private static CreatureSpawnDefinition UnknownRouteSpawn(uint id, float x, float y, float z) =>
        new(id, 13, 9001, x, y, z, 0, PatrolRouteId: 999999, WanderRadius: 4);

    private static GameContent WithRoute(GameContent? content = null) => (content ?? GameContent.Development) with
    {
        WaypointRoutes = new Dictionary<uint, WaypointRouteDefinition> { [RouteId] = Route },
    };

    private static GameContent WithSightRange(int range) => GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9001] = new(9001, "creature-9001", 3, 100, NoticeDistance: range),
        },
    };

    private static async Task<PlaySession> SessionAsync(GameContent content, params CreatureSpawnDefinition[] spawns)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            spawns, content: content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
