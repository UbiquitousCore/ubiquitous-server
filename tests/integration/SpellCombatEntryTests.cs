using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class SpellCombatEntryTests
{
    private const uint ShadowStrike = 5815;
    private const uint RangedBolt = 100;
    private const float PlayerStartX = 1;

    private static readonly IReadOnlyList<int> DualWielding =
        [.. EquipmentSlots.Build((EquipmentSlots.MainHand, 3907), (EquipmentSlots.OffHand, 3907))];

    [Fact]
    public async Task Cast_FightsTheCreaturesOwnHealthPoolNotTheSyntheticFallback()
    {
        var session = await SessionAsync(Creature(maxHealth: 99999), At(2));

        var responses = await CastAsync(session, ShadowStrike);

        var flushed = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));
        var values = Assert.Single(flushed, message => message.Contains("\"type\":\"entity.updated\"")
            && message.Contains("\"networkId\":\"Creature:1\""));
        Assert.Contains("\"maxHealth\":99999", values);
        Assert.DoesNotContain(responses, message => message.Contains("\"type\":\"unit.died\""));
    }

    [Fact]
    public async Task Cast_SurvivesAsManyBlowsAsItsOwnHealthPoolAllows()
    {
        var session = await SessionAsync(Creature(maxHealth: 99999), At(2));

        for (var cast = 0; cast < 12; cast++) await CastAsync(session, ShadowStrike);
        var responses = await CastAsync(session, ShadowStrike);

        Assert.DoesNotContain(responses, message => message.Contains("\"type\":\"unit.died\""));
    }

    [Fact]
    public async Task Cast_PutsBothSidesIntoCombatWithTheSameMessagesARightClickSends()
    {
        var session = await SessionAsync(Creature(), At(2));

        var responses = await CastAsync(session, ShadowStrike);

        Assert.Contains(responses, message => message.Contains("\"type\":\"attack.meleeStart\"")
            && message.Contains("\"attackerId\":\"player:char-a\"")
            && message.Contains("\"targetId\":\"Creature:1\""));
        Assert.Contains(responses, message => message.Contains("\"type\":\"attack.enterCombat\"")
            && message.Contains("\"networkId\":\"player:char-a\""));
        Assert.Contains(responses, message => message.Contains("\"type\":\"attack.enterCombat\"")
            && message.Contains("\"networkId\":\"Creature:1\""));
    }

    [Fact]
    public async Task Cast_ReportsTheFightStartingBeforeTheBlowThatStartedIt()
    {
        var session = await SessionAsync(Creature(), At(2));

        var responses = await CastAsync(session, ShadowStrike);

        var start = IndexOf(responses, "\"type\":\"attack.meleeStart\"");
        var log = IndexOf(responses, "\"type\":\"spell.outcome\"");
        Assert.True(start < log, "attack.meleeStart must precede spell.outcome");
    }

    [Fact]
    public async Task Cast_LeavesTheCreatureSwingingBackOnTheFollowingTicks()
    {
        var session = await SessionAsync(Creature(), At(2));
        var start = DateTimeOffset.UtcNow;
        await CastAsync(session, ShadowStrike);

        var later = session.TickCombat(start + TimeSpan.FromSeconds(3));

        Assert.Contains(later, message => message.Contains("\"type\":\"attack.swingResult\"")
            && message.Contains("\"attackerId\":\"Creature:1\""));
    }

    [Fact]
    public async Task Cast_OpensTheSameFightFromRangeThroughTheDirectDamagePath()
    {
        var session = await SessionAsync(Creature(), At(12));
        var start = DateTimeOffset.UtcNow;
        await CastAsync(session, RangedBolt);

        var resolved = session.TickCombat(start + TimeSpan.FromSeconds(2));

        Assert.Contains(resolved, message => message.Contains("\"type\":\"attack.meleeStart\""));
        Assert.Contains(resolved, message => message.Contains("\"type\":\"attack.enterCombat\"")
            && message.Contains("\"networkId\":\"player:char-a\""));
        Assert.Contains(resolved, message => message.Contains("\"type\":\"attack.enterCombat\"")
            && message.Contains("\"networkId\":\"Creature:1\""));
    }

    [Fact]
    public async Task Cast_LeavesTheCreatureWalkingTowardAPlayerWhoOpenedFromRange()
    {
        var session = await SessionAsync(Creature(), At(12));
        var start = DateTimeOffset.UtcNow;
        await CastAsync(session, RangedBolt);
        session.TickCombat(start + TimeSpan.FromSeconds(2));

        var later = session.TickCombat(start + TimeSpan.FromSeconds(2.25));

        Assert.Contains(later, message => message.Contains("\"type\":\"entity.moved\"")
            && message.Contains("\"networkId\":\"Creature:1\""));
    }

    [Fact]
    public async Task Cast_NeverMakesADummyWalkTowardAPlayerWhoOpenedFromRange()
    {
        var session = await SessionAsync(Dummy(), At(12));
        var start = DateTimeOffset.UtcNow;
        await CastAsync(session, RangedBolt);
        session.TickCombat(start + TimeSpan.FromSeconds(2));

        var later = session.TickCombat(start + TimeSpan.FromSeconds(2.25));

        Assert.DoesNotContain(later, message => message.Contains("\"type\":\"entity.moved\"")
            && message.Contains("\"networkId\":\"Creature:1\""));
    }

    [Fact]
    public async Task Cast_NeverTakesHealthFromADummyCarryingTheUnkillableFlagBit()
    {
        var session = await SessionAsync(Dummy(maxHealth: 83), At(2));

        var responses = await CastAsync(session, ShadowStrike);

        var flushed = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));
        var values = Assert.Single(flushed, message => message.Contains("\"type\":\"entity.updated\"")
            && message.Contains("\"networkId\":\"Creature:1\""));
        Assert.Contains("\"health\":83", values);
        Assert.Contains("\"maxHealth\":83", values);
        Assert.Contains(responses, message => message.Contains("\"type\":\"spell.outcome\""));
    }

    [Fact]
    public async Task Cast_NeverKillsADummyCarryingTheUnkillableFlagBitHoweverManyLand()
    {
        var session = await SessionAsync(Dummy(maxHealth: 83), At(2));

        for (var cast = 0; cast < 20; cast++)
        {
            var responses = await CastAsync(session, ShadowStrike);
            Assert.DoesNotContain(responses, message => message.Contains("\"type\":\"unit.died\""));
        }
    }

    [Fact]
    public async Task Cast_StillKillsAScriptBoundDummyBecauseItCarriesNoFlagBit()
    {
        var session = await SessionAsync(
            new CreatureDefinition(1226, "creature-1226", 1, 1, RawExtraFlags: 1u << 15), At(2));

        var responses = await CastAsync(session, ShadowStrike);

        Assert.Contains(responses, message => message.Contains("\"type\":\"unit.died\""));
    }

    [Fact]
    public async Task Cast_NeverMakesADummySwingBackEvenThoughTheFightOpened()
    {
        var session = await SessionAsync(Dummy(), At(2));
        var start = DateTimeOffset.UtcNow;

        var responses = await CastAsync(session, ShadowStrike);
        var later = session.TickCombat(start + TimeSpan.FromSeconds(3));

        Assert.Contains(responses, message => message.Contains("\"type\":\"attack.enterCombat\""));
        Assert.DoesNotContain(later, message => message.Contains("\"type\":\"attack.swingResult\"")
            && message.Contains("\"attackerId\":\"Creature:1\""));
    }

    [Fact]
    public async Task Cast_NeverMakesADummyWalkTowardThePlayerEither()
    {
        var session = await SessionAsync(Dummy(), At(12));
        var start = DateTimeOffset.UtcNow;
        await CastAsync(session, ShadowStrike);

        var later = session.TickCombat(start + TimeSpan.FromMilliseconds(250));

        Assert.DoesNotContain(later, message => message.Contains("\"type\":\"entity.moved\"")
            && message.Contains("\"networkId\":\"Creature:1\""));
    }

    private static int IndexOf(IReadOnlyList<string> responses, string marker)
    {
        for (var index = 0; index < responses.Count; index++)
            if (responses[index].Contains(marker)) return index;
        return -1;
    }

    private static CreatureDefinition Creature(int maxHealth = 100) =>
        new(9001, "creature-9001", 3, maxHealth, MinDamage: 4, MaxDamage: 4);

    private static CreatureDefinition Dummy(int maxHealth = 100) =>
        new(9001, "creature-9001", 3, maxHealth, MinDamage: 4, MaxDamage: 4, RawExtraFlags: 1u << 7);

    private static CreatureSpawnDefinition At(float distance) =>
        new(1001, 13, 9001, PlayerStartX + distance, 2, 3, 0);

    private static async Task<IReadOnlyList<string>> CastAsync(PlaySession session, uint spellId) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 5, new { spellId, targetId = "Creature:1" })));

    private static async Task<PlaySession> SessionAsync(
        CreatureDefinition creature, CreatureSpawnDefinition spawn)
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9001] = creature,
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, PlayerClassIds.Ranger, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, DualWielding)]),
            [spawn], null, content, spellRollUnit: () => 1.0, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
