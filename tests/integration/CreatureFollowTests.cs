using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class CreatureFollowTests
{
    private const uint RouteId = 77;
    private const uint LeaderSpawn = 1001;

    private static readonly WaypointRouteDefinition Route = new(RouteId, 13,
    [
        new(6, 2, 33, 2000),
        new(1, 2, 33, 2000),
    ]);

    [Fact]
    public async Task TickCombat_MovesAFollowerThatCarriesNoRouteOfItsOwn()
    {
        var session = await SessionAsync(WithRoute(), Leader(1, 2, 33), Follower(1002, 2, 2, 33));

        var movers = MoversAcross(session, ticks: 8);

        Assert.Equal(2, movers.Count);
    }

    [Fact]
    public async Task TickCombat_KeepsAFollowerTrackingItsLeaderAcrossTicks()
    {
        var session = await SessionAsync(WithRoute(), Leader(1, 2, 33), Follower(1002, 2, 2, 33));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var moves = 0;
        for (int tick = 1; tick <= 8; tick++)
        {
            moves += session.TickCombat(now + TimeSpan.FromSeconds(tick))
                .Count(message => message.Contains("\"type\":\"entity.moved\""));
        }

        Assert.True(moves >= 3, $"only {moves} movement messages across eight ticks");
    }

    [Fact]
    public async Task TickCombat_LeavesASpawnWithNoLeaderStandingStill()
    {
        var session = await SessionAsync(WithRoute(), Leader(1, 2, 33), Unattached(1002, 2, 2, 33));

        var movers = MoversAcross(session, ticks: 8);

        Assert.Single(movers);
    }

    [Fact]
    public async Task TickCombat_LeavesAFollowerOfARoutelessLeaderStandingStill()
    {
        var routeless = new CreatureSpawnDefinition(LeaderSpawn, 13, 9001, 1, 2, 33, 0);
        var session = await SessionAsync(WithRoute(), routeless, Follower(1002, 2, 2, 33));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var tick = session.TickCombat(now + TimeSpan.FromSeconds(1));

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"entity.moved\""));
    }

    [Fact]
    public async Task TickCombat_NeverAlsoWandersAFollowerWhoseOwnIdleBehaviourSaysWalk()
    {
        var wandering = new CreatureSpawnDefinition(
            1002, 13, 9001, 2, 2, 33, 0, IdleBehaviour: 1, WanderRadius: 10, LeaderSpawnId: LeaderSpawn);
        var session = await SessionAsync(WithRoute(), Leader(1, 2, 33), wandering);
        var now = DateTimeOffset.UtcNow;

        for (int tick = 0; tick <= 8; tick++)
        {
            var messages = session.TickCombat(now + TimeSpan.FromSeconds(tick));
            var moved = MoverIdsIn(messages);
            Assert.Equal(moved.Count, moved.Distinct().Count());
        }
    }

    [Fact]
    public async Task TickCombat_NeverDragsAFollowerThatIsItselfTheCurrentFight()
    {
        var session = await SessionAsync(
            WithRoute(WithSightRange(5)), Leader(1, 2, 33), Follower(1002, 2, 2, 4));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        for (int tick = 1; tick <= 8; tick++)
        {
            var moved = MoverIdsIn(session.TickCombat(now + TimeSpan.FromSeconds(tick)));
            Assert.Equal(moved.Count, moved.Distinct().Count());
        }
    }

    private static HashSet<string> MoversAcross(PlaySession session, int ticks)
    {
        var now = DateTimeOffset.UtcNow;
        var movers = new HashSet<string>(StringComparer.Ordinal);
        for (int tick = 0; tick <= ticks; tick++)
        {
            movers.UnionWith(MoverIdsIn(session.TickCombat(now + TimeSpan.FromSeconds(tick))));
        }

        return movers;
    }

    private static List<string> MoverIdsIn(IReadOnlyList<string> messages)
    {
        const string marker = "\"networkId\":";
        var ids = new List<string>();
        foreach (var message in messages)
        {
            if (!message.Contains("\"type\":\"entity.moved\"")) continue;
            int at = message.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) continue;
            int start = at + marker.Length;
            int end = start;
            while (end < message.Length && message[end] != ',' && message[end] != '}') end++;
            ids.Add(message[start..end]);
        }

        return ids;
    }

    private static CreatureSpawnDefinition Leader(float x, float y, float z) =>
        new(LeaderSpawn, 13, 9001, x, y, z, 0, PatrolRouteId: RouteId, WanderRadius: 4);

    private static CreatureSpawnDefinition Follower(uint id, float x, float y, float z) =>
        new(id, 13, 9001, x, y, z, 0, LeaderSpawnId: LeaderSpawn);

    private static CreatureSpawnDefinition Unattached(uint id, float x, float y, float z) =>
        new(id, 13, 9001, x, y, z, 0);

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
