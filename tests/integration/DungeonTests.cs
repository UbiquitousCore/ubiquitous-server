using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed partial class DungeonTests
{
    private const float GateApproachX = -2678f;
    private const float GateApproachZ = -585f;
    private const float GateInsideX = -2690f;
    private const float InsideFloorY = -1.9f;
    private const float InsideArrivalX = -682f;
    private const float InsideArrivalZ = -2098f;

    [Fact]
    public async Task WalkingIntoTheDungeonMouth_EntersAnInstancedCopy()
    {
        var (session, _) = await JoinAsync("dung1", "Ayla",
            [new CreatureSpawnDefinition(3001, 14, 9400, -2670, 39, -580, 0)]);

        var entered = await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);

        Assert.Contains(entered, m => m.Contains("\"type\":\"world.loading\"") && m.Contains("\"map\":14"));
        Assert.Contains(entered, m => m.Contains("\"type\":\"world.entered\"") && m.Contains("\"map\":14"));
        Assert.NotNull(session.CurrentInstanceKey);
        Assert.Contains(entered, m => m.Contains("\"type\":\"entity.spawned\"") && m.Contains("\"entry\":9400"));
    }

    [Fact]
    public async Task KillingBothPlacedBosses_CreatesTheLastOneAndDoesNotFinishTheRun()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [2668] = new(2668, "Ariel", 20, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1),
                [2666] = new(2666, "creature-2666", 20, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1),
                [2667] = new(2667, "Cosmo", 21, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1),
            },
        };
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("dung30", "Ayla",
            [
                new CreatureSpawnDefinition(3003, 14, 2668, -681, InsideFloorY, -2097, 0),
                new CreatureSpawnDefinition(3004, 14, 2666, -683, InsideFloorY, -2097, 0),
            ],
            content, instances);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        var key = session.CurrentInstanceKey!;

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        foreach (var target in new[] { "Creature:1", "Creature:2" })
        {
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AttackEngage, 290, new { targetId = target })));
            for (var i = 0; i < 6; i++)
            {
                now += TimeSpan.FromSeconds(2);
                log.AddRange(session.TickCombat(now));
            }
        }

        Assert.True(instances.HasReached(key, "boss.2668.down"));
        Assert.True(instances.HasReached(key, "boss.2666.down"));

        var summon = Assert.Single(content.DungeonSummons);
        var appeared = Assert.Single(
            log, m => m.Contains("\"type\":\"entity.spawned\"") && m.Contains("\"entry\":2667"));
        Assert.Contains($"\"x\":{Math.Truncate(summon.X)}", appeared);
        Assert.Contains($"\"z\":{Math.Truncate(summon.Z)}", appeared);

        var createdAt = log.FindIndex(m =>
            m.Contains("\"type\":\"entity.spawned\"") && m.Contains("\"entry\":2667"));
        var finishedAt = log.FindIndex(m => m.Contains("\"type\":\"dungeon.completed\""));
        Assert.True(finishedAt < 0 || finishedAt > createdAt,
            "the run finished before its last boss was ever created");
    }

    [Fact]
    public async Task ASummonedBoss_IsCreatedOnlyOnce()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [2668] = new(2668, "Ariel", 20, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1),
                [2666] = new(2666, "creature-2666", 20, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1),
                [2667] = new(2667, "Cosmo", 21, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1),
            },
        };
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("dung31", "Ayla",
            [
                new CreatureSpawnDefinition(3003, 14, 2668, -681, InsideFloorY, -2097, 0),
                new CreatureSpawnDefinition(3004, 14, 2666, -683, InsideFloorY, -2097, 0),
            ],
            content, instances);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        foreach (var target in new[] { "Creature:1", "Creature:2" })
        {
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AttackEngage, 290, new { targetId = target })));
            for (var i = 0; i < 6; i++)
            {
                now += TimeSpan.FromSeconds(2);
                log.AddRange(session.TickCombat(now));
            }
        }

        for (var i = 0; i < 20; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }

        Assert.Equal(1, log.Count(m =>
            m.Contains("\"type\":\"entity.spawned\"") && m.Contains("\"entry\":2667")));
    }

    [Fact]
    public async Task PartyMembersWalkingInTogether_LandInTheSameCopy()
    {
        var hub = new PlaySessionHub();
        var instances = new InstanceService();
        var spawns = new[] { new CreatureSpawnDefinition(3001, 14, 9400, -2670, 39, -580, 0) };
        var (ayla, _) = await JoinAsync(hub, instances, "dung2a", "Ayla", spawns, GateApproachX, GateApproachZ);
        var (borin, _) = await JoinAsync(hub, instances, "dung2b", "Borin", spawns, GateApproachX, GateApproachZ - 2);
        await SendAsync(hub, ayla, WebMessageTypes.PartyInvite, 5, new { networkId = "player:char-dung2b" });
        await SendAsync(hub, borin, WebMessageTypes.PartyAccept, 6, new { });

        var aylaEnter = await SendAsync(hub, ayla, WebMessageTypes.PlayerMove, 7,
            new PlayerMovePayload(GateInsideX, 2, GateApproachZ, 0, true, MovementState.Walking));
        var borinEnter = await SendAsync(hub, borin, WebMessageTypes.PlayerMove, 7,
            new PlayerMovePayload(GateInsideX, 2, GateApproachZ - 2, 0, true, MovementState.Walking));

        Assert.Equal(ayla.CurrentInstanceKey, borin.CurrentInstanceKey);
        Assert.Equal(CreatureNetworkId(aylaEnter), CreatureNetworkId(borinEnter));
    }

    [Fact]
    public async Task KillingTheWarden_CompletesTheRunExactlyOnce()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9401] = new(9401, "creature-9401", 14, 30, MinDamage: 1, MaxDamage: 2, Rank: CreatureRank.Elite),
            },
        };
        var (session, _) = await JoinAsync("dung3", "Ayla",
            [new CreatureSpawnDefinition(3003, 14, 9401, -681, InsideFloorY, -2097, 0)], content);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:1" })));

        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        var first = session.TickCombat(now);
        Assert.DoesNotContain(first, m => m.Contains("\"type\":\"dungeon.completed\""));
        var second = session.TickCombat(now + TimeSpan.FromSeconds(2));
        var third = session.TickCombat(now + TimeSpan.FromSeconds(4));
        var done = first.Concat(second).Concat(third).ToList();

        Assert.Contains(done, m => m.Contains("\"type\":\"dungeon.completed\"")
            && m.Contains("\"mapId\":14") && m.Contains("The Prison"));
        var later = session.TickCombat(now + TimeSpan.FromSeconds(6));
        Assert.DoesNotContain(later, m => m.Contains("\"type\":\"dungeon.completed\""));
    }

    [Fact]
    public async Task WalkingOutThroughTheExit_ReturnsToTheOpenWorld()
    {
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("dung4", "Ayla",
            [new CreatureSpawnDefinition(3001, 14, 9400, -2670, 39, -580, 0)], instances: instances);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        var key = session.CurrentInstanceKey!;
        Assert.True(instances.IsLive(key));

        var exited = await MoveAsync(session, 3, -684, InsideFloorY, -2090);

        Assert.Contains(exited, m => m.Contains("\"type\":\"world.entered\"") && m.Contains("\"map\":1"));
        Assert.Null(session.CurrentInstanceKey);
        Assert.True(instances.IsLive(key));
        Assert.False(instances.IsLive(key, DateTimeOffset.UtcNow + InstanceService.EmptyGrace));
    }

    [Fact]
    public async Task WalkingOutMidFight_LeavesNothingStillFightingOnTheWayBackIn()
    {
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("dung32", "Ayla",
            [new CreatureSpawnDefinition(3005, 14, 9500, -683, InsideFloorY, -2093, 0)],
            PackContent(), instances);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        var key = session.CurrentInstanceKey!;

        await MoveAsync(session, 3, -683, InsideFloorY, -2096);
        var opened = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 10, new { targetId = "Creature:1" })));
        var now = DateTimeOffset.UtcNow;
        var fighting = new List<string>(opened);
        for (var i = 0; i < 3; i++)
        {
            now += TimeSpan.FromSeconds(2);
            fighting.AddRange(session.TickCombat(now));
        }

        Assert.Contains(fighting, m => m.Contains("\"type\":\"attack.meleeStart\"")
            && m.Contains("\"attackerId\":\"Creature:1\""));

        var leaving = await MoveAsync(session, 20, -684, InsideFloorY, -2090);
        Assert.Contains(leaving, m => m.Contains("\"type\":\"attack.exitCombat\"")
            && m.Contains("player:char-dung32"));

        Assert.Null(session.CurrentInstanceKey);
        await MoveAsync(session, 21, GateApproachX, 39, GateApproachZ);
        await MoveAsync(session, 22, GateInsideX, 39, GateApproachZ);
        Assert.Equal(key, session.CurrentInstanceKey);

        var back = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            now += TimeSpan.FromSeconds(2);
            back.AddRange(session.TickCombat(now));
        }

        Assert.DoesNotContain(back, m => m.Contains("\"type\":\"attack.meleeStart\"")
            && m.Contains("\"attackerId\":\"Creature:1\""));

    }

    [Fact]
    public async Task WalkingInFromTheArrivalPoint_StaysInside()
    {
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("dung10", "Ayla",
            [new CreatureSpawnDefinition(3001, 14, 9400, -670, InsideFloorY, -2200, 0)], instances: instances);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        var key = session.CurrentInstanceKey!;

        var walked = new List<string>();
        walked.AddRange(await MoveAsync(session, 3, InsideArrivalX, InsideFloorY, -2105));
        walked.AddRange(await MoveAsync(session, 4, InsideArrivalX, InsideFloorY, -2112));
        walked.AddRange(await MoveAsync(session, 5, InsideArrivalX - 3, InsideFloorY, -2119));

        Assert.DoesNotContain(walked, m => m.Contains("\"type\":\"world.entered\""));
        Assert.Equal(key, session.CurrentInstanceKey);
    }

    [Fact]
    public async Task SteppingOutOfTheOpenWorldArrival_DoesNotWalkStraightBackIn()
    {
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("dung11", "Ayla",
            [new CreatureSpawnDefinition(3001, 14, 9400, -670, InsideFloorY, -2200, 0)], instances: instances);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await MoveAsync(session, 3, -684, InsideFloorY, -2090);
        Assert.Null(session.CurrentInstanceKey);

        var stepped = await MoveAsync(session, 4, -2670, 39, -578);

        Assert.DoesNotContain(stepped, m => m.Contains("\"type\":\"world.entered\""));
        Assert.Null(session.CurrentInstanceKey);
    }

    [Fact]
    public async Task AStaleMoveFromTheOldMap_IsRefusedEvenForAnAdminSession()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("dung12", "p", 1, isAdmin: true),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-dung12", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 1, GateApproachX, 2, GateApproachZ, 4, [100, 200])]),
            [new CreatureSpawnDefinition(3001, 14, 9400, -670, InsideFloorY, -2200, 0)],
            content: GameContent.Development, combatRollUnit: () => 1.0, instances: new InstanceService());
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("dung12", "p"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        await MoveAsync(session, 2, GateApproachX, 2, GateApproachZ);
        await MoveAsync(session, 3, GateInsideX, 2, GateApproachZ);
        Assert.NotNull(session.CurrentInstanceKey);

        var stale = await MoveAsync(session, 4, GateApproachX, 2, GateApproachZ);

        Assert.Contains(stale, m => m.Contains("\"type\":\"move.accepted\"")
            && m.Contains("-2098"));
        Assert.DoesNotContain(stale, m => m.Contains("\"type\":\"entity.moved\""));
    }

    [Fact]
    public async Task SteppingOutAndBackIn_ResumesTheSameRun()
    {
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("dung13", "Ayla",
            [new CreatureSpawnDefinition(3001, 14, 9400, -670, InsideFloorY, -2200, 0)], instances: instances);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        var key = session.CurrentInstanceKey!;

        await MoveAsync(session, 3, -684, InsideFloorY, -2090);
        Assert.Null(session.CurrentInstanceKey);

        await MoveAsync(session, 4, GateInsideX, 39, GateApproachZ);

        Assert.Equal(key, session.CurrentInstanceKey);
    }

    [Fact]
    public async Task DungeonReset_IsRefusedFromInsideAndEndsTheRunFromOutside()
    {
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("dung14", "Ayla",
            [new CreatureSpawnDefinition(3001, 14, 9400, -670, InsideFloorY, -2200, 0)], instances: instances);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        var key = session.CurrentInstanceKey!;

        var refused = await SendAsync(session, WebMessageTypes.DungeonRestart, 3);
        Assert.Contains(refused, m => m.Contains("dungeon_reset_refused"));
        Assert.True(instances.IsLive(key));

        await MoveAsync(session, 4, -684, InsideFloorY, -2090);
        var done = await SendAsync(session, WebMessageTypes.DungeonRestart, 5);

        Assert.Contains(done, m => m.Contains("\"type\":\"dungeon.restarted\"") && m.Contains("\"count\":1"));
        Assert.False(instances.IsLive(key));

        await MoveAsync(session, 6, GateInsideX, 39, GateApproachZ);
        Assert.True(instances.IsLive(session.CurrentInstanceKey!));
    }

    [Fact]
    public async Task WhatARunHasKilled_StaysKilledForTheNextArrival()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9500] = new(9500, "jailer", 3, 10, MinDamage: 1, MaxDamage: 2,
                    NoticeDistance: 1, RespawnTimeMinSeconds: 36000, RespawnTimeMaxSeconds: 36000),
            },
        };
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("dung16", "Ayla",
            [new CreatureSpawnDefinition(3003, 14, 9500, -681, InsideFloorY, -2097, 0)],
            content, instances);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:1" })));

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }
        Assert.Contains(log, m => m.Contains("\"type\":\"unit.died\"") && m.Contains("Creature:1"));

        await MoveAsync(session, 4, -684, InsideFloorY, -2090);
        var back = await MoveAsync(session, 5, GateInsideX, 39, GateApproachZ);

        Assert.DoesNotContain(back, m => m.Contains("\"type\":\"entity.spawned\"")
            && m.Contains("\"networkId\":\"Creature:1\"")
            && !m.Contains("\"dead\":true"));
    }

    [Fact]
    public async Task OneShottingAPackmate_StillPullsTheRestOfItsGroup()
    {
        var (session, _) = await JoinAsync("dung17", "Ayla",
            [new CreatureSpawnDefinition(3003, 14, 9500, -681, InsideFloorY, -2097, 0),
             new CreatureSpawnDefinition(3004, 14, 9500, -680.5f, InsideFloorY, -2097.5f, 0, LeaderSpawnId: 3003)],
            PackContent());
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);

        var opened = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:1" })));

        Assert.Contains(opened, m => m.Contains("\"type\":\"attack.meleeStart\"")
            && m.Contains("\"attackerId\":\"Creature:2\""));
    }

    [Fact]
    public async Task AGatedWay_RefusesTheCrossingUntilItsBossIsDown()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [2668] = new(2668, "Ariel", 20, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1),
            },
        };
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("dung18", "Ayla",
            [new CreatureSpawnDefinition(3003, 14, 2668, -681, InsideFloorY, -2097, 0)],
            content, instances);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        var key = session.CurrentInstanceKey!;

        await WalkAsync(session, 100, InsideArrivalX, InsideArrivalZ, -560, -2366);
        var refused = await MoveAsync(session, 200, -546, InsideFloorY, -2366);

        Assert.Contains(refused, m => m.Contains("barrier_closed"));
        Assert.Contains(refused, m => m.Contains("\"type\":\"move.accepted\""));
        Assert.False(instances.HasReached(key, "boss.2668.down"));

        await WalkAsync(session, 210, -560, -2366, -681, -2097);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 290, new { targetId = "Creature:1" })));
        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }

        Assert.Contains(log, m => m.Contains("\"type\":\"dungeon.barrierOpened\"")
            && m.Contains("prison.inner-hall") && m.Contains("\"announce\":true"));

        Assert.Contains(log, m => m.Contains("\"type\":\"dungeon.barrierOpened\"")
            && m.Contains("dungeon_door01"));
        Assert.True(instances.HasReached(key, "boss.2668.down"));

        await WalkAsync(session, 300, InsideArrivalX, InsideArrivalZ, -560, -2366);
        var allowed = await MoveAsync(session, 400, -546, InsideFloorY, -2366);
        Assert.DoesNotContain(allowed, m => m.Contains("barrier_closed"));
    }

    [Fact]
    public async Task StandingInsideAClosedWay_CanStillWalkOutOfIt()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("dung20", "p"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-dung20", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 14, -545, 11.6f, -2366, 4, [100, 200])]),
            [new CreatureSpawnDefinition(3001, 14, 9400, -670, InsideFloorY, -2200, 0)],
            content: GameContent.Development, combatRollUnit: () => 1.0, instances: new InstanceService());
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("dung20", "p"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var out1 = await MoveAsync(session, 2, -558, 11.6f, -2366);

        Assert.DoesNotContain(out1, m => m.Contains("barrier_closed"));
        Assert.Contains(out1, m => m.Contains("\"type\":\"entity.moved\""));

        var back = await MoveAsync(session, 3, -546, 11.6f, -2366);
        Assert.Contains(back, m => m.Contains("barrier_closed"));
    }

    [Fact]
    public async Task EnteringAWorld_SaysWhichMapObjectsToLeaveOut()
    {
        var hiddenHere = new StaticObjectSpawnDefinition(9001, 14, 288, -600, 1, -2200, 0);
        var hiddenElsewhere = new StaticObjectSpawnDefinition(9002, 1, 288, 10, 1, 10, 0);
        var content = GameContent.Development with
        {
            HiddenStaticObjects = [hiddenHere, hiddenElsewhere],
        };
        var (session, _) = await JoinAsync("dung21", "Ayla",
            [new CreatureSpawnDefinition(3001, 14, 9400, -670, InsideFloorY, -2200, 0)], content);

        var entered = await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);

        var message = Assert.Single(entered, m => m.Contains("\"type\":\"world.hiddenObjects\""));
        Assert.Contains("-600", message);
        Assert.DoesNotContain("\"x\":10", message);
    }

    [Fact]
    public async Task EnteringAWorldWithNothingHidden_StillSaysSo()
    {
        var (session, _) = await JoinAsync("dung22", "Ayla",
            [new CreatureSpawnDefinition(3001, 14, 9400, -670, InsideFloorY, -2200, 0)]);

        var entered = await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);

        Assert.Contains(entered, m => m.Contains("\"type\":\"world.hiddenObjects\"")
            && m.Contains("\"positions\":[]"));
    }

    [Fact]
    public async Task AWayAlreadyOpened_IsStatedWithoutAnnouncingItAgain()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [2668] = new(2668, "Ariel", 20, 10, MinDamage: 1, MaxDamage: 2, NoticeDistance: 1),
            },
        };
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("dung19", "Ayla",
            [new CreatureSpawnDefinition(3003, 14, 2668, -681, InsideFloorY, -2097, 0)],
            content, instances);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:1" })));
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 6; i++) { now += TimeSpan.FromSeconds(2); session.TickCombat(now); }

        await MoveAsync(session, 10, -684, InsideFloorY, -2090);
        var back = await MoveAsync(session, 11, GateInsideX, 39, GateApproachZ);

        Assert.Contains(back, m => m.Contains("\"type\":\"dungeon.barrierOpened\"")
            && m.Contains("prison.inner-hall") && m.Contains("\"announce\":false"));
    }

    [Fact]
    public async Task TickCombat_LeavesTheDeadTrashLyingInsideTheSameCopy()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9500] = new(9500, "creature-9500", 3, 30, MinDamage: 1, MaxDamage: 2,
                    RespawnTimeMinSeconds: 1, RespawnTimeMaxSeconds: 1),
            },
        };
        var (session, _) = await JoinAsync("dung5", "Ayla",
            [new CreatureSpawnDefinition(3003, 14, 9500, -681, InsideFloorY, -2097, 0)], content);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:1" })));

        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        var log = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }
        Assert.Contains(log, m => m.Contains("\"type\":\"unit.died\"") && m.Contains("Creature:1"));

        var later = session.TickCombat(now + TimeSpan.FromHours(1));
        Assert.DoesNotContain(later, m => m.Contains("\"type\":\"entity.removed\"")
            && m.Contains("\"networkId\":\"Creature:1\""));
        Assert.DoesNotContain(later, m => m.Contains("\"type\":\"entity.spawned\""));
        Assert.DoesNotContain(later, m => m.Contains("\"type\":\"attack.meleeStart\""));
    }

    [Fact]
    public async Task TickCombat_AFollowerHoldsThePlayerWhileItsLeaderIsStillFighting()
    {
        var (session, _) = await JoinAsync("dung6", "Ayla",
            [new CreatureSpawnDefinition(3003, 14, 9500, -681, InsideFloorY, -2097, 0),
             new CreatureSpawnDefinition(3004, 14, 9500, -680.5f, InsideFloorY, -2097.5f, 0, LeaderSpawnId: 3003)],
            PackContent());
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        var opened = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:2" })));
        Assert.Contains(opened, m => m.Contains("\"type\":\"attack.meleeStart\"")
            && m.Contains("\"attackerId\":\"Creature:1\""));
        session.TickCombat(DateTimeOffset.UtcNow);

        await MoveAsync(session, 4, InsideArrivalX, InsideFloorY, -2111);
        await MoveAsync(session, 5, InsideArrivalX, InsideFloorY, -2124);
        await MoveAsync(session, 6, InsideArrivalX, InsideFloorY, -2137);
        await MoveAsync(session, 7, InsideArrivalX, InsideFloorY, -2150);
        var now = DateTimeOffset.UtcNow;
        var tick = new List<string>();
        for (var i = 0; i < 14; i++)
        {
            now += TimeSpan.FromSeconds(2);
            tick.AddRange(session.TickCombat(now));
        }

        Assert.DoesNotContain(tick, m => m.Contains("\"type\":\"attack.exitCombat\""));
        Assert.Contains(tick, m => m.Contains("\"type\":\"attack.swingResult\"")
            && m.Contains("\"attackerId\":\"Creature:2\""));
    }

    [Fact]
    public async Task TickCombat_TheFightSurvivesWalkingIntoTheNextSection()
    {
        var (session, _) = await JoinAsync("dung15", "Ayla",
            [new CreatureSpawnDefinition(3003, 14, 9500, -681, InsideFloorY, -2097, 0)],
            PackContent());
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:1" })));

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        var sequence = 4;
        for (var z = -2105f; z >= -2180f; z -= 7f)
        {
            await MoveAsync(session, sequence++, InsideArrivalX, InsideFloorY, z);
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }

        Assert.DoesNotContain(log, m => m.Contains("\"type\":\"attack.exitCombat\""));
    }

    [Fact]
    public async Task TickCombat_TheWholePackStandsDownTogetherWhenTheFightIsLost()
    {
        var (session, _) = await JoinAsync("dung7", "Ayla",
            [new CreatureSpawnDefinition(3003, 14, 9500, -681, InsideFloorY, -2097, 0),
             new CreatureSpawnDefinition(3004, 14, 9500, -680.5f, InsideFloorY, -2097.5f, 0, LeaderSpawnId: 3003)],
            PackContent());
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        var opened = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:1" })));
        Assert.Contains(opened, m => m.Contains("\"type\":\"attack.meleeStart\"")
            && m.Contains("\"attackerId\":\"Creature:2\""));
        session.TickCombat(DateTimeOffset.UtcNow);

        await MoveAsync(session, 4, InsideArrivalX, InsideFloorY, -2111);
        await MoveAsync(session, 5, InsideArrivalX, InsideFloorY, -2124);
        await MoveAsync(session, 6, InsideArrivalX, InsideFloorY, -2137);
        await MoveAsync(session, 7, InsideArrivalX, InsideFloorY, -2150);
        var outOfReach = DateTimeOffset.UtcNow;
        var noticed = session.TickCombat(outOfReach);
        Assert.DoesNotContain(noticed, m => m.Contains("\"type\":\"attack.exitCombat\""));
        var tick = session.TickCombat(outOfReach + Ubiquitous.Scripts.DungeonInmateScript.OutrunTime + TimeSpan.FromSeconds(1));

        Assert.Contains(tick, m => m.Contains("\"type\":\"attack.exitCombat\"")
            && m.Contains("Creature:1"));
        Assert.Contains(tick, m => m.Contains("\"type\":\"attack.exitCombat\"")
            && m.Contains("Creature:2"));
    }

    [Fact]
    public async Task ReleasingInsideTheDungeon_StaysInTheSameCopy()
    {
        var instances = new InstanceService();
        var (session, _) = await JoinAsync("dung8", "Ayla",
            [new CreatureSpawnDefinition(3001, 14, 9400, -670, InsideFloorY, -2200, 0)], instances: instances);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        var key = session.CurrentInstanceKey!;

        var released = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.PlayerUnstuck, 3, new { })));

        Assert.Contains(released, m => m.Contains("\"type\":\"world.entered\"") && m.Contains("\"map\":14"));
        Assert.Equal(key, session.CurrentInstanceKey);
        Assert.True(instances.IsLive(key));
    }

    [Fact]
    public async Task LoggingInOnADungeonMap_OpensACopyAndCanWalkBackOut()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("dung9", "p"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-dung9", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 14, -682.7f, -1.9f, -2104.8f, 4, [100, 200])]),
            [new CreatureSpawnDefinition(3001, 14, 9400, -670, InsideFloorY, -2200, 0)],
            content: GameContent.Development, combatRollUnit: () => 1.0, instances: new InstanceService());
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("dung9", "p"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        Assert.NotNull(session.CurrentInstanceKey);

        var walkedOut = await MoveAsync(session, 2, -684, InsideFloorY, -2090);

        Assert.Contains(walkedOut, m => m.Contains("\"type\":\"world.entered\"") && m.Contains("\"map\":1"));
        Assert.Null(session.CurrentInstanceKey);
    }

    [Fact]
    public async Task TheLastBoss_CastsItsWayThroughTheFightAndBringsOutPuppets()
    {
        var content = BossContent();
        var (session, _) = await JoinAsync("dung40", "Ayla",
            [new CreatureSpawnDefinition(3006, 14, 2667, -681, InsideFloorY, -2097, 0)],
            content);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await MoveAsync(session, 3, InsideArrivalX, InsideFloorY, -2096);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 10, new { targetId = "Creature:1" })));

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        for (var i = 0; i < 40; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }

        Assert.Contains(log, m => m.Contains("\"type\":\"spell.released\"")
            && m.Contains("\"casterId\":\"Creature:1\""));

        Assert.Contains(log, m => m.Contains("\"type\":\"entity.spawned\"")
            && m.Contains("\"entry\":2697"));
    }

    [Fact]
    public async Task TheFirstBoss_AlternatesItsTwoAbilities()
    {
        var content = BossContent();
        var (session, _) = await JoinAsync("dung41", "Ayla",
            [new CreatureSpawnDefinition(3007, 14, 2666, -681, InsideFloorY, -2097, 0)],
            content);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await MoveAsync(session, 3, InsideArrivalX, InsideFloorY, -2096);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 10, new { targetId = "Creature:1" })));

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        for (var i = 0; i < 40; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }

        Assert.Contains(log, m => m.Contains("\"type\":\"spell.released\"") && m.Contains("\"spellId\":2674"));
        Assert.Contains(log, m => m.Contains("\"type\":\"spell.released\"") && m.Contains("\"spellId\":2675"));
        Assert.Contains(log, m => m.Contains("\"type\":\"spell.released\"") && m.Contains("\"spellId\":2683"));
    }

    [Fact]
    public async Task AnOrdinaryCreature_CastsNothing()
    {
        var (session, _) = await JoinAsync("dung42", "Ayla",
            [new CreatureSpawnDefinition(3008, 14, 9500, -681, InsideFloorY, -2097, 0)],
            PackContent());
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await MoveAsync(session, 3, InsideArrivalX, InsideFloorY, -2096);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 10, new { targetId = "Creature:1" })));

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }

        Assert.DoesNotContain(log, m => m.Contains("\"type\":\"spell.released\"")
            && m.Contains("\"casterId\":\"Creature:1\""));
    }

    [Fact]
    public async Task KillingThePlayer_SendsTheWholePackHomeAndNotJustTheKiller()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9500] = new(9500, "jailer", 3, 100000, MinDamage: 500, MaxDamage: 500,
                    NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
            },
        };
        var (session, _) = await JoinAsync("dung50", "Ayla",
            [
                new CreatureSpawnDefinition(3010, 14, 9500, -681, InsideFloorY, -2097, 0),
                new CreatureSpawnDefinition(3011, 14, 9500, -680.5f, InsideFloorY, -2097.5f, 0, LeaderSpawnId: 3010),
            ],
            content);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await MoveAsync(session, 3, InsideArrivalX, InsideFloorY, -2096);

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 10, new { targetId = "Creature:2" })));

        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }

        Assert.Contains(log, m => m.Contains("\"type\":\"unit.died\"")
            && m.Contains("player:char-dung50"));

        Assert.True(session.IsReturningHome("Creature:1"), "the leader should be going home");
        Assert.True(session.IsReturningHome("Creature:2"), "the follower should be going home");
    }

    [Fact]
    public async Task KillingThePlayer_LetsGoOfEveryBodyItHadPulled()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9500] = new(9500, "jailer", 3, 100000, MinDamage: 500, MaxDamage: 500,
                    NoticeDistance: 12, FactionType: UnitFactionTypes.Hostile),
            },
        };
        var (session, _) = await JoinAsync("dung51", "Ayla",
            [
                new CreatureSpawnDefinition(3020, 14, 9500, -681, InsideFloorY, -2097, 0),
                new CreatureSpawnDefinition(3021, 14, 9500, -683, InsideFloorY, -2098, 0),
            ],
            content);
        await MoveAsync(session, 2, GateInsideX, 2, GateApproachZ);
        await MoveAsync(session, 3, InsideArrivalX, InsideFloorY, -2096);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 10, new { targetId = "Creature:1" })));
        var now = DateTimeOffset.UtcNow;
        var log = new List<string>();
        log.AddRange(session.TickCombat(now += TimeSpan.FromSeconds(2)));
        log.AddRange(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 11, new { targetId = "Creature:2" }))));

        for (var i = 0; i < 20; i++)
        {
            now += TimeSpan.FromSeconds(2);
            log.AddRange(session.TickCombat(now));
        }

        Assert.Contains(log, m => m.Contains("\"type\":\"unit.died\"")
            && m.Contains("player:char-dung51"));
        Assert.True(session.IsReturningHome("Creature:1"), "the one left behind by the switch should be going home");
        Assert.True(session.IsReturningHome("Creature:2"), "and so should the one it died to");
    }

    private static GameContent BossContent() => GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [2632] = new(2632, "spell-2632", 2000, 0, 30f, EffectDamage: 1),
            [2633] = new(2633, "spell-2633", 2000, 0, 30f, EffectDamage: 1),
            [2634] = new(2634, "spell-2634", 0, 0, 30f, EffectDamage: 1),
            [2635] = new(2635, "spell-2635", 0, 0, 0f),
            [2636] = new(2636, "spell-2636", 0, 0, 0f, EffectHeal: 4000),
            [2637] = new(2637, "spell-2637", 0, 0, 0f),
            [2645] = new(2645, "spell-2645", 0, 0, 0f, EffectDamage: 100000),
            [2647] = new(2647, "spell-2647", 0, 0, 0f),
            [2652] = new(2652, "spell-2652", 0, 0, 30f, AuraStun: true),
            [2657] = new(2657, "spell-2657", 2000, 0, 0f, EffectDamage: 1),
            [2674] = new(2674, "spell-2674", 0, 0, 0f),
            [2675] = new(2675, "spell-2675", 0, 0, 0f),
            [2683] = new(2683, "spell-2683", 0, 0, 0f, EffectDamage: 1),
            [2796] = new(2796, "spell-2796", 0, 0, 0f),
        },
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [2666] = new(2666, "creature-2666", 20, 100000, MinDamage: 1, MaxDamage: 1,
                NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
            [2668] = new(2668, "creature-2668", 20, 4000, MinDamage: 1, MaxDamage: 1,
                NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
            [2667] = new(2667, "creature-2667", 21, 600, MinDamage: 1, MaxDamage: 1,
                NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
            [2697] = new(2697, "creature-2697", 19, 200, MinDamage: 1, MaxDamage: 1,
                NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
        },
    };

    private static GameContent PackContent() => GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9500] = new(9500, "jailer", 3, 100000, MinDamage: 1, MaxDamage: 1,
                NoticeDistance: 1, FactionType: UnitFactionTypes.Hostile),
        },
    };

    [Fact]
    public void ReleaseInsideTheDungeon_ReturnsToTheDungeonDoor()
    {
        var destination = GraveyardLookup.Resolve(GameContent.Development, 14, new Ubiquitous.Common.Position(-2640, 39, -560));

        Assert.Equal(14, destination.MapId);
    }

    private static string CreatureNetworkId(IEnumerable<string> responses)
    {
        var spawned = responses.Single(r => r.Contains("\"type\":\"entity.spawned\"") && r.Contains("\"entry\""));
        const string marker = "\"networkId\":\"";
        var start = spawned.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = spawned.IndexOf('"', start);
        return spawned[start..end];
    }

    private sealed record Inbox(List<string> Direct, List<string> Forwarded);

    private static async Task<(PlaySession Session, Inbox Inbox)> JoinAsync(
        string guid, string name, CreatureSpawnDefinition[] spawns,
        GameContent? content = null, InstanceService? instances = null)
    {
        var hub = new PlaySessionHub();
        return await JoinAsync(hub, instances ?? new InstanceService(), guid, name,
            spawns, GateApproachX, GateApproachZ, content);
    }

    private static async Task<(PlaySession Session, Inbox Inbox)> JoinAsync(
        PlaySessionHub hub, InstanceService instances, string guid, string name,
        CreatureSpawnDefinition[] spawns, float x, float z, GameContent? content = null)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([new CharacterRecord(
                $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 1, x, 2, z, 4, [100, 200])]),
            spawns, content: content ?? GameContent.Development,
            combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9, instances: instances, parties: TestSessions.PartiesOf(hub));
        var inbox = new Inbox([], []);
        hub.Register(session, message => { inbox.Forwarded.Add(message); return Task.CompletedTask; });

        await SendAsync(hub, session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
        await SendAsync(hub, session, WebMessageTypes.CharactersChoose, 1,
            new ChooseCharacterRequest(name));
        return (session, inbox);
    }

    private static async Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, int sequence) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, new { })));

    private static async Task<IReadOnlyList<string>> WalkAsync(
        PlaySession session, int sequence, float fromX, float fromZ, float toX, float toZ)
    {
        const float step = 12f;
        var last = Array.Empty<string>() as IReadOnlyList<string>;
        var dx = toX - fromX;
        var dz = toZ - fromZ;
        var distance = MathF.Sqrt((dx * dx) + (dz * dz));
        var steps = Math.Max(1, (int)MathF.Ceiling(distance / step));
        for (var i = 1; i <= steps; i++)
        {
            var t = (float)i / steps;
            last = await MoveAsync(session, sequence + i, fromX + (dx * t), InsideFloorY, fromZ + (dz * t));
        }
        return last;
    }

    private static async Task<IReadOnlyList<string>> MoveAsync(PlaySession session, int sequence, float x, float y, float z) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, sequence, new PlayerMovePayload(x, y, z, 0, true, MovementState.Walking))));

    private static async Task<IReadOnlyList<string>> SendAsync(
        PlaySessionHub hub, PlaySession session, string type, int sequence, object payload)
    {
        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
        var presence = await hub.BroadcastAsync(session, responses);
        return [.. responses, .. presence];
    }
}
