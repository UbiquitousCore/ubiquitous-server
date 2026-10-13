using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class CreatureChaseTests
{
    [Fact]
    public async Task TickCombat_ChasesTheCombatTargetTheSameTickItAggros()
    {
        var session = await SessionAsync(WithSightRange(15), new CreatureSpawnDefinition(1001, 13, 9001, 1, 2, 13, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.meleeStart\""));
        Assert.Contains(tick, message =>
            message.Contains("\"type\":\"entity.moved\"") && message.Contains("\"target\""));
    }

    [Fact]
    public async Task TickCombat_ChaseFacesThePlayer()
    {
        var session = await SessionAsync(WithSightRange(15), new CreatureSpawnDefinition(1001, 13, 9001, 1, 2, 13, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);
        var move = Assert.Single(tick, message =>
            message.Contains("\"type\":\"entity.moved\"") && message.Contains("\"target\""));
        using var document = System.Text.Json.JsonDocument.Parse(move);
        var yaw = document.RootElement.GetProperty("payload").GetProperty("yaw").GetDouble();
        Assert.Equal(0, yaw, precision: 9);
    }

    [Fact]
    public async Task TickCombat_ChasesEvenWhenIdleBehaviourSaysThisCreatureNeverWandersOnItsOwn()
    {
        var neverWanders = new CreatureSpawnDefinition(1001, 13, 9001, 1, 2, 13, 0, IdleBehaviour: 0);
        var session = await SessionAsync(WithSightRange(15), neverWanders);

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.Contains(tick, message =>
            message.Contains("\"type\":\"entity.moved\"") && message.Contains("\"target\""));
    }

    [Fact]
    public async Task TickCombat_ClosesTheDistanceThenStopsAndStaysStoppedOnceInMeleeRange()
    {
        var session = await SessionAsync(WithSightRange(15), new CreatureSpawnDefinition(1001, 13, 9001, 1, 2, 13, 0));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var sawStop = false;
        for (var i = 0; i < 20 && !sawStop; i++)
        {
            now += TimeSpan.FromMilliseconds(250);
            var tick = session.TickCombat(now);
            if (tick.Any(message => message.Contains("\"type\":\"entity.moved\"") && message.Contains("\"moving\":false")))
            {
                sawStop = true;
            }
        }

        Assert.True(sawStop, "expected the chase to eventually report arriving back within melee range");

        now += TimeSpan.FromMilliseconds(250);
        var afterStop = session.TickCombat(now);
        Assert.DoesNotContain(afterStop, message => message.Contains("\"type\":\"entity.moved\""));
    }

    [Fact]
    public async Task TickCombat_NeverLeashesWhileTheCreatureIsSuccessfullyKeepingPace()
    {
        var session = await SessionAsync(WithSightRange(15), new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var sequence = 3;
        for (var step = 0; step < 6; step++)
        {
            var x = 1 + (step + 1) * MovementRules.PlayerForwardSpeed * 0.25f;
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.PlayerMove, sequence++, new PlayerMovePayload(x, 2, 3, 0, true, MovementState.Walking))));
            now += TimeSpan.FromMilliseconds(250);
            session.TickCombat(now);
        }

        var tick = session.TickCombat(now + TimeSpan.FromMilliseconds(250));

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"attack.halted\""));
    }

    [Fact]
    public async Task TickCombat_NeverChasesATrainingDummyEvenWhenThePlayerAttacksIt()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9001] = new(9001, "creature-9001", 3, 100, RawExtraFlags: 1u << 7),
            },
        };
        var session = await SessionAsync(content, new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 3, new PlayerMovePayload(9, 2, 4, 0, true, MovementState.Walking))));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message =>
            message.Contains("\"type\":\"entity.moved\"") && message.Contains("\"target\""));
    }

    [Fact]
    public async Task SwitchingTargets_LeavesTheFirstCreatureChasingThePlayer()
    {
        var session = await SessionAsync(GameContent.Development,
            new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 3, 0),
            new CreatureSpawnDefinition(1002, 13, 9001, 0, 2, 3, 0));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var switched = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:2" })));

        Assert.DoesNotContain(switched, message => message.Contains("\"type\":\"attack.halted\""));
        Assert.Contains(switched, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"attackerId\":\"Creature:1\""));

        var now = DateTimeOffset.UtcNow;
        var sequence = 4;
        var firstFollowed = false;
        for (var step = 1; step <= 4; step++)
        {
            var z = 3 + step * MovementRules.PlayerForwardSpeed * 0.25f;
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.PlayerMove, sequence++, new PlayerMovePayload(1, 2, z, 0, true, MovementState.Walking))));
            now += TimeSpan.FromMilliseconds(250);
            firstFollowed |= session.TickCombat(now).Any(message =>
                message.Contains("\"type\":\"entity.moved\"") && message.Contains("\"networkId\":\"Creature:1\""));
        }

        Assert.True(firstFollowed, "expected the first creature to follow the player after the switch");
    }

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
