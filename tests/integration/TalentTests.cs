using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class TalentTests
{
    private const int Ranger = PlayerClassIds.Ranger;

    private static readonly IReadOnlyList<TalentTreeDefinition> Trees =
    [
        new(50, Ranger,
        [
            new(501, 1, 1, 0, [], [9501, 9502]),
            new(502, 1, 2, 0, [], [9510]),
            new(503, 2, 1, 5, [], [9520]),
        ]),
        new(60, PlayerClassIds.Warrior, [new(601, 1, 1, 0, [], [9601])]),
    ];

    private static GameContent Content()
    {
        var spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [9501] = new(9501, "hardy-1", 0, 0, 0, AuraAttributeId: AttributeIds.Stamina, AuraAttributeAmount: 10),
            [9502] = new(9502, "hardy-2", 0, 0, 0, AuraAttributeId: AttributeIds.Stamina, AuraAttributeAmount: 20),
            [9510] = new(9510, "talent-strike", 0, 6000, 5, PowerCost: 5, EffectDamage: 10),
            [9520] = new(9520, "deep-passive", 0, 0, 0, AuraAttributeId: AttributeIds.Agility, AuraAttributeAmount: 3),
            [9601] = new(9601, "other-class", 0, 0, 0, AuraAttributeId: AttributeIds.Strength, AuraAttributeAmount: 3),
        };
        return GameContent.Development with { Spells = spells, TalentTrees = Trees };
    }

    [Fact]
    public async Task Entering_SendsThePointsTheLevelHasEarned()
    {
        var (_, entered) = await EnterAsync(new InMemoryCharacterStore([Character(level: 12)]));

        var state = Assert.Single(entered, IsType(WebMessageTypes.TalentState));
        Assert.Contains("\"points\":3", state);
        Assert.Contains("\"ranks\":[]", state);
    }

    [Fact]
    public async Task Learn_SpendsAPointAndAppliesAPassiveStaminaBonus()
    {
        var (session, entered) = await EnterAsync(new InMemoryCharacterStore([Character(level: 12)]));
        var before = MaxHealth(entered);

        var responses = await LearnAsync(session, 501);

        var state = Assert.Single(responses, IsType(WebMessageTypes.TalentState));
        Assert.Contains("\"points\":2", state);
        Assert.Contains("{\"talentId\":501,\"rank\":1}", state);
        Assert.Equal(before + 10 * CombatService.HealthPerStamina, MaxHealth(responses));

        var second = await LearnAsync(session, 501);
        Assert.Equal(before + 20 * CombatService.HealthPerStamina, MaxHealth(second));
    }

    [Fact]
    public async Task Learn_AnAbilityJoinsTheSpellbookAndCanBeCast()
    {
        var (session, entered) = await EnterAsync(new InMemoryCharacterStore([Character(level: 12)]));
        Assert.DoesNotContain("9510", Assert.Single(entered, IsType(WebMessageTypes.SpellBook)));

        var responses = await LearnAsync(session, 502);

        Assert.Contains("9510", Assert.Single(responses, IsType(WebMessageTypes.SpellBook)));
        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 9, new { spellId = 9510 })));
        Assert.DoesNotContain(cast, m => m.Contains(WireCodes.SpellRefusal.UnknownSpell));
    }

    [Theory]
    [InlineData(503u, WireCodes.TalentRefusal.TierLocked)]
    [InlineData(601u, WireCodes.TalentRefusal.WrongClass)]
    [InlineData(999u, WireCodes.TalentRefusal.UnknownTalent)]
    public async Task Learn_IsRefusedWithAReason(uint talentId, string reason)
    {
        var (session, _) = await EnterAsync(new InMemoryCharacterStore([Character(level: 30)]));

        var responses = await LearnAsync(session, talentId);

        var refused = Assert.Single(responses);
        Assert.Contains("\"type\":\"talent.refused\"", refused);
        Assert.Contains($"\"reason\":\"{reason}\"", refused);
    }

    [Fact]
    public async Task Learn_BelowLevelTenHasNoPointToSpend()
    {
        var (session, _) = await EnterAsync(new InMemoryCharacterStore([Character(level: 9)]));

        var refused = Assert.Single(await LearnAsync(session, 501));
        Assert.Contains($"\"reason\":\"{WireCodes.TalentRefusal.NoPoints}\"", refused);
    }

    [Fact]
    public async Task Reset_GivesThePointsBackAndTakesTheAbilityAway()
    {
        var (session, _) = await EnterAsync(new InMemoryCharacterStore([Character(level: 12)]));
        await LearnAsync(session, 502);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.TalentReset, 8, new { })));

        var state = Assert.Single(responses, IsType(WebMessageTypes.TalentState));
        Assert.Contains("\"points\":3", state);
        Assert.DoesNotContain("9510", Assert.Single(responses, IsType(WebMessageTypes.SpellBook)));
    }

    [Fact]
    public async Task BoughtRanks_AreSavedAtOnceAndComeBackNextSession()
    {
        var store = new InMemoryCharacterStore([Character(level: 12)]);
        var (first, _) = await EnterAsync(store);
        await LearnAsync(first, 501);
        await LearnAsync(first, 501);

        var (_, entered) = await EnterAsync(store);

        var state = Assert.Single(entered, IsType(WebMessageTypes.TalentState));
        Assert.Contains("{\"talentId\":501,\"rank\":2}", state);
        Assert.Contains("\"points\":1", state);
    }

    [Fact]
    public async Task ASavedRankTheRulesNoLongerAllowIsDroppedOnEntry()
    {
        var saved = Character(level: 10) with { TalentRanks = new Dictionary<uint, int> { [501] = 2, [503] = 1 } };
        var (_, entered) = await EnterAsync(new InMemoryCharacterStore([saved]));

        var state = Assert.Single(entered, IsType(WebMessageTypes.TalentState));
        Assert.Contains("{\"talentId\":501,\"rank\":1}", state);
        Assert.DoesNotContain("503", state);
        Assert.Contains("\"points\":0", state);
    }

    private static Predicate<string> IsType(string type) => message => message.Contains($"\"type\":\"{type}\"");

    private static int MaxHealth(IEnumerable<string> responses)
    {
        var values = responses.Last(message => message.Contains("\"type\":\"player.updated\"") && message.Contains("\"maxHealth\""));
        const string marker = "\"maxHealth\":";
        var start = values.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = start;
        while (end < values.Length && char.IsDigit(values[end])) end++;
        return int.Parse(values[start..end]);
    }

    private static async Task<IReadOnlyList<string>> LearnAsync(PlaySession session, uint talentId) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.TalentLearn, 7, new TalentLearnRequest(talentId))));

    private static async Task<(PlaySession Session, IReadOnlyList<string> Entered)> EnterAsync(InMemoryCharacterStore store)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), store,
            [], null, Content());
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        var entered = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return (session, entered);
    }

    private static CharacterRecord Character(int level) => new(
        "char-a", 1, "Ayla", 1, 2, Ranger, 4, 5, 6, 7, 8, 13, 1, 2, 3, level, []);
}
