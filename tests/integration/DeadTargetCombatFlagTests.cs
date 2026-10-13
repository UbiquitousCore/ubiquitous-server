using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class DeadTargetCombatFlagTests
{
    private const uint ShadowStrike = 5815;

    private static readonly IReadOnlyList<int> DualWielding =
        [.. EquipmentSlots.Build((EquipmentSlots.MainHand, 3907), (EquipmentSlots.OffHand, 3907))];

    [Fact]
    public async Task TickCombat_TakesTheKilledCreatureOutOfCombatAlongsideItsDeath()
    {
        var session = await SessionAsync(Creature(maxHealth: 4));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var kill = KillByMelee(session);

        Assert.Contains(kill, message => message.Contains("\"type\":\"unit.died\"")
            && message.Contains("\"networkId\":\"Creature:1\""));
        Assert.Contains(kill, message => message.Contains("\"type\":\"attack.exitCombat\"")
            && message.Contains("\"networkId\":\"Creature:1\""));
    }

    [Fact]
    public async Task TickCombat_StillTakesTheAttackerOutOfCombatToo()
    {
        var session = await SessionAsync(Creature(maxHealth: 4));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var kill = KillByMelee(session);

        Assert.Contains(kill, message => message.Contains("\"type\":\"attack.exitCombat\"")
            && message.Contains("\"networkId\":\"player:char-a\""));
    }

    [Fact]
    public async Task Cast_TakesACreatureKilledByASpellOutOfCombatToo()
    {
        var session = await SessionAsync(Creature(maxHealth: 1));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.SpellRequest, 5,
                new { spellId = ShadowStrike, targetId = "Creature:1" })));

        Assert.Contains(responses, message => message.Contains("\"type\":\"unit.died\""));
        Assert.Contains(responses, message => message.Contains("\"type\":\"attack.exitCombat\"")
            && message.Contains("\"networkId\":\"Creature:1\""));
    }

    [Fact]
    public async Task TickCombat_SendsTheDeathBeforeTheVictimLeavesTheFight()
    {
        var session = await SessionAsync(Creature(maxHealth: 4));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var kill = KillByMelee(session);

        var died = IndexOf(kill, "\"type\":\"unit.died\"");
        var left = kill
            .Select((message, index) => (message, index))
            .Where(entry => entry.message.Contains("\"type\":\"attack.exitCombat\"")
                && entry.message.Contains("\"networkId\":\"Creature:1\""))
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();
        Assert.True(died >= 0 && left > died, $"unit.died at {died}, victim exitCombat at {left}");
    }

    private static IReadOnlyList<string> KillByMelee(PlaySession session)
    {
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        for (var swing = 0; swing < 12; swing++)
        {
            var tick = session.TickCombat(now).ToList();
            if (tick.Any(message => message.Contains("\"type\":\"unit.died\""))) return tick;
            now += TimeSpan.FromSeconds(2);
        }

        return [];
    }

    private static int IndexOf(IReadOnlyList<string> responses, string marker)
    {
        for (var index = 0; index < responses.Count; index++)
            if (responses[index].Contains(marker)) return index;
        return -1;
    }

    private static GameContent Creature(int maxHealth) => GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9001] = new(9001, "creature-9001", 3, maxHealth, MinDamage: 1, MaxDamage: 1),
        },
    };

    private static async Task<PlaySession> SessionAsync(GameContent content)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, PlayerClassIds.Ranger, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, DualWielding)]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 3, 0)], null, content,
            spellRollUnit: () => 1.0, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
