using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PlayerDeathEventTests
{
    private static readonly GameContent LethalContent = GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9001] = new(9001, "creature-9001", 3, 1_000_000,
                MinDamage: 1_000_000, MaxDamage: 1_000_000, LeashRange: 2f),
        },
    };

    [Fact]
    public async Task TheHerosDeathIsAnnouncedExactlyOnceHoweverLongTheFightIsPumped()
    {
        var log = await DeathLogAsync(level: 25, ticks: 200);

        Assert.Single(log, m => m.Contains("\"type\":\"unit.died\"") && m.Contains("player:char-a"));
        Assert.Single(log, m => m.Contains("\"type\":\"player.death\""));
    }

    [Fact]
    public async Task NothingMovesTheHerosHealthOnceTheHeroIsDown()
    {
        var log = await DeathLogAsync(level: 25, ticks: 200);

        var dead = log.FindIndex(m => m.Contains("\"type\":\"unit.died\"") && m.Contains("player:char-a"));
        Assert.InRange(dead, 0, log.Count - 1);
        Assert.DoesNotContain(log.Skip(dead), m =>
            m.Contains("\"type\":\"player.updated\"") && m.Contains("\"health\":"));
    }

    [Fact]
    public async Task TheGoldPenaltyStillArrivesAfterTheDeathItPaysFor()
    {
        var log = await DeathLogAsync(level: 25, ticks: 20);

        var dead = log.FindIndex(m => m.Contains("\"type\":\"unit.died\"") && m.Contains("player:char-a"));
        var gold = log.FindIndex(m => m.Contains("\"type\":\"player.updated\"") && m.Contains("\"gold\":"));
        Assert.InRange(dead, 0, log.Count - 1);
        Assert.InRange(gold, dead + 1, log.Count - 1);
    }

    private static async Task<List<string>> DeathLogAsync(int level, int ticks)
    {
        var characters = new InMemoryCharacterStore([Character("char-a", "Ayla", level)]);
        var session = TestSessions.Create(Accounts, characters,
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            content: LethalContent, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.0);
        await EnterWorldAsync(session, "Ayla");
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var now = DateTimeOffset.UtcNow + CombatService.SwingInterval;
        var log = new List<string>();
        for (var tick = 0; tick < ticks; tick++)
        {
            log.AddRange(session.TickCombat(now));
            now += CombatService.SwingInterval;
        }

        return log;
    }

    private static IAccountStore Accounts => new DevelopmentAccountStore("developer", "dev-secret");

    private static async Task EnterWorldAsync(PlaySession session, string characterName)
    {
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(characterName))));
    }

    private static CharacterRecord Character(string guid, string name, int level) => new(
        guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, level, [100, 200], Xp: 0, Currency: 5000);
}
