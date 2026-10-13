using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class GameMasterTests
{
    [Fact]
    public async Task AdminSession_SwingsForWhatItsCharacterActuallyHits()
    {
        var session = await SessionAsync(admin: true,
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)]);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var tick = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        Assert.Contains(tick, m => m.Contains("\"type\":\"attack.swingResult\""));
        Assert.DoesNotContain(tick, m => m.Contains($"\"damage\":{CombatService.GodModeDamage}"));
    }

    [Fact]
    public async Task AdminSession_FinishesAFightWithTheOperatorSpell()
    {
        var session = await SessionAsync(admin: true,
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)]);

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2,
            new { spellId = OperatorSpells.Finish, targetId = "Creature:1" })));

        Assert.Contains(cast, m => m.Contains("\"type\":\"unit.died\"") && m.Contains("Creature:1"));
    }

    [Fact]
    public async Task AnOrdinarySession_IsRefusedTheOperatorSpell()
    {
        var session = await SessionAsync(admin: false,
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)]);

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2,
            new { spellId = OperatorSpells.Finish, targetId = "Creature:1" })));

        Assert.Contains(cast, m => m.Contains("\"type\":\"spell.failed\""));
        Assert.DoesNotContain(cast, m => m.Contains("\"type\":\"unit.died\""));
    }

    [Fact]
    public async Task AdminSession_TakesDamageLikeAnybodyElse()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9001] = new(9001, "creature-9001", 3, 200000, MinDamage: 50, MaxDamage: 50),
            },
        };
        var session = await SessionAsync(admin: true,
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var at = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        session.TickCombat(at);

        var flushed = session.TickCombat(at + TimeSpan.FromMilliseconds(700));
        var fullHealth = PlayerStatCurves.BaseMaxHealth(3, 4, CombatService.PlayerMaxHealth)
            + PlayerStatCurves.AttributesAt(3, 4, StartingLoadouts.For(2, 3)!.Attributes).Stamina * CombatService.HealthPerStamina;
        Assert.Contains(flushed, m => m.Contains("\"type\":\"player.updated\""));
        Assert.DoesNotContain(flushed, m => m.Contains("\"type\":\"player.updated\"")
            && m.Contains($"\"health\":{fullHealth}"));
    }

    [Fact]
    public async Task TheImmunityDialStillHolds()
    {
        var combat = new CombatService();
        combat.EnterAsCharacter(3, 4, "player:char-a");
        var full = combat.PlayerHealth;
        combat.GodMode = true;

        combat.DamageTarget("Creature:1", "player:char-a", 50, DateTimeOffset.UtcNow);

        Assert.Equal(full, combat.PlayerHealth);
    }

    [Fact]
    public async Task OrdinarySession_HitsForOrdinaryDamage()
    {
        var session = await SessionAsync(admin: false,
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)]);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var tick = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(tick, m => m.Contains($"\"damage\":{CombatService.GodModeDamage}"));
        Assert.DoesNotContain(tick, m => m.Contains("\"type\":\"unit.died\""));
    }

    private static async Task<PlaySession> SessionAsync(
        bool admin, CreatureSpawnDefinition[] spawns, GameContent? content = null)
    {
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret", isAdmin: admin),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            spawns, content: content ?? GameContent.Development,
            spellRollUnit: () => 1.0, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    [Fact]
    public async Task AdminSession_TopsItselfUpWithTheOperatorSpell()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9001] = new(9001, "creature-9001", 3, 200000, MinDamage: 50, MaxDamage: 50),
            },
        };
        var session = await SessionAsync(admin: true,
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var at = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        session.TickCombat(at);

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 3, new { spellId = OperatorSpells.Restore })));

        var flushed = session.TickCombat(at + TimeSpan.FromMilliseconds(700));
        var fullHealth = PlayerStatCurves.BaseMaxHealth(3, 4, CombatService.PlayerMaxHealth)
            + PlayerStatCurves.AttributesAt(3, 4, StartingLoadouts.For(2, 3)!.Attributes).Stamina * CombatService.HealthPerStamina;
        Assert.Contains(flushed, m => m.Contains("\"type\":\"player.updated\"")
            && m.Contains($"\"health\":{fullHealth}"));
    }

    [Fact]
    public async Task AnOrdinarySession_IsRefusedTheOperatorHeal()
    {
        var session = await SessionAsync(admin: false,
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)]);

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2, new { spellId = OperatorSpells.Restore })));

        Assert.Contains(cast, m => m.Contains("\"type\":\"spell.failed\""));
    }
}
