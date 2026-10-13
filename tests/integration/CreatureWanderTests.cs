using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class CreatureWanderTests
{
    [Fact]
    public async Task TickCombat_LeavesAFreshlySpawnedCreatureStandingThroughItsFirstPause()
    {
        var session = await SessionAsync(Wandering(1001, 1, 2, 33));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"entity.moved\""));
    }

    [Fact]
    public async Task TickCombat_StartsWanderingOncePauseElapses()
    {
        var session = await SessionAsync(Wandering(1001, 1, 2, 33));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var tick = session.TickCombat(now + CreatureWanderService.PauseDuration);

        Assert.Contains(tick, message =>
            message.Contains("\"type\":\"entity.moved\"") && message.Contains("\"target\""));
    }

    [Fact]
    public async Task TickCombat_NeverWandersASpawnWhoseOwnIdleBehaviourIsNotWalk()
    {
        var stationary = new CreatureSpawnDefinition(1001, 13, 9001, 1, 2, 33, 0, IdleBehaviour: 0, WanderRadius: 10);
        var session = await SessionAsync(stationary);
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var tick = session.TickCombat(now + CreatureWanderService.PauseDuration + CreatureWanderService.PauseDuration);

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"entity.moved\""));
    }

    [Fact]
    public async Task TickCombat_NeverRandomlyWandersASpawnThatCarriesItsOwnWaypointRoute()
    {
        var waypointDriven = new CreatureSpawnDefinition(1001, 13, 9001, 1, 2, 33, 0, IdleBehaviour: 1, PatrolRouteId: 77, WanderRadius: 10);
        var session = await SessionAsync(waypointDriven);
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var tick = session.TickCombat(now + CreatureWanderService.PauseDuration + CreatureWanderService.PauseDuration);

        var patrolXs = PatrolRoutes.Generate(waypointDriven).Stops
            .Select(stop => $"\"target\":{{\"x\":{((double)stop.X).ToString(System.Globalization.CultureInfo.InvariantCulture)},")
            .ToArray();
        Assert.All(
            tick.Where(message => message.Contains("\"type\":\"entity.moved\"") && message.Contains("\"target\":{")),
            message => Assert.Contains(patrolXs, message.Contains));
    }

    [Fact]
    public async Task TickCombat_NeverWandersACreatureThatIsCurrentlyTheCombatTarget()
    {
        var session = await SessionAsync(WithSightRange(5), Wandering(1001, 2, 2, 4));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var tick = session.TickCombat(now + CreatureWanderService.PauseDuration);

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"entity.moved\""));
    }

    [Fact]
    public async Task TickCombat_NeverWandersADeadCreature()
    {
        var session = await SessionAsync(Wandering(1001, 2, 2, 4));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        List<string> last = [];
        for (var swing = 0; swing < 9 && !last.Any(message => message.Contains("unit.died")); swing++)
        {
            last = session.TickCombat(now).ToList();
            now += TimeSpan.FromSeconds(2);
        }

        Assert.Contains(last, message => message.Contains("\"type\":\"unit.died\""));

        var afterDeath = session.TickCombat(now + CreatureWanderService.PauseDuration + TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(afterDeath, message => message.Contains("\"type\":\"entity.moved\""));
    }

    [Fact]
    public async Task TickCombat_ResumesWanderingAfterLeashingOutOfCombat()
    {
        var session = await SessionAsync(WithSightRange(5), Wandering(1001, 2, 2, 4));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var sequence = 3;
        for (var x = 15; x <= 60; x += 14)
        {
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.PlayerMove, sequence++, new PlayerMovePayload(x, 2, 3, 0, true, MovementState.Walking))));
        }

        var leashTick = session.TickCombat(now + TimeSpan.FromSeconds(2));
        Assert.Contains(leashTick, message => message.Contains("\"type\":\"attack.halted\""));

        for (var i = 3; i <= 40; i++) session.TickWorld(now + TimeSpan.FromSeconds(i));

        var settled = now + TimeSpan.FromSeconds(40);
        session.TickCombat(settled);
        var wanderTick = session.TickCombat(settled + CreatureWanderService.PauseDuration);

        Assert.Contains(wanderTick, message => message.Contains("\"type\":\"entity.moved\""));
    }

    [Fact]
    public async Task TickCombat_LegStartFacesItsDestination()
    {
        var session = await SessionAsync(Wandering(1001, 1, 2, 33));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var tick = session.TickCombat(now + CreatureWanderService.PauseDuration);
        var move = Assert.Single(tick, message =>
            message.Contains("\"type\":\"entity.moved\"") && message.Contains("\"target\""));
        using var document = JsonDocument.Parse(move);
        var payload = document.RootElement.GetProperty("payload");
        var yaw = payload.GetProperty("yaw").GetDouble();
        var target = payload.GetProperty("target");
        var dx = target.GetProperty("x").GetDouble() - 1;
        var dz = target.GetProperty("z").GetDouble() - 33;
        Assert.True(Math.Abs(dx) + Math.Abs(dz) > 0.1);
        Assert.Equal(Math.Atan2(dx, -dz), yaw, precision: 5);
    }

    [Fact]
    public async Task TickCombat_AGroupSeenTogetherDoesNotSetOffTogether_AndEachKeepsToItsRange()
    {
        var spawns = Enumerable.Range(0, 8)
            .Select(i => Wandering((uint)(1001 + i), 1 + (i * 3), 2, 33))
            .ToArray();
        var seeded = new Random(20260926);
        var session = await SessionAsync(GameContent.Development, () => seeded.NextDouble(), spawns);
        var start = DateTimeOffset.UtcNow;
        session.TickCombat(start);

        var firstSetOff = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var homes = spawns.ToDictionary(spawn => $"Creature:{spawn.Id - 1000}", spawn => spawn, StringComparer.Ordinal);
        for (var step = 1; step <= 300; step++)
        {
            var now = start + TimeSpan.FromMilliseconds(100 * step);
            foreach (var message in session.TickCombat(now))
            {
                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                if (root.GetProperty("type").GetString() != "entity.moved") continue;
                var payload = root.GetProperty("payload");
                var id = payload.GetProperty("networkId").GetString()!;
                if (!homes.TryGetValue(id, out var home)) continue;
                if (payload.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.Object)
                {
                    firstSetOff.TryAdd(id, now);
                    var dx = target.GetProperty("x").GetSingle() - home.X;
                    var dz = target.GetProperty("z").GetSingle() - home.Z;
                    Assert.True(Math.Sqrt((dx * dx) + (dz * dz)) <= home.WanderRadius + 0.001,
                        $"{id} walked to a point outside its own range");
                }
            }
        }

        Assert.Equal(spawns.Length, firstSetOff.Count);
        Assert.True(firstSetOff.Values.Distinct().Count() > spawns.Length / 2,
            "most of the group set off on the same tick");
        Assert.All(firstSetOff.Values, at => Assert.True(at - start <= CreatureWanderService.PauseDuration));
    }

    private static CreatureSpawnDefinition Wandering(uint id, float x, float y, float z) =>
        new(id, 13, 9001, x, y, z, 0, IdleBehaviour: 1, WanderRadius: 10);

    private static Task<PlaySession> SessionAsync(params CreatureSpawnDefinition[] spawns) =>
        SessionAsync(GameContent.Development, spawns);

    private static GameContent WithSightRange(int range) => GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9001] = new(9001, "creature-9001", 3, 100, NoticeDistance: range),
        },
    };

    private static Task<PlaySession> SessionAsync(GameContent content, params CreatureSpawnDefinition[] spawns) =>
        SessionAsync(content, () => 0.9, spawns);

    private static async Task<PlaySession> SessionAsync(
        GameContent content, Func<double> wanderRollUnit, params CreatureSpawnDefinition[] spawns)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            spawns, content: content, combatRollUnit: () => 1.0, wanderRollUnit: wanderRollUnit);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
