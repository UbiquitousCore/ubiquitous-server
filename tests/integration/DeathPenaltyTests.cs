using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class DeathPenaltyTests
{
    private static readonly GameContent LethalContent = GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9001] = new(9001, "creature-9001", 3, 1_000_000,
                MinDamage: 1_000_000, MaxDamage: 1_000_000, LeashRange: 2f),
        },
    };

    private const int StartingCurrency = 5000;

    [Fact]
    public async Task ADeathAtOrBelowTheLevelGateCostsNothing()
    {
        var death = await DeathAsync(level: 10);

        var announced = Assert.Single(death.Log, m => m.Contains("\"type\":\"player.death\""));
        Assert.Contains("\"deathCost\":0", announced);
        Assert.DoesNotContain(death.Log, m => m.Contains("\"type\":\"player.updated\"") && m.Contains("\"gold\":"));
        Assert.Equal(StartingCurrency, await death.SavedCurrencyAsync());
    }

    [Fact]
    public async Task ADeathAtTheFirstPayingLevelChargesTheBandAmount()
    {
        var death = await DeathAsync(level: 11);

        var announced = Assert.Single(death.Log, m => m.Contains("\"type\":\"player.death\""));
        Assert.Contains("\"deathCost\":500", announced);
        Assert.Contains("\"killerId\":\"Creature:1\"", announced);
        Assert.Contains(death.Log, m => m.Contains("\"type\":\"player.updated\"")
            && m.Contains($"\"gold\":{StartingCurrency - 500}"));
        Assert.Equal(StartingCurrency - 500, await death.SavedCurrencyAsync());
    }

    [Fact]
    public async Task ADeathHighUpTheLadderChargesThatBandInstead()
    {
        var death = await DeathAsync(level: 31);

        var announced = Assert.Single(death.Log, m => m.Contains("\"type\":\"player.death\""));
        Assert.Contains("\"deathCost\":3000", announced);
        Assert.Equal(StartingCurrency - 3000, await death.SavedCurrencyAsync());
    }

    [Fact]
    public async Task ADeathCannotTakeAPurseBelowEmpty()
    {
        var death = await DeathAsync(level: 31, currency: 120);

        var announced = Assert.Single(death.Log, m => m.Contains("\"type\":\"player.death\""));
        Assert.Contains("\"deathCost\":120", announced);
        Assert.Contains(death.Log, m => m.Contains("\"type\":\"player.updated\"") && m.Contains("\"gold\":0"));
        Assert.Equal(0, await death.SavedCurrencyAsync());
    }

    [Fact]
    public async Task LosingADuelIsNoDeath_AndCostsTheLoserNothing()
    {
        var characters = new InMemoryCharacterStore(
        [
            Character("char-a", "Ayla", level: 25),
            Character("char-b", "Borin", level: 25),
        ]);
        var duels = new DuelService(countdown: TimeSpan.Zero);
        var blows = new PlayerBlowRelay();
        var sessionA = TestSessions.Create(Accounts, characters, combatRollUnit: () => 1.0, duels: duels, blows: blows);
        var sessionB = TestSessions.Create(Accounts, characters, combatRollUnit: () => 1.0, duels: duels, blows: blows);
        var hub = new PlaySessionHub();
        hub.Register(sessionA, _ => Task.CompletedTask);
        hub.Register(sessionB, _ => Task.CompletedTask);
        await EnterWorldAsync(sessionA, "Ayla");
        await EnterWorldAsync(sessionB, "Borin");

        await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.DuelChallenge, 2, new { targetId = "player:char-b" })));
        await sessionB.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DuelAccept, 3, new { })));
        await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 4, new { targetId = "player:char-b" })));

        var now = DateTimeOffset.UtcNow + CombatService.SwingInterval;
        var log = new List<string>();
        for (var swing = 0; swing < 200 && !log.Any(m => m.Contains("\"type\":\"duel.ended\"")); swing++)
        {
            sessionA.TickCombat(now);
            log.AddRange(sessionB.TickCombat(now));
            now += CombatService.SwingInterval;
        }

        Assert.Contains(log, m => m.Contains("\"type\":\"duel.ended\"") && m.Contains("\"reason\":\"lost\""));
        Assert.DoesNotContain(log, m => m.Contains("\"type\":\"player.death\""));
        Assert.DoesNotContain(log, m => m.Contains("\"type\":\"unit.died\""));
        Assert.DoesNotContain(log, m => m.Contains("\"type\":\"player.updated\"") && m.Contains("\"gold\":"));
        Assert.Equal(StartingCurrency, await SavedCurrencyAsync(sessionB, characters, "char-b"));
    }

    private sealed record Death(List<string> Log, PlaySession Session, InMemoryCharacterStore Characters)
    {
        public Task<int?> SavedCurrencyAsync() => DeathPenaltyTests.SavedCurrencyAsync(Session, Characters, "char-a");
    }

    private static async Task<Death> DeathAsync(int level, int? currency = null)
    {
        var characters = new InMemoryCharacterStore([Character("char-a", "Ayla", level, currency)]);
        var session = TestSessions.Create(Accounts, characters,
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            content: LethalContent, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.0);
        await EnterWorldAsync(session, "Ayla");
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var now = DateTimeOffset.UtcNow + CombatService.SwingInterval;
        var log = new List<string>();
        for (var swing = 0; swing < 20 && !log.Any(m => m.Contains("\"type\":\"player.death\"")); swing++)
        {
            log.AddRange(session.TickCombat(now));
            now += CombatService.SwingInterval;
        }

        Assert.Contains(log, m => m.Contains("\"type\":\"unit.died\"") && m.Contains("player:char-a"));
        return new Death(log, session, characters);
    }

    private static async Task<int?> SavedCurrencyAsync(
        PlaySession session, InMemoryCharacterStore characters, string guid)
    {
        await session.SaveCharacterAsync(CancellationToken.None);
        var saved = await characters.GetByGuidAsync(guid);
        return saved?.Currency;
    }

    private static IAccountStore Accounts => new DevelopmentAccountStore("developer", "dev-secret");

    private static async Task EnterWorldAsync(PlaySession session, string characterName)
    {
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(characterName))));
    }

    private static CharacterRecord Character(string guid, string name, int level, int? currency = null) => new(
        guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, level, [100, 200], Xp: 0, Currency: currency ?? StartingCurrency);
}
