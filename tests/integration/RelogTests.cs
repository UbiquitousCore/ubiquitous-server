using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class RelogTests
{
    private static readonly CreatureSpawnDefinition[] Spawns =
        [new CreatureSpawnDefinition(7001, 13, 9500, 30, 2, 30, 0)];

    private static readonly GameContent Content = GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9500] = new(9500, "wolf", 3, 100000, MinDamage: 1, MaxDamage: 1,
                FactionType: UnitFactionTypes.Hostile),
        },
    };

    [Fact]
    public async Task ACreatureDraggedAcrossTheMap_SpawnsWhereItStandsForTheNextSession()
    {
        var maps = new SharedMapRegistry();
        var first = await JoinAsync(maps, "relog-a", "Ayla", "char-relog-a", 28, 30);
        await SendAsync(first, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        var sequence = 3;
        for (var x = 20f; x >= -10f; x -= 8f)
        {
            await MoveAsync(first, sequence++, x, 2, 30);
            now += TimeSpan.FromSeconds(2);
            first.TickCombat(now);
        }

        var (_, entered) = await JoinReportingAsync(maps, "relog-b", "Borin", "char-relog-b", 0, 30);
        var spawned = SpawnOf(entered, "Creature:1");

        Assert.NotNull(spawned);
        maps.For(13).TryGetPosition("Creature:1", out var standing);
        Assert.NotEqual(30f, standing.X);
        Assert.Contains($"\"position\":{{\"x\":{standing.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}", spawned);
    }

    [Fact]
    public async Task PullingThenLoggingOutAndWaiting_FindsTheMobsBackWhereTheyStarted()
    {
        var maps = new SharedMapRegistry();
        var first = await JoinAsync(maps, "relog-f", "Ayla", "char-relog-f", 28, 30);
        await SendAsync(first, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        var sequence = 3;
        for (var x = 20f; x >= -10f; x -= 8f)
        {
            await MoveAsync(first, sequence++, x, 2, 30);
            now += TimeSpan.FromSeconds(2);
            first.TickCombat(now);
        }
        var shared = maps.For(13);
        shared.TryGetPosition("Creature:1", out var dragged);
        Assert.True(dragged.X < 20, "the chase should have moved it");

        first.ReleaseSharedCombat();
        Assert.True(shared.IsReturning("Creature:1"));

        var clock = DateTimeOffset.UtcNow;
        for (var i = 1; i <= 60; i++) shared.TickWorld(clock + TimeSpan.FromSeconds(i));

        shared.TryGetPosition("Creature:1", out var settled);
        Assert.Equal(30, settled.X);
        Assert.Equal(30, settled.Z);
        Assert.False(shared.IsReturning("Creature:1"));
    }

    [Fact]
    public async Task LoggingOut_LeavesBystandersWhereTheyStand()
    {
        var maps = new SharedMapRegistry();
        var session = await JoinAsync(maps, "relog-g", "Ayla", "char-relog-g", 28, 30);
        var shared = maps.For(13);

        shared.Adopt("Creature:1", 7001, new Ubiquitous.Common.Position(30, 2, 30));
        shared.SetPosition("Creature:1", new Ubiquitous.Common.Position(80, 2, 80));

        session.ReleaseSharedCombat();

        Assert.False(shared.IsReturning("Creature:1"));
        for (var i = 1; i <= 60; i++) shared.TickWorld(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(i));

        shared.TryGetPosition("Creature:1", out var where);
        Assert.Equal(80, where.X);
        Assert.Equal(80, where.Z);
    }

    [Fact]
    public async Task LoggingOut_TakesThePlayerOffEveryHateListOnTheMap()
    {
        var maps = new SharedMapRegistry();
        var session = await JoinAsync(maps, "relog-e", "Ayla", "char-relog-e", 28, 30);
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });
        session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        var shared = maps.For(13);
        Assert.Contains("player:char-relog-e", shared.HostilityFor("Creature:1").Keys);

        session.ReleaseSharedCombat();

        Assert.DoesNotContain("player:char-relog-e", shared.HostilityFor("Creature:1").Keys);
        Assert.Null(shared.MobVictim("Creature:1"));
        Assert.DoesNotContain("player:char-relog-e", shared.Fighters("Creature:1"));
    }

    private static string? SpawnOf(IReadOnlyList<string> responses, string networkId) =>
        responses.FirstOrDefault(m => m.Contains("\"type\":\"entity.spawned\"")
            && m.Contains($"\"networkId\":\"{networkId}\""));

    private static async Task<PlaySession> JoinAsync(
        SharedMapRegistry maps, string user, string name, string guid, float x, float z)
    {
        var (session, _) = await JoinReportingAsync(maps, user, name, guid, x, z);
        return session;
    }

    private static async Task<(PlaySession Session, IReadOnlyList<string> Entered)> JoinReportingAsync(
        SharedMapRegistry maps, string user, string name, string guid, float x, float z)
    {
        var session = TestSessions.Create(
            new DevelopmentAccountStore(user, "p"),
            new InMemoryCharacterStore([new CharacterRecord(
                guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, x, 2, z, 4, [100, 200])]),
            Spawns, content: Content, combatRollUnit: () => 1.0, sharedMaps: maps);
        return (session, await EnterAsync(session, user, name));
    }

    private static async Task<IReadOnlyList<string>> EnterAsync(PlaySession session, string user, string name)
    {
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest(user, "p"))));
        return await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name))));
    }

    private static async Task<IReadOnlyList<string>> SendAsync(
        PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));

    private static async Task<IReadOnlyList<string>> MoveAsync(
        PlaySession session, int sequence, float x, float y, float z) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, sequence, new PlayerMovePayload(x, y, z, 0, true, MovementState.Walking))));
}
