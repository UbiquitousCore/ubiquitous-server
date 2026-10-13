using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class StressTestTests
{
    private const uint DummyEntry = 9500;

    private static GameContent Content => GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [DummyEntry] = new(DummyEntry, "creature-dummy", 10, 500, RawExtraFlags: 128),
            // The real jackstraw row carries no flag bit; the script binding names it a dummy.
            [1226] = new(1226, "creature-jackstraw", 10, 500),
        },
    };

    private static readonly CreatureSpawnDefinition DummySpawn = new(1001, 13, DummyEntry, 10, 2, 10, 0);

    [Fact]
    public async Task AnAdministrator_SpawnsAsManyBotsAsAsked_AroundTheDummyAndEngagesIt()
    {
        var session = await SessionAsync(admin: true);
        session.TickCombat(DateTimeOffset.UtcNow);

        var spawned = await StressAsync(session, new { count = 3 });

        var bots = spawned.Where(message => message.Contains($"\"type\":\"{WebMessageTypes.EntitySpawned}\"")).ToList();
        Assert.Equal(3, bots.Count);
        // Player bodies: a name and a race each, cycling human, elf, orc.
        Assert.Contains("\"name\":\"Stressbot1\"", bots[0]);
        Assert.Contains("\"race\":0", bots[0]);
        Assert.Contains("\"race\":1", bots[1]);
        Assert.Contains("\"race\":2", bots[2]);
        Assert.All(bots, bot => Assert.Contains("player:stress-", bot));
        var starts = spawned.Where(message => message.Contains($"\"type\":\"{WebMessageTypes.AttackMeleeStart}\"")).ToList();
        Assert.Equal(3, starts.Count);
        Assert.All(starts, start => Assert.Contains("\"targetId\":\"Creature:1\"", start));
        // Each engage announces the bot and the dummy that is now fighting.
        var entered = spawned.Where(message => message.Contains($"\"type\":\"{WebMessageTypes.AttackEnterCombat}\"")).ToList();
        Assert.Equal(6, entered.Count);
        Assert.Equal(3, entered.Count(message => message.Contains("\"networkId\":\"Creature:1\"")));
    }

    [Fact]
    public async Task AnyoneElse_IsRefused_AndNothingSpawns()
    {
        var session = await SessionAsync(admin: false);
        session.TickCombat(DateTimeOffset.UtcNow);

        var refused = await StressAsync(session, new { count = 3 });

        Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.Forbidden));
        Assert.DoesNotContain(refused, message => message.Contains($"\"type\":\"{WebMessageTypes.EntitySpawned}\""));
    }

    [Fact]
    public async Task ACountOutsideOneToTwoHundred_IsRefused()
    {
        var session = await SessionAsync(admin: true);
        session.TickCombat(DateTimeOffset.UtcNow);

        foreach (var count in new[] { 0, 201 })
        {
            var refused = await StressAsync(session, new { count });
            Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.InvalidPayload));
        }
    }

    [Fact]
    public async Task Tick_BotsHoldFireOutOfReach_AndSwingOnceTheyWanderIn()
    {
        var session = await SessionAsync(admin: true);
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);
        await StressAsync(session, new { count = 3 });

        // Fresh bots ring the dummy outside melee reach, so the first tick stays quiet.
        var quiet = session.TickCombat(now + TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(quiet, message => message.Contains("\"type\":\"attack.swingResult\""));

        var swung = false;
        for (var i = 2; i <= 30 && !swung; i++)
        {
            var tick = session.TickCombat(now + TimeSpan.FromSeconds(i));
            swung = tick.Any(message => message.Contains("\"type\":\"attack.swingResult\"")
                && message.Contains("\"targetId\":\"Creature:1\""));
        }
        Assert.True(swung, "No bot swung at the dummy within 30 seconds.");
    }

    [Fact]
    public async Task ADummyBoundByNameCounts_WithoutTheExtraFlagsBit()
    {
        // Entry 1226 is bound to the dummy script by name in content; real table rows
        // carry no flag bit, so the finder must go by the script, not the bit.
        var session = await SessionAsync(admin: true,
            new CreatureSpawnDefinition(1001, 13, 1226, 10, 2, 10, 0));
        session.TickCombat(DateTimeOffset.UtcNow);

        var spawned = await StressAsync(session, new { count = 2 });

        Assert.Equal(2, spawned.Count(message => message.Contains($"\"type\":\"{WebMessageTypes.EntitySpawned}\"")));
    }

    [Fact]
    public async Task NoDummyInSight_IsRefused()
    {
        // Entry 9001 is an ordinary creature, not a training dummy.
        var session = await SessionAsync(admin: true, new CreatureSpawnDefinition(1001, 13, 9001, 10, 2, 10, 0));
        session.TickCombat(DateTimeOffset.UtcNow);
        var refused = await StressAsync(session, new { count = 3 });

        Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.InvalidTarget));
        // The refusal names the entries in sight, so a dummy missing its table flag shows up as a number.
        Assert.Contains(refused, message => message.Contains("entries 9001"));
    }

    [Fact]
    public async Task ARerun_ReplacesThePack()
    {
        var session = await SessionAsync(admin: true);
        session.TickCombat(DateTimeOffset.UtcNow);
        await StressAsync(session, new { count = 2 });

        var rerun = await StressAsync(session, new { count = 1 });

        Assert.Equal(2, rerun.Count(message => message.Contains($"\"type\":\"{WebMessageTypes.EntityRemoved}\"")));
        Assert.Single(rerun, message => message.Contains($"\"type\":\"{WebMessageTypes.EntitySpawned}\""));
    }

    [Fact]
    public async Task Tick_BotsWander()
    {
        var session = await SessionAsync(admin: true);
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);
        await StressAsync(session, new { count = 3 });

        // The first tick after the spawn only books the wander legs; the legs start on a later one.
        session.TickCombat(now + TimeSpan.FromSeconds(1));
        var tick = session.TickCombat(now + TimeSpan.FromSeconds(10));

        Assert.Contains(tick, message => message.Contains($"\"type\":\"{WebMessageTypes.EntityMoved}\"")
            && message.Contains("\"moving\":true"));
    }

    private static Task<PlaySession> SessionAsync(bool admin, params CreatureSpawnDefinition[] spawns) =>
        SessionAsync(admin, Content, spawns);

    private static async Task<PlaySession> SessionAsync(bool admin, GameContent content, params CreatureSpawnDefinition[] spawns)
    {
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret", isAdmin: admin),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            spawns.Length == 0 ? [DummySpawn] : spawns,
            content: content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.5);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static Task<IReadOnlyList<string>> StressAsync(PlaySession session, object payload) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.DevStressTest, 2, payload)));
}
