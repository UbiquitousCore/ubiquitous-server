using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class CreatureAggroTests
{
    [Fact]
    public async Task TickCombat_AggrosAVisibleCreatureWithinRangeWithNoAttackStartAtAll()
    {
        var session = await SessionAsync(WithSightRange(5), new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.meleeStart\""));
        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.enterCombat\""));
    }

    [Fact]
    public async Task TickCombat_ACreatureThatNoticesThePlayerIsTheOneThatAttacks()
    {
        var session = await SessionAsync(WithSightRange(5), new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        var start = Assert.Single(tick, message => message.Contains("\"type\":\"attack.meleeStart\""));
        Assert.Contains("\"attackerId\":\"Creature:1\"", start);
    }

    [Fact]
    public async Task TickCombat_ThePlayerOnlySwingsBackOnceTheCreatureHasStruck()
    {
        var session = await SessionAsync(WithSightRange(5), new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        var now = DateTimeOffset.UtcNow;
        var swings = session.TickCombat(now).ToList();
        for (var i = 0; i < 6; i++)
        {
            now += TimeSpan.FromSeconds(1);
            swings.AddRange(session.TickCombat(now));
        }

        var results = swings.Where(message => message.Contains("\"type\":\"attack.swingResult\"")).ToList();
        Assert.Contains("\"attackerId\":\"Creature:1\"", results.First());
        Assert.Contains(results, message => !message.Contains("\"attackerId\":\"Creature:1\""));
    }

    [Fact]
    public async Task TickCombat_AttackingTheCreatureThatNoticedThePlayerSwingsAtOnce()
    {
        var session = await SessionAsync(WithSightRange(5), new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var engaged = await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });
        var tick = session.TickCombat(now + TimeSpan.FromMilliseconds(100));

        Assert.Contains(engaged, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"targetId\":\"Creature:1\""));
        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.swingResult\"")
            && message.Contains("\"targetId\":\"Creature:1\""));
    }

    [Fact]
    public async Task TickCombat_LeavesACreatureBeyondItsOwnSightRangeAlone()
    {
        var session = await SessionAsync(WithSightRange(5), new CreatureSpawnDefinition(1001, 13, 9001, 30, 2, 3, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"attack.meleeStart\""));
    }

    [Fact]
    public async Task TickCombat_NeverAggrosAServiceNpcWithNoSightRangeAtAll()
    {
        var session = await SessionAsync(new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"attack.meleeStart\""));
    }

    [Fact]
    public async Task TickCombat_AggrosAHostileMonsterWithNoSightRangeAtAll()
    {
        var session = await SessionAsync(ContentWithCreature(
        new CreatureDefinition(9500, "wolf", 3, 100, FactionType: UnitFactionTypes.Hostile)),
            new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.Contains(tick, message => message.Contains("type\":\"attack.meleeStart"));
    }

    [Fact]
    public async Task TickCombat_LeavesAHostileMonsterBeyondTheFallbackRangeAlone()
    {
        var session = await SessionAsync(ContentWithCreature(
        new CreatureDefinition(9500, "wolf", 3, 100, FactionType: UnitFactionTypes.Hostile)),
            new CreatureSpawnDefinition(1001, 13, 9500, 30, 2, 3, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message => message.Contains("type\":\"attack.meleeStart\""));
    }

    [Fact]
    public async Task TickCombat_LeavesAHostileMonsterJustOutsideTheFallbackRangeAlone()
    {
        var session = await SessionAsync(ContentWithCreature(
        new CreatureDefinition(9500, "wolf", 3, 100, FactionType: UnitFactionTypes.Hostile)),
            new CreatureSpawnDefinition(1001, 13, 9500, 13, 2, 3, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message => message.Contains("type\":\"attack.meleeStart\""));
    }

    [Fact]
    public async Task TickCombat_NeverAggrosANeutralMonsterWithNoSightRangeAtAll()
    {
        var session = await SessionAsync(ContentWithCreature(
        new CreatureDefinition(9500, "critter", 1, 10, FactionType: UnitFactionTypes.Neutral)),
            new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message => message.Contains("type\":\"attack.meleeStart\""));
    }

    [Fact]
    public async Task TickCombat_AggrosEveryCreatureInRangeWhenThePlayerIsIdle()
    {
        var session = await SessionAsync(WithSightRange(5),
            new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9001, 2.5f, 2, 4, 0));

        var first = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.Equal(2, first.Count(message => message.Contains("\"type\":\"attack.meleeStart\"")));
    }

    [Fact]
    public async Task TickCombat_LeavesADistantStrangerAloneWhileFighting()
    {
        var session = await SessionAsync(WithSightRange(5),
            new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9001, 30, 2, 3, 0));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var tick = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("Creature:2"));
    }

    [Fact]
    public async Task TickCombat_LeavesADeadCreatureAloneEvenWithinAggroRange()
    {
        var session = await SessionAsync(new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
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

        var afterDeath = session.TickCombat(now + TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(afterDeath, message => message.Contains("\"type\":\"attack.meleeStart\""));
    }

    [Fact]
    public async Task TickCombat_LeashesOutOfAFightOnceThePlayerStraysFarEnough()
    {
        var session = await SessionAsync(WithSightRange(5), new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        session.TickCombat(DateTimeOffset.UtcNow);

        var sequence = 3;
        for (var x = 15; x <= 60; x += 14)
        {
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.PlayerMove, sequence++, new PlayerMovePayload(x, 2, 3, 0, true, MovementState.Walking))));
        }

        var tick = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.halted\""));
        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.exitCombat\""));
        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"attack.swingResult\""));
    }

    [Fact]
    public async Task TickCombat_LeashesAFightPulledBeyondLeashRangeFromHome()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret", 1, isAdmin: true),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 0, 2, 0, 0)],
            content: GameContent.Development, combatRollUnit: () => 1.0, sharedMaps: maps);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        await SendAsync(session, WebMessageTypes.PlayerMove, 2, new PlayerMovePayload(2, 2, 0, 0, true, MovementState.Walking));
        await SendAsync(session, WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:1" });
        var state = maps.For(13);
        state.SetPosition("Creature:1", new Ubiquitous.Common.Position(42, 2, 0));
        await SendAsync(session, WebMessageTypes.PlayerMove, 4, new PlayerMovePayload(45, 2, 0, 0, true, MovementState.Walking));

        var now = DateTimeOffset.UtcNow;
        var tick = session.TickCombat(now);
        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.halted\""));

        session.TickWorld(now + TimeSpan.FromSeconds(1));
        var walking = session.TickCombat(now + TimeSpan.FromSeconds(1));

        var leg = Assert.Single(walking, message => message.Contains("\"type\":\"entity.moved\"")
            && message.Contains("\"moving\":true"));
        Assert.Contains("\"target\":{\"x\":0", leg);
    }

    [Fact]
    public async Task TickCombat_LeashesAJoinerDrawnBeyondLeashRangeWhileTheTargetStaysHome()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret", 1, isAdmin: true),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0), new CreatureSpawnDefinition(1002, 13, 9001, 3, 2, 4, 0)],
            content: WithSightRange(5), combatRollUnit: () => 1.0, sharedMaps: maps);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);
        maps.For(13).SetPosition("Creature:2", new Ubiquitous.Common.Position(60, 2, 4));

        var tick = session.TickCombat(now + TimeSpan.FromSeconds(1));

        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.halted\"")
            && message.Contains("Creature:2"));
    }

    [Fact]
    public async Task TickCombat_DoesNotLeashAFightThatStaysWellWithinRange()
    {
        var session = await SessionAsync(WithSightRange(5), new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        session.TickCombat(DateTimeOffset.UtcNow);

        var tick = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"attack.halted\""));
        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.swingResult\""));
    }

    [Fact]
    public async Task TickCombat_NeverAggrosATownsfolkCreature()
    {
        var session = await SessionAsync(
            ContentWithCreature(new CreatureDefinition(
                9001, "creature-9001", 3, 100, FactionType: UnitFactionTypes.Townsfolk)),
            new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"attack.meleeStart\""));
    }

    [Fact]
    public async Task TickCombat_NeverAggrosANeutralCreature()
    {
        var session = await SessionAsync(
            ContentWithCreature(new CreatureDefinition(
                9001, "creature-9001", 3, 100, FactionType: UnitFactionTypes.Neutral)),
            new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"attack.meleeStart\""));
    }

    [Fact]
    public async Task TickCombat_StillAggrosAHostileCreatureCarryingAServiceFlag()
    {
        var session = await SessionAsync(
            ContentWithCreature(new CreatureDefinition(
                9001, "creature-9001", 3, 100, NoticeDistance: 5,
                RawNpcFlags: (uint)NpcFlags.Vendor, FactionType: UnitFactionTypes.Hostile)),
            new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.meleeStart\""));
    }

    [Fact]
    public async Task TickCombat_StillAggrosACreatureCarryingNoFactionAtAll()
    {
        var session = await SessionAsync(WithSightRange(5), new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.meleeStart\""));
    }

    internal static GameContent ContentWithCreature(CreatureDefinition creature) =>
        GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [creature.EntryId] = creature,
            },
        };

    private static GameContent WithSightRange(int range) => GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9001] = new(9001, "creature-9001", 3, 100, NoticeDistance: range),
        },
    };

    [Fact]
    public async Task TickCombat_NeverAggrosATrainingDummy()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9001] = new(9001, "creature-9001", 3, 100, RawExtraFlags: 1u << 7),
            },
        };
        var session = await SessionAsync(content, new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"attack.meleeStart\""));
    }

    [Fact]
    public async Task TickCombat_NeverAggrosACreatureBoundToTheDummyScript()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [1226] = new(1226, "test-dummy", 1, 99999, RawExtraFlags: 1u << 15),
            },
        };
        var session = await SessionAsync(content, new CreatureSpawnDefinition(1001, 13, 1226, 2, 2, 4, 0));

        var tick = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"attack.meleeStart\""));
    }

    [Fact]
    public async Task TickCombat_ExitsATrainingDummyFightAfterItsExitDelayOutOfMeleeRangeNotAtTheSynthetic40UnitLeash()
    {
        var content = TrainingDummyContent();
        var session = await SessionAsync(content, new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 3, new PlayerMovePayload(15, 2, 3, 0, true, MovementState.Walking))));

        var now = DateTimeOffset.UtcNow;
        var firstOutOfRangeTick = session.TickCombat(now);
        Assert.DoesNotContain(firstOutOfRangeTick, message => message.Contains("\"type\":\"attack.halted\""));

        var delay = Ubiquitous.Scripts.TrainingDummyScript.CombatExitDelay;
        var stillWithin = session.TickCombat(now + delay - TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(stillWithin, message => message.Contains("\"type\":\"attack.halted\""));

        var afterDelay = session.TickCombat(now + delay);
        Assert.Contains(afterDelay, message => message.Contains("\"type\":\"attack.halted\""));
        Assert.Contains(afterDelay, message => message.Contains("\"type\":\"attack.exitCombat\""));
    }

    [Fact]
    public async Task TickCombat_TrainingDummySixSecondWindowResetsOnReturningToMeleeRange()
    {
        var content = TrainingDummyContent();
        var session = await SessionAsync(content, new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var sequence = 3;
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, sequence++, new PlayerMovePayload(15, 2, 3, 0, true, MovementState.Walking))));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);
        session.TickCombat(now + TimeSpan.FromSeconds(4));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, sequence++, new PlayerMovePayload(2, 2, 3, 0, true, MovementState.Walking))));
        now += TimeSpan.FromSeconds(4);
        session.TickCombat(now);

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, sequence++, new PlayerMovePayload(15, 2, 3, 0, true, MovementState.Walking))));
        now += TimeSpan.FromSeconds(5);
        var tick = session.TickCombat(now);

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"attack.halted\""));
    }

    [Fact]
    public async Task TickCombat_HittingOneOfAGroupPullsItsPackmateIn()
    {
        var session = await SessionAsync(PackContent(),
            new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9501, 14, 2, 4, 0, LeaderSpawnId: 1001));
        var opened = await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        Assert.Contains(opened, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("Creature:2"));
    }

    [Fact]
    public async Task TickCombat_HittingOneGroupLeavesTheNextGroupStanding()
    {
        var session = await SessionAsync(PackContent(),
            new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9501, 14, 2, 4, 0));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var tick = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("Creature:2"));
    }

    [Fact]
    public async Task TickCombat_APackmateKeepsSwingingWhileTheFightRuns()
    {
        var session = await SessionAsync(PackContent(),
            new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9501, 14, 2, 4, 0, LeaderSpawnId: 1001));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        var swings = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            now += TimeSpan.FromSeconds(2);
            swings.AddRange(session.TickCombat(now));
        }

        Assert.Contains(swings, message => message.Contains("\"type\":\"attack.swingResult\"")
            && message.Contains("\"attackerId\":\"Creature:2\""));
    }

    [Fact]
    public async Task TickCombat_ACreatureNoticesAPlayerWhoIsAlreadyFighting()
    {
        var session = await SessionAsync(PackContent(),
            new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9501, 3, 2, 5, 0));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var tick = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        Assert.Contains(tick, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"attackerId\":\"Creature:2\""));
    }

    private static GameContent PackContent() => GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9500] = new(9500, "wolf", 3, 100, FactionType: UnitFactionTypes.Hostile),
            [9501] = new(9501, "wolf", 3, 100, FactionType: UnitFactionTypes.Hostile),
        },
    };

    private static GameContent TrainingDummyContent() => GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9001] = new(9001, "creature-9001", 3, 100, RawExtraFlags: 1u << 7),
        },
    };

    private static Task<PlaySession> SessionAsync(params CreatureSpawnDefinition[] spawns) =>
        SessionAsync(GameContent.Development, spawns);

    private static async Task<PlaySession> SessionAsync(GameContent content, params CreatureSpawnDefinition[] spawns)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            spawns, content: content, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static async Task<IReadOnlyList<string>> SendAsync(
        PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
}
