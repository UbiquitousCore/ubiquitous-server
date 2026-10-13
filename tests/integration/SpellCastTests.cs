using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class SpellCastTests
{
    private static readonly CharacterRecord Character = new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);

    private static int MageMaxPower(CharacterRecord mage, int wisdomBonus)
    {
        var spells = new SpellService([]);
        spells.EnterAsCharacter(mage.Class, mage.Level,
            baseIntellect: StartingLoadouts.For(mage.Race, mage.Class)!.Attributes.Intellect);
        return spells.EffectiveMaxPower(wisdomBonus);
    }

    private static StartingAttributes OwnAttributes() =>
        PlayerStatCurves.AttributesAt(
            Character.Class, Character.Level, StartingLoadouts.For(Character.Race, Character.Class)!.Attributes);

    private static async Task<PlaySession> EnterWorldAsync(
        CreatureSpawnDefinition[] spawns, Func<double>? spellRollUnit = null)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), spawns, spellRollUnit: spellRollUnit ?? (() => 1.0), developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        return session;
    }

    private static Task<PlaySession> EnterWorldAsync(params CreatureSpawnDefinition[] spawns) =>
        EnterWorldAsync(spawns, spellRollUnit: null);

    [Fact]
    public async Task EnteringTheWorld_SendsTheKnownSpellList()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), developmentEntry: true);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var book = StartingLoadouts.For(Character.Race, Character.Class)!;
        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.book\"")
            && m.Contains(
                $"\"spells\":[100,{OperatorSpells.Finish},{OperatorSpells.Restore},"
                + string.Join(',', book.Spells) + "]"));
    }

    [Fact]
    public async Task EnteringTheWorld_AnOrdinaryHumanRangerDoesNotGetTheDevPlaytestersDungeonSpells()
    {
        var human = Character with { Race = PlayerRaceIds.Human, Class = PlayerClassIds.Ranger };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([human]), developmentEntry: true);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = human.Guid })));

        var book = StartingLoadouts.For(human.Race, human.Class)!;
        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.book\"")
            && m.Contains(
                $"\"spells\":[100,{OperatorSpells.Finish},{OperatorSpells.Restore},"
                + string.Join(',', book.Spells) + "]"));
    }

    [Fact]
    public async Task Cast_OnAVisibleTargetStartsTheCastBar()
    {
        var session = await EnterWorldAsync(new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 100, targetId = "Creature:1" })));

        var reply = Assert.Single(responses);
        Assert.Contains("\"type\":\"spell.casting\"", reply);
        Assert.Contains("\"spellId\":100", reply);
        Assert.Contains("\"castTime\":1500", reply);
    }

    // A spell that starts the fight says so before its outcome: ubiquitous-client drops a reply numbered below one it has read.
    [Fact]
    public async Task Cast_ThatStartsAFightNumbersEveryReplyInTheOrderItIsSent()
    {
        var monk = Character with { Race = PlayerRaceIds.Human, Class = PlayerClassIds.Monk };
        var content = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [5460] = new(5460, "spell-5460", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f,
                    PowerCost: 5, EffectDamage: 1000),
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([monk]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            content: content, spellRollUnit: () => 1.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = monk.Guid })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 5460, targetId = "Creature:1" })));

        var numbers = responses.Select(m => WebMessageJson.Deserialize(m).Sequence).ToList();
        Assert.Contains(responses, m => m.Contains("\"type\":\"attack.enterCombat\""));
        Assert.Contains(responses, m => m.Contains("\"type\":\"unit.died\""));
        Assert.Equal(numbers.Order(), numbers);
    }

    [Fact]
    public async Task Cast_ADifferentClasssOwnOpenerSpellIsActuallyKnown()
    {
        var monk = Character with { Race = PlayerRaceIds.Human, Class = PlayerClassIds.Monk };
        var content = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [5460] = new(5460, "spell-5460", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f,
                    PowerCost: 5, EffectDamage: 10),
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([monk]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            content: content, spellRollUnit: () => 1.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = monk.Guid })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 5460, targetId = "Creature:1" })));

        Assert.DoesNotContain(responses, m => m.Contains(WebMessageTypes.SpellFailed));
        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.released\"") && m.Contains("\"spellId\":5460"));
    }

    [Fact]
    public async Task TickCombat_ResolvesTheCastOnceItsTimeElapsesAndStartsItsCooldown()
    {
        var session = await EnterWorldAsync(new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        var now = DateTimeOffset.UtcNow;
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 100, targetId = "Creature:1" })));

        var early = session.TickCombat(now.AddMilliseconds(500));
        Assert.DoesNotContain(early, m => m.Contains("\"type\":\"spell.released\""));

        var responses = session.TickCombat(now.AddMilliseconds(1600));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.released\""));
        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"damage\":20"));
        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.cooldownStarted\"") && m.Contains("\"spellId\":100"));

        Assert.Contains(responses, m =>
            m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"finalHealth\":80"));

        var flushed = session.TickCombat(now.AddMilliseconds(2300));
        Assert.Contains(flushed, m => m.Contains("\"type\":\"entity.updated\"") && m.Contains("\"health\":80"));
    }

    [Fact]
    public async Task Cast_AMissedSpellDealsNoDamageButStillStartsItsCooldown()
    {
        var session = await EnterWorldAsync(
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], () => SpellService.ResistChance);
        var now = DateTimeOffset.UtcNow;
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 100, targetId = "Creature:1" })));

        var responses = session.TickCombat(now.AddMilliseconds(1600));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"result\":\"miss\"") && m.Contains("\"damage\":0"));
        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.cooldownStarted\"") && m.Contains("\"spellId\":100"));
        Assert.DoesNotContain(responses, m => m.Contains("\"type\":\"entity.updated\""));

        Assert.DoesNotContain(responses, m => m.Contains("\"finalHealth\""));
    }

    [Fact]
    public async Task Cast_AResistedSpellDealsNoDamageAndSendsNoLogEntry()
    {
        var session = await EnterWorldAsync(
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], () => 0.0);
        var now = DateTimeOffset.UtcNow;
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 100, targetId = "Creature:1" })));

        var responses = session.TickCombat(now.AddMilliseconds(1600));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.released\"") && m.Contains("\"resisted\":[\"Creature:1\"]"));
        Assert.DoesNotContain(responses, m => m.Contains("\"type\":\"spell.outcome\""));
        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.cooldownStarted\"") && m.Contains("\"spellId\":100"));
    }

    [Fact]
    public async Task Cast_RefusesAnUnknownSpell()
    {
        var session = await EnterWorldAsync(new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 9999, targetId = "Creature:1" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.failed\"") && m.Contains("\"reason\":\"unknownSpell\""));
    }

    [Fact]
    public async Task Cast_RefusesAnInvisibleTarget()
    {
        var session = await EnterWorldAsync();

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 100, targetId = "Creature:404" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.failed\"") && m.Contains("\"reason\":\"invalidTarget\""));
    }

    [Fact]
    public async Task Cast_RefusesWhileOnCooldown()
    {
        var session = await EnterWorldAsync(new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        var now = DateTimeOffset.UtcNow;
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 100, targetId = "Creature:1" })));
        session.TickCombat(now.AddMilliseconds(1600));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2, new { spellId = 100, targetId = "Creature:1" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.failed\"") && m.Contains("\"reason\":\"onCooldown\""));
    }

    [Fact]
    public async Task Cancel_StopsAnInFlightCast()
    {
        var session = await EnterWorldAsync(new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 100, targetId = "Creature:1" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.SpellAbort, 2, new { spellId = 100 })));

        var reply = Assert.Single(responses);
        Assert.Contains("\"type\":\"spell.interrupted\"", reply);

        var later = session.TickCombat(DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.DoesNotContain(later, m => m.Contains("\"type\":\"spell.released\""));
    }

    [Fact]
    public async Task Cast_OutOfRangeIsRefusedAndNeverGoesOff()
    {
        var session = await EnterWorldAsync(new CreatureSpawnDefinition(1001, 13, 9001, 45, 2, 3, 0));
        var now = DateTimeOffset.UtcNow;

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 100, targetId = "Creature:1" })));

        var reply = Assert.Single(responses);
        Assert.Contains("\"type\":\"spell.failed\"", reply);
        Assert.Contains("\"reason\":\"outOfRange\"", reply);
        var later = session.TickCombat(now.AddSeconds(5));
        Assert.DoesNotContain(later, m => m.Contains("\"type\":\"spell.released\"") || m.Contains("\"type\":\"spell.outcome\""));

        var again = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2, new { spellId = 100, targetId = "Creature:1" })));
        Assert.DoesNotContain(again, m => m.Contains("\"reason\":\"alreadyCasting\"") || m.Contains("\"reason\":\"onCooldown\""));
    }

    [Fact]
    public async Task Move_WalkingInterruptsACastInProgress()
    {
        var session = await EnterWorldAsync(new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        var now = DateTimeOffset.UtcNow;
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 100, targetId = "Creature:1" })));

        var moved = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 2, new PlayerMovePayload(2, 2, 3, 0, true, MovementState.Walking))));

        Assert.Contains(moved, m => m.Contains("\"type\":\"spell.interrupted\""));
        var later = session.TickCombat(now.AddSeconds(5));
        Assert.DoesNotContain(later, m => m.Contains("\"type\":\"spell.released\""));
    }

    [Fact]
    public async Task Move_TurningInPlaceLetsTheCastFinish()
    {
        var session = await EnterWorldAsync(new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        var now = DateTimeOffset.UtcNow;
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 100, targetId = "Creature:1" })));

        var turned = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 2, new PlayerMovePayload(1, 2, 3, 1.5, true, MovementState.Walking))));

        Assert.DoesNotContain(turned, m => m.Contains("\"type\":\"spell.interrupted\""));
        Assert.Contains(session.TickCombat(now.AddMilliseconds(1600)), m => m.Contains("\"type\":\"spell.released\""));
    }

    [Fact]
    public async Task TickCombat_ACastWhoseTargetLeftRangeFailsWhenItFinishesAndCostsNothing()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            spellRollUnit: () => 1.0, sharedMaps: maps, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 100, targetId = "Creature:1" })));

        maps.For(13).SetPosition("Creature:1", new Ubiquitous.Common.Position(45, 2, 3));
        var responses = session.TickCombat(now.AddMilliseconds(1600));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.failed\"") && m.Contains("\"reason\":\"outOfRange\""));
        Assert.DoesNotContain(responses, m => m.Contains("\"type\":\"spell.outcome\"")
            || m.Contains("\"type\":\"spell.cooldownStarted\"") || m.Contains("\"power\""));

        maps.For(13).SetPosition("Creature:1", new Ubiquitous.Common.Position(2, 2, 4));
        var again = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2, new { spellId = 100, targetId = "Creature:1" })));
        Assert.Contains(again, m => m.Contains("\"type\":\"spell.casting\""));
    }

    [Fact]
    public async Task Cancel_WithNothingPendingIsRefused()
    {
        var session = await EnterWorldAsync();

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.SpellAbort, 1, new { spellId = 100 })));

        Assert.Contains("invalid_state", Assert.Single(responses));
    }

    [Fact]
    public async Task SetActionSlot_StoresAndEchoesBackWithTheBarConverted()
    {
        var session = await EnterWorldAsync();

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.ActionBarAssign, 1, new { slot = 7, id = 100, type = 0, misc = 0 })));

        var reply = Assert.Single(responses);
        Assert.Contains("\"type\":\"actionBar.assigned\"", reply);
        Assert.Contains("\"bar\":\"shortcut\"", reply);
        Assert.Contains("\"slot\":1", reply);
        Assert.Contains("\"id\":100", reply);
    }

    [Fact]
    public async Task Cast_AHealSpellTargetsSelfAndNeedsNoVisibleBody()
    {
        var healContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [104] = new(104, "spell-104", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 10, EffectHeal: 40),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 104],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), content: healContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 104 })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"result\":\"heal\""));
        var flushed = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));
        Assert.Contains(flushed, m => m.Contains("\"type\":\"player.updated\""));
    }

    [Fact]
    public async Task Cast_ADamageOverTimeAppliesAnAuraThenTicksOnItsOwnClock()
    {
        var dotContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [107] = new(107, "spell-107", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5, AuraPeriodicDamage: 6),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 107],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: dotContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 107, targetId = "Creature:1" })));

        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":1"));
        Assert.DoesNotContain(cast, m => m.Contains("\"type\":\"spell.outcome\""));

        var slack = TimeSpan.FromMilliseconds(100);
        var firstTick = session.TickCombat(now + AuraService.TickInterval + slack);
        Assert.Contains(firstTick, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"damage\":6"));
        Assert.DoesNotContain(firstTick, m => m.Contains("\"type\":\"aura.ended\""));

        IReadOnlyList<string> lastTick = [];
        for (int i = 1; i < AuraService.TickCount; i++)
            lastTick = session.TickCombat(now + AuraService.TickInterval * (i + 1) + slack);
        Assert.Contains(lastTick, m => m.Contains("\"type\":\"aura.ended\""));
    }

    [Fact]
    public async Task Cast_AHealOverTimeTargetsSelfAndNeedsNoVisibleBody()
    {
        var hotContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [108] = new(108, "spell-108", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5, AuraPeriodicHeal: 8),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 108],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), content: hotContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 108 })));

        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":0"));

        var tick = session.TickCombat(now + AuraService.TickInterval + TimeSpan.FromMilliseconds(100));
        Assert.Contains(tick, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"result\":\"periodicHeal\""));
    }

    [Fact]
    public async Task Cast_AHealingPowerBuffIncreasesWhatAHealOverTimeRestoresPerTick()
    {
        var hotContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [117] = new(117, "spell-117", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 41, AuraAttributeAmount: 10, AuraAttributeIsPercent: false, DurationMs: 5000),
                [119] = new(119, "spell-119", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5, AuraPeriodicHeal: 4),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 117, 119],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: hotContent, combatRollUnit: () => 1.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 1, new { targetId = "Creature:1" })));
        session.TickCombat(now + CombatService.SwingInterval + TimeSpan.FromMilliseconds(100));
        session.TickCombat(now + (CombatService.SwingInterval * 2) + TimeSpan.FromMilliseconds(100));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2, new { spellId = 117 })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 3, new { spellId = 119 })));

        var tick = session.TickCombat(now + AuraService.TickInterval + TimeSpan.FromMilliseconds(100));

        var expectedTick = CombatService.ApplyHealingPowerBonus(4, healingPower: 10);
        Assert.Contains(tick, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains($"\"damage\":{expectedTick}"));
    }

    [Fact]
    public async Task Cast_ASpellPowerBuffIncreasesTheDamageADirectDamageSpellDeals()
    {
        var spellPowerContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [117] = new(117, "spell-117", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 38, AuraAttributeAmount: 100, AuraAttributeIsPercent: false, DurationMs: 5000),
                [121] = new(121, "spell-121", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5, EffectDamage: 15),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 117, 121],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: spellPowerContent, spellRollUnit: () => 1.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var buffCast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 117 })));
        Assert.Contains(buffCast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":0"));

        var damageCast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2, new { spellId = 121, targetId = "Creature:1" })));

        var expectedDamage = CombatService.ApplySpellPowerBonus(15, spellPower: 100);
        Assert.Contains(damageCast, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains($"\"damage\":{expectedDamage}"));
    }

    [Fact]
    public async Task Cast_ASpellPowerBuffIncreasesWhatADamageOverTimeDealsPerTick()
    {
        var dotContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [117] = new(117, "spell-117", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 38, AuraAttributeAmount: 100, AuraAttributeIsPercent: false, DurationMs: 5000),
                [122] = new(122, "spell-122", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5, AuraPeriodicDamage: 6),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 117, 122],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: dotContent, spellRollUnit: () => 1.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 117 })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2, new { spellId = 122, targetId = "Creature:1" })));

        var tick = session.TickCombat(now + AuraService.TickInterval + TimeSpan.FromMilliseconds(100));

        var expectedTick = CombatService.ApplySpellPowerBonus(6, spellPower: 100);
        Assert.Contains(tick, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains($"\"damage\":{expectedTick}"));
    }

    [Fact]
    public async Task Cast_ASpellCriticalRatingBuffCanDoubleADirectDamageSpells()
    {
        var spellCritContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [117] = new(117, "spell-117", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 40, AuraAttributeAmount: 100, AuraAttributeIsPercent: false, DurationMs: 5000),
                [121] = new(121, "spell-121", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5, EffectDamage: 15),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 117, 121],
        };
        var spellRolls = 0;
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: spellCritContent,
            spellRollUnit: () => spellRolls++ == 0 ? 0.99 : 0.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var buffCast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 117 })));
        Assert.Contains(buffCast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":0"));

        var damageCast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2, new { spellId = 121, targetId = "Creature:1" })));

        var expectedDamage = (int)Math.Round(15 * SpellService.SpellCriticalDamageMultiplier, MidpointRounding.AwayFromZero);
        Assert.Contains(damageCast, m => m.Contains("\"type\":\"spell.outcome\"")
            && m.Contains("\"result\":\"critical\"")
            && m.Contains($"\"damage\":{expectedDamage}"));
    }

    [Fact]
    public async Task Cast_AStunAppliesAndExpiresTheControlLockOnItsTarget()
    {
        var stunContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [109] = new(109, "spell-109", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraStun: true, DurationMs: 2000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 109],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: stunContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 109, targetId = "Creature:1" })));

        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"")
            && m.Contains("\"harmful\":1") && m.Contains("\"remaining\":2000"));
        Assert.DoesNotContain(cast, m => m.Contains("\"type\":\"spell.outcome\""));

        var slack = TimeSpan.FromMilliseconds(100);
        var expiry = session.TickCombat(now + TimeSpan.FromMilliseconds(2000) + slack);
        Assert.Contains(expiry, m => m.Contains("\"type\":\"aura.ended\""));
    }

    [Fact]
    public async Task Cast_ASilenceAppliesAndExpiresTheCastLockOnItsTarget()
    {
        var silenceContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [112] = new(112, "spell-112", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraSilence: true, DurationMs: 2000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 112],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: silenceContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 112, targetId = "Creature:1" })));

        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"")
            && m.Contains("\"harmful\":1") && m.Contains("\"remaining\":2000"));
        Assert.DoesNotContain(cast, m => m.Contains("\"type\":\"spell.outcome\""));

        var silenceSlack = TimeSpan.FromMilliseconds(100);
        var silenceExpiry = session.TickCombat(now + TimeSpan.FromMilliseconds(2000) + silenceSlack);
        Assert.Contains(silenceExpiry, m => m.Contains("\"type\":\"aura.ended\""));
    }

    [Fact]
    public async Task Cast_ASlowAppliesAndExpiresTheDebuffOnItsTarget()
    {
        var slowContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [110] = new(110, "spell-110", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraSlowPercent: 50, DurationMs: 2000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 110],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: slowContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var slowCast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 110, targetId = "Creature:1" })));

        Assert.Contains(slowCast, m => m.Contains("\"type\":\"aura.applied\"")
            && m.Contains("\"harmful\":1") && m.Contains("\"remaining\":2000"));
        Assert.DoesNotContain(slowCast, m => m.Contains("\"type\":\"spell.outcome\""));

        var slowSlack = TimeSpan.FromMilliseconds(100);
        var slowExpiry = session.TickCombat(now + TimeSpan.FromMilliseconds(2000) + slowSlack);
        Assert.Contains(slowExpiry, m => m.Contains("\"type\":\"aura.ended\""));
    }

    [Fact]
    public async Task Cast_ASpeedupAppliesAndExpiresABuffOnItsTarget()
    {
        var speedupContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [111] = new(111, "spell-111", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraSpeedupPercent: 30, DurationMs: 2000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 111],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: speedupContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var speedupCast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 111, targetId = "Creature:1" })));

        Assert.Contains(speedupCast, m => m.Contains("\"type\":\"aura.applied\"")
            && m.Contains("\"harmful\":0") && m.Contains("\"remaining\":2000"));

        var speedupSlack = TimeSpan.FromMilliseconds(100);
        var speedupExpiry = session.TickCombat(now + TimeSpan.FromMilliseconds(2000) + speedupSlack);
        Assert.Contains(speedupExpiry, m => m.Contains("\"type\":\"aura.ended\""));
    }

    [Fact]
    public async Task Cast_AMeleeAttackSpeedBuffAppliesAndExpiresOnItsTarget()
    {
        var attackSpeedContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [113] = new(113, "spell-113", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraMeleeAttackSpeedPercent: 50, DurationMs: 2000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 113],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: attackSpeedContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 113, targetId = "Creature:1" })));

        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"")
            && m.Contains("\"harmful\":0") && m.Contains("\"remaining\":2000"));

        var slack = TimeSpan.FromMilliseconds(100);
        var expiry = session.TickCombat(now + TimeSpan.FromMilliseconds(2000) + slack);
        Assert.Contains(expiry, m => m.Contains("\"type\":\"aura.ended\""));
    }

    [Fact]
    public async Task Cast_AChillAppliesAndExpiresATagWithNoOtherEffect()
    {
        var chillContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [117] = new(117, "spell-117", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraChill: true, DurationMs: 2000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 117],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: chillContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var chillCast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 117, targetId = "Creature:1" })));

        Assert.Contains(chillCast, m => m.Contains("\"type\":\"aura.applied\"")
            && m.Contains("\"harmful\":1") && m.Contains("\"remaining\":2000"));
        Assert.DoesNotContain(chillCast, m => m.Contains("\"type\":\"spell.outcome\""));

        var chillSlack = TimeSpan.FromMilliseconds(100);
        var chillExpiry = session.TickCombat(now + TimeSpan.FromMilliseconds(2000) + chillSlack);
        Assert.Contains(chillExpiry, m => m.Contains("\"type\":\"aura.ended\""));
    }

    [Fact]
    public async Task Cast_ADamageSpellThatChillsDealsItsDamageAndChillsTheCreature()
    {
        var content = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [118] = new(118, "spell-118", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    EffectDamage: 26, AuraChill: true, DurationMs: 5000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 118],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: content, spellRollUnit: () => 1.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 118, targetId = "Creature:1" })));

        Assert.Contains(cast, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"damage\":26"));
        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"")
            && m.Contains("\"harmful\":1") && m.Contains("\"remaining\":5000"));
    }

    [Fact]
    public async Task Cast_APureTriggerSpellFiresTheTriggeredSpellsOwnEffect()
    {
        var triggerContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [200] = new(200, "spell-200", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraTriggerSpellId: 201, AuraTriggerChancePercent: 100),
                [201] = new(201, "spell-201", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 0,
                    EffectDamage: 15),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 200, 201],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: triggerContent, spellRollUnit: () => 0.5, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 200, targetId = "Creature:1" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"damage\":15"));
    }

    [Fact]
    public async Task Cast_APeriodicTriggerAppliesAnAuraThenRollsOnItsOwnClock()
    {
        var periodicTriggerContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [202] = new(202, "spell-202", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraPeriodicTriggerSpellId: 201, AuraPeriodicTriggerChancePercent: 100),
                [201] = new(201, "spell-201", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 0,
                    EffectDamage: 15),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 202, 201],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: periodicTriggerContent, spellRollUnit: () => 0.5, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 202, targetId = "Creature:1" })));

        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":1"));
        Assert.DoesNotContain(cast, m => m.Contains("\"type\":\"spell.outcome\""));

        var slack = TimeSpan.FromMilliseconds(100);
        var firstTick = session.TickCombat(now + AuraService.TickInterval + slack);
        Assert.Contains(firstTick, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"damage\":15"));
    }

    [Fact]
    public async Task Cast_ASpellWithBothADirectEffectAndATriggerAppliesBothWhenTheChanceLands()
    {
        var triggerContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [200] = new(200, "spell-200", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    EffectDamage: 20, AuraTriggerSpellId: 201, AuraTriggerChancePercent: 100),
                [201] = new(201, "spell-201", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 0,
                    EffectDamage: 5),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 200, 201],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: triggerContent, spellRollUnit: () => 0.5, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 200, targetId = "Creature:1" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"damage\":20"));
        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"damage\":5"));
    }

    [Fact]
    public async Task Cast_ATriggerThatDoesNotLandAppliesOnlyThePrimaryEffect()
    {
        var triggerContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [200] = new(200, "spell-200", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    EffectDamage: 20, AuraTriggerSpellId: 201, AuraTriggerChancePercent: 20),
                [201] = new(201, "spell-201", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 0,
                    EffectDamage: 5),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 200, 201],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: triggerContent, spellRollUnit: () => 0.5, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 200, targetId = "Creature:1" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"damage\":20"));
        Assert.DoesNotContain(responses, m => m.Contains("\"damage\":5"));
    }

    [Fact]
    public async Task Cast_AnAttributeDebuffAppliesAndExpiresWithItsNegativeSignShown()
    {
        var debuffContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [110] = new(110, "spell-110", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 0, AuraAttributeAmount: -21, AuraAttributeIsPercent: false, DurationMs: 2000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 110],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: debuffContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 110, targetId = "Creature:1" })));

        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"")
            && m.Contains("\"harmful\":1") && m.Contains("\"remaining\":2000"));
        Assert.DoesNotContain(cast, m => m.Contains("\"type\":\"spell.outcome\""));

        var slack = TimeSpan.FromMilliseconds(100);
        var expiry = session.TickCombat(now + TimeSpan.FromMilliseconds(2000) + slack);
        Assert.Contains(expiry, m => m.Contains("\"type\":\"aura.ended\""));
    }

    [Fact]
    public async Task Cast_AnArmorBuffSelfTargetsAndReducesRetaliationDamageTaken()
    {
        var armorContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [111] = new(111, "spell-111", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 0, AuraAttributeAmount: 600, AuraAttributeIsPercent: false, DurationMs: 5000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 111],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: armorContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 111 })));
        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":0"));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var swings = session.TickCombat(now + CombatService.SwingInterval + TimeSpan.FromMilliseconds(100));

        var retaliation = Assert.Single(swings, s => s.Contains("\"type\":\"attack.swingResult\"") && s.Contains("\"attackerId\":\"Creature:1\""));
        Assert.Contains($"\"damage\":{CombatService.ApplyArmorMitigation(CombatService.RetaliationDamage, 600)}", retaliation);
    }

    [Fact]
    public async Task Cast_AStrengthBuffSelfTargetsAndIncreasesThePlayersOwnMeleeDamage()
    {
        var strengthContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [112] = new(112, "spell-112", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 25, AuraAttributeAmount: 100, AuraAttributeIsPercent: false, DurationMs: 5000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 112],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: strengthContent, combatRollUnit: () => 1.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 112 })));
        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":0"));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var swings = session.TickCombat(now + CombatService.SwingInterval + TimeSpan.FromMilliseconds(100));

        var meleeHit = Assert.Single(swings, s => s.Contains("\"attackerId\":\"player:char-a\""));
        var own = OwnAttributes();
        var attackPower = PlayerStatCurves.AttackPower(
            Character.Class, Character.Level, strength: own.Strength + 100, agility: own.Agility);
        var expectedDamage = CombatService.ApplyAttackPowerBonus(
            CombatService.ApplyStrengthBonus(CombatService.SwingDamage, 100), attackPower);
        Assert.Contains($"\"damage\":{expectedDamage}", meleeHit);
    }

    [Fact]
    public async Task Cast_AHealingPowerBuffIncreasesTheAmountAFollowingHealRestores()
    {
        var healingPowerContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [117] = new(117, "spell-117", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 41, AuraAttributeAmount: 10, AuraAttributeIsPercent: false, DurationMs: 5000),
                [118] = new(118, "spell-118", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5, EffectHeal: 5),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 117, 118],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: healingPowerContent, combatRollUnit: () => 1.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 1, new { targetId = "Creature:1" })));
        session.TickCombat(now + CombatService.SwingInterval + TimeSpan.FromMilliseconds(100));
        session.TickCombat(now + (CombatService.SwingInterval * 2) + TimeSpan.FromMilliseconds(100));

        var buffCast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2, new { spellId = 117 })));
        Assert.Contains(buffCast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":0"));

        var healCast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 3, new { spellId = 118 })));

        var expectedHeal = CombatService.ApplyHealingPowerBonus(5, healingPower: 10);
        Assert.Contains(healCast, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains($"\"damage\":{expectedHeal}"));
    }

    [Fact]
    public async Task Cast_AStaminaBuffSelfTargetsAndRaisesThePlayersOwnMaxHealth()
    {
        var staminaContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [113] = new(113, "spell-113", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 27, AuraAttributeAmount: 50, AuraAttributeIsPercent: false, DurationMs: 5000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 113],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: staminaContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 113 })));
        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":0"));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var swings = session.TickCombat(now + CombatService.SwingInterval + TimeSpan.FromMilliseconds(100));

        var ownStamina = PlayerStatCurves.AttributesAt(
            Character.Class, Character.Level, StartingLoadouts.For(Character.Race, Character.Class)!.Attributes).Stamina;
        var expectedMaxHealth = PlayerStatCurves.BaseMaxHealth(Character.Class, Character.Level, CombatService.PlayerMaxHealth)
            + (ownStamina + 50) * CombatService.HealthPerStamina;
        Assert.Contains(swings, s => s.Contains("\"type\":\"player.updated\"")
            && s.Contains($"\"maxHealth\":{expectedMaxHealth}"));
    }

    [Fact]
    public async Task Cast_AnAgilityBuffSelfTargetsAndCanDodgeARetaliationSwing()
    {
        var agilityContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [114] = new(114, "spell-114", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 26, AuraAttributeAmount: 100, AuraAttributeIsPercent: false, DurationMs: 5000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 114],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: agilityContent,
            combatRollUnit: () => 0.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 114 })));
        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":0"));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var swings = session.TickCombat(now + CombatService.SwingInterval + TimeSpan.FromMilliseconds(100));

        var retaliation = Assert.Single(swings, s => s.Contains("\"type\":\"attack.swingResult\"") && s.Contains("\"attackerId\":\"Creature:1\""));
        Assert.Contains("\"outcome\":\"dodge\"", retaliation);
        Assert.Contains("\"damage\":0", retaliation);
    }

    [Fact]
    public async Task Cast_ADodgeRatingBuffAloneCanDodgeARetaliationSwingWithNoAgilityInvolved()
    {
        var dodgeRatingContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [120] = new(120, "spell-120", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 21, AuraAttributeAmount: 100, AuraAttributeIsPercent: false, DurationMs: 5000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 120],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: dodgeRatingContent,
            combatRollUnit: () => 0.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 120 })));
        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":0"));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var swings = session.TickCombat(now + CombatService.SwingInterval + TimeSpan.FromMilliseconds(100));

        var retaliation = Assert.Single(swings, s => s.Contains("\"type\":\"attack.swingResult\"") && s.Contains("\"attackerId\":\"Creature:1\""));
        Assert.Contains("\"outcome\":\"dodge\"", retaliation);
        Assert.Contains("\"damage\":0", retaliation);
    }

    [Fact]
    public async Task Cast_ACritRatingBuffSelfTargetsAndCanDoubleAnOutgoingMeleeSwing()
    {
        var critContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [117] = new(117, "spell-117", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 18, AuraAttributeAmount: 100, AuraAttributeIsPercent: false, DurationMs: 5000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 117],
        };
        var combatRolls = 0;
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: critContent,
            combatRollUnit: () => combatRolls++ == 0 ? 1.0 : 0.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 117 })));
        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":0"));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var swings = session.TickCombat(now + CombatService.SwingInterval + TimeSpan.FromMilliseconds(100));

        var outgoing = Assert.Single(swings, s => s.Contains("\"attackerId\":\"player:char-a\""));
        Assert.Contains("\"outcome\":\"critical\"", outgoing);
        var own = OwnAttributes();
        var hit = CombatService.ApplyAttackPowerBonus(CombatService.SwingDamage,
            PlayerStatCurves.AttackPower(Character.Class, Character.Level, own.Strength, own.Agility));
        Assert.Contains(
            $"\"damage\":{(int)Math.Round(hit * CombatService.CriticalDamageMultiplier)}",
            outgoing);
    }

    [Fact]
    public async Task Cast_AWisdomBuffSelfTargetsAndRaisesThePlayersOwnMaxPower()
    {
        var wisdomContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [115] = new(115, "spell-115", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 28, AuraAttributeAmount: 50, AuraAttributeIsPercent: false, DurationMs: 15000),
                [116] = new(116, "spell-116", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 90, EffectHeal: 1),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 115, 116],
        };
        var mage = Character with { Class = PlayerClassIds.Mage };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([mage]), content: wisdomContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 115 })));
        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":0"));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2, new { spellId = 116 })));

        var tick = session.TickCombat(now + SpellService.PowerRegenInterval + TimeSpan.FromMilliseconds(100));

        Assert.Contains(tick, m => m.Contains("\"type\":\"player.updated\"")
            && m.Contains($"\"maxPower\":{MageMaxPower(mage, wisdomBonus: 50)}"));
    }

    [Fact]
    public async Task Cast_AParryRatingBuffSelfTargetsAndCanParryARetaliationSwing()
    {
        var parryContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [118] = new(118, "spell-118", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 22, AuraAttributeAmount: 100, AuraAttributeIsPercent: false, DurationMs: 5000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 118],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: parryContent,
            combatRollUnit: () => 0.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 118 })));
        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":0"));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var swings = session.TickCombat(now + CombatService.SwingInterval + TimeSpan.FromMilliseconds(100));

        var retaliation = Assert.Single(swings, s => s.Contains("\"type\":\"attack.swingResult\"") && s.Contains("\"attackerId\":\"Creature:1\""));
        Assert.Contains("\"outcome\":\"parry\"", retaliation);
        Assert.Contains("\"damage\":0", retaliation);
    }

    [Fact]
    public async Task Cast_ABlockRatingBuffSelfTargetsAndCanBlockARetaliationSwing()
    {
        var blockContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [119] = new(119, "spell-119", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                    AuraAttributeId: 20, AuraAttributeAmount: 100, AuraAttributeIsPercent: false, DurationMs: 5000),
            },
            StartingSpells = [.. GameContent.Development.StartingSpells, 119],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: blockContent,
            combatRollUnit: () => 0.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var now = DateTimeOffset.UtcNow;

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = 119 })));
        Assert.Contains(cast, m => m.Contains("\"type\":\"aura.applied\"") && m.Contains("\"harmful\":0"));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var swings = session.TickCombat(now + CombatService.SwingInterval + TimeSpan.FromMilliseconds(100));

        var retaliation = Assert.Single(swings, s => s.Contains("\"type\":\"attack.swingResult\"") && s.Contains("\"attackerId\":\"Creature:1\""));
        Assert.Contains("\"outcome\":\"block\"", retaliation);
        var expectedDamage = CombatService.ApplyArmorMitigation(
            (int)Math.Round(
                CombatService.RetaliationDamage * (1 - CombatService.BlockDamageReduction),
                MidpointRounding.AwayFromZero),
            0);
        Assert.Contains($"\"damage\":{expectedDamage}", retaliation);
    }
}
