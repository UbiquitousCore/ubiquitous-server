using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PlayerResourceTests
{
    private static readonly CharacterRecord Character = new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);

    private static async Task<PlaySession> EnterWorldAsync(
        CharacterRecord character, CreatureSpawnDefinition[]? spawns = null, GameContent? content = null)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([character]), spawns ?? [], content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = character.Guid })));
        return session;
    }

    [Fact]
    public async Task DevEnterWorld_SendsTheStartingResourceSnapshot()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), developmentEntry: true);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var stamina = PlayerStatCurves.AttributesAt(
            Character.Class, Character.Level, StartingLoadouts.For(Character.Race, Character.Class)!.Attributes).Stamina;
        var expectedHealth = PlayerStatCurves.BaseMaxHealth(3, 4, fallback: 0) + stamina * CombatService.HealthPerStamina;
        var snapshot = Assert.Single(responses, m => m.Contains("\"type\":\"player.updated\""));
        Assert.Contains("\"level\":4", snapshot);
        Assert.Contains($"\"health\":{expectedHealth}", snapshot);
        Assert.Contains($"\"maxHealth\":{expectedHealth}", snapshot);
        Assert.Contains("\"power\":100", snapshot);
        Assert.Contains("\"maxPower\":100", snapshot);
        Assert.Contains("\"xp\":0", snapshot);
        Assert.Contains($"\"xpToNextLevel\":{LevelingService.XpToNextLevel(4)}", snapshot);
        Assert.Contains("\"gold\":5000", snapshot);
    }

    [Fact]
    public async Task DevEnterWorld_AManaUsingClassGetsItsPerLevelCurveInsteadOfTheFlatPool()
    {
        var mage = Character with { Class = 2 };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([mage]), developmentEntry: true);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var intellect = PlayerStatCurves.AttributesAt(
            mage.Class, mage.Level, StartingLoadouts.For(mage.Race, mage.Class)!.Attributes).Intellect;
        var expectedMax = PlayerStatCurves.BaseMaxPower(2, mage.Level, fallback: -1) + intellect * SpellService.ManaPerWisdom;
        Assert.True(expectedMax > 0 && expectedMax != 100);
        var snapshot = Assert.Single(responses, m => m.Contains("\"type\":\"player.updated\""));
        Assert.Contains($"\"power\":{expectedMax}", snapshot);
        Assert.Contains($"\"maxPower\":{expectedMax}", snapshot);
    }

    [Fact]
    public async Task Cast_AnInstantCastMarksItsNewPowerForTheValueWindow()
    {
        var content = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [120] = new(120, "spell-120", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 10, EffectDamage: 5),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 120],
        };
        var session = await EnterWorldAsync(Character,
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], content);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 120, targetId = "Creature:1" })));

        Assert.DoesNotContain(responses, m => m.Contains("\"type\":\"player.updated\""));
        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.released\""));
        var flushed = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));
        Assert.Contains(flushed, m => m.Contains("\"type\":\"player.updated\"") && m.Contains("\"power\":90"));
    }

    [Fact]
    public async Task Cast_RefusesACastTheCasterCannotAffordAndSpendsNothing()
    {
        var content = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [121] = new(121, "spell-121", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f,
                    PowerCost: SpellService.PlayerMaxPower + 1, EffectDamage: 5),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 121],
        };
        var session = await EnterWorldAsync(Character,
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], content);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 121, targetId = "Creature:1" })));

        var reply = Assert.Single(responses);
        Assert.Contains("\"type\":\"spell.failed\"", reply);
        Assert.Contains("\"reason\":\"notEnoughPower\"", reply);

        var affordableCast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2, new { spellId = 100, targetId = "Creature:1" })));
        Assert.DoesNotContain(affordableCast, m => m.Contains("\"type\":\"spell.failed\""));
    }

    [Fact]
    public async Task DevEnterWorld_SendsTheCharactersOwnStatsAlongsideTheirBase()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), developmentEntry: true);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var own = PlayerStatCurves.AttributesAt(
            Character.Class, Character.Level, StartingLoadouts.For(Character.Race, Character.Class)!.Attributes);
        var snapshot = Assert.Single(responses, m => m.Contains("\"type\":\"player.updated\""));
        Assert.Contains($"\"strength\":{own.Strength}", snapshot);
        Assert.Contains($"\"baseStrength\":{own.Strength}", snapshot);
        Assert.Contains($"\"agility\":{own.Agility}", snapshot);
        Assert.Contains($"\"baseAgility\":{own.Agility}", snapshot);
        Assert.Contains($"\"stamina\":{own.Stamina}", snapshot);
        Assert.Contains($"\"baseStamina\":{own.Stamina}", snapshot);
        Assert.Contains($"\"wisdom\":{own.Intellect}", snapshot);
        Assert.Contains($"\"baseWisdom\":{own.Intellect}", snapshot);
    }

    [Fact]
    public async Task DevEnterWorld_SendsTheCharacterSheetAsAFightRollsIt()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), developmentEntry: true);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var own = PlayerStatCurves.AttributesAt(
            Character.Class, Character.Level, StartingLoadouts.For(Character.Race, Character.Class)!.Attributes);
        var snapshot = Assert.Single(responses, m => m.Contains("\"type\":\"player.updated\""));
        Assert.Contains(
            $"\"attackPower\":{PlayerStatCurves.AttackPower(Character.Class, Character.Level, own.Strength, own.Agility)}",
            snapshot);
        // With no ratings worn: the base miss and spell chances, and nothing turned aside.
        Assert.Contains("\"hitChance\":9500", snapshot);
        Assert.Contains("\"spellHitChance\":8700", snapshot);
        Assert.Contains("\"critChance\":0", snapshot);
        Assert.Contains("\"parryChance\":0", snapshot);
    }

    [Fact]
    public async Task TickCombat_SendsAStatRaisedAboveItsBaseOnceABuffLands()
    {
        var content = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [122] = new(122, "spell-122", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: AttributeIds.Strength, AuraAttributeAmount: 7, AuraAttributeIsPercent: false,
                    DurationMs: 15000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 122],
        };
        var session = await EnterWorldAsync(Character, content: content);
        var now = DateTimeOffset.UtcNow;
        Assert.DoesNotContain(session.TickCombat(now), m => m.Contains("\"strength\""));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 122 })));
        var tick = session.TickCombat(now + TimeSpan.FromMilliseconds(100));

        var own = PlayerStatCurves.AttributesAt(
            Character.Class, Character.Level, StartingLoadouts.For(Character.Race, Character.Class)!.Attributes);
        var update = Assert.Single(tick, m => m.Contains("\"type\":\"player.updated\"") && m.Contains("\"strength\""));
        Assert.Contains($"\"strength\":{own.Strength + 7}", update);
        Assert.Contains($"\"baseStrength\":{own.Strength}", update);
        Assert.Contains($"\"stamina\":{own.Stamina}", update);
    }
}
