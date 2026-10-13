using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class SpellFlightTests
{
    private const uint Bolt = 7501;
    private const uint FrostBolt = 7502;
    private const float PlayerStartX = 1;
    private const float BoltSpeed = 10f;
    private const float Distance = 10f;
    private static readonly TimeSpan Flight = TimeSpan.FromSeconds(Distance / BoltSpeed);

    private static readonly CharacterRecord Mage = new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);

    private static async Task<PlaySession> SessionAsync()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9001] = new(9001, "creature-9001", 3, 100, MinDamage: 4, MaxDamage: 4),
            },
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [Bolt] = new(Bolt, "spell-7501", 0, 0, 30f, PowerCost: 0, EffectDamage: 20, Speed: BoltSpeed),
                [FrostBolt] = new(FrostBolt, "spell-7502", 0, 0, 30f, PowerCost: 0, EffectDamage: 20,
                    AuraSlowPercent: 50, AuraChill: true, DurationMs: 5000, Speed: BoltSpeed),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, Bolt, FrostBolt],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Mage]), [new CreatureSpawnDefinition(1001, 13, 9001, PlayerStartX + Distance, 2, 3, 0)],
            content: content, spellRollUnit: () => 1.0, combatRollUnit: () => 1.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        return session;
    }

    private static Task<IReadOnlyList<string>> CastAsync(PlaySession session, uint spellId) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId, targetId = "Creature:1" })));

    private static bool Is(string message, string type) => message.Contains($"\"type\":\"{type}\"");

    private static bool CreatureEntersCombat(string message) =>
        Is(message, "attack.enterCombat") && message.Contains("\"networkId\":\"Creature:1\"");

    [Fact]
    public async Task ABoltLeavesAtOnceButTheCreatureFeelsNothingUntilItArrives()
    {
        var session = await SessionAsync();
        var now = DateTimeOffset.UtcNow;

        var cast = await CastAsync(session, Bolt);

        Assert.Single(cast, m => Is(m, "spell.released") && m.Contains("\"speed\":10"));
        Assert.DoesNotContain(cast, m => Is(m, "spell.outcome") || CreatureEntersCombat(m));
        var midway = session.TickCombat(now + Flight / 2);
        Assert.DoesNotContain(midway, m => Is(m, "spell.outcome") || CreatureEntersCombat(m));

        var arrival = session.TickCombat(now + Flight + TimeSpan.FromMilliseconds(100));

        Assert.Contains(arrival, m => Is(m, "spell.outcome") && m.Contains("\"damage\":20"));
        Assert.Contains(arrival, CreatureEntersCombat);
        Assert.DoesNotContain(arrival, m => Is(m, "spell.released"));
    }

    [Fact]
    public async Task AFrostBoltSlowsTheCreatureOnlyOnArrivalAndItChasesAtTheSlowedSpeed()
    {
        var session = await SessionAsync();
        var now = DateTimeOffset.UtcNow;

        var cast = await CastAsync(session, FrostBolt);
        Assert.DoesNotContain(cast, m => Is(m, "aura.applied"));

        var arrival = session.TickCombat(now + Flight + TimeSpan.FromMilliseconds(100));
        Assert.Contains(arrival, m => Is(m, "aura.applied") && m.Contains("\"unitId\":\"Creature:1\""));
        var chasing = session.TickCombat(now + Flight + TimeSpan.FromMilliseconds(600));

        var slowedSpeed = CreatureChaseService.Speed / 2;
        Assert.Contains(chasing.Concat(arrival), m => Is(m, "entity.moved")
            && m.Contains("\"networkId\":\"Creature:1\"") && m.Contains($"\"speed\":{slowedSpeed}"));
    }
}
