using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class AuraStateFieldTests
{
    private const uint Sneak = 5813;
    private const string AnyBodyState = "\"stunned\":";

    private static readonly IReadOnlyList<int> DualWielding =
        [.. EquipmentSlots.Build((EquipmentSlots.MainHand, 3907), (EquipmentSlots.OffHand, 3907))];

    [Fact]
    public async Task Cast_SneakRaisesTheStealthBitOnTheCaster()
    {
        var session = await SessionAsync(GameContent.Development);

        var responses = await CastAsync(session, Sneak);

        var flushed = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));
        Assert.Contains(flushed, message => message.Contains("\"type\":\"player.updated\"")
            && message.Contains("\"stealthed\":1"));
    }

    [Fact]
    public async Task Cast_SneakAlsoTellsEveryoneWatchingTheCaster()
    {
        var session = await SessionAsync(GameContent.Development);

        var responses = await CastAsync(session, Sneak);

        var flushed = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));
        Assert.Contains(flushed, message => message.Contains("\"type\":\"entity.updated\"")
            && message.Contains("\"networkId\":\"player:char-a\"")
            && message.Contains("\"stealthed\":1"));
    }

    [Fact]
    public async Task TickCombat_ClearsTheStealthBitWhenTheAuraRunsOut()
    {
        var session = await SessionAsync(GameContent.Development);
        var start = DateTimeOffset.UtcNow;
        await CastAsync(session, Sneak);

        var held = session.TickCombat(start + TimeSpan.FromSeconds(13));
        Assert.Contains(held, message => message.Contains("\"type\":\"player.updated\"")
            && message.Contains("\"stealthed\":1"));
        session.TickCombat(start + TimeSpan.FromSeconds(15));
        var expired = session.TickCombat(start + TimeSpan.FromSeconds(15) + TimeSpan.FromMilliseconds(700));

        Assert.Contains(expired, message => message.Contains("\"type\":\"player.updated\"")
            && message.Contains("\"stealthed\":0"));
    }

    [Fact]
    public async Task TickCombat_SaysNothingWhileTheStateHasNotChanged()
    {
        var session = await SessionAsync(GameContent.Development);
        var start = DateTimeOffset.UtcNow;
        await CastAsync(session, Sneak);

        var first = session.TickCombat(start + TimeSpan.FromSeconds(1));
        Assert.Contains(first, message => message.Contains(AnyBodyState));
        var second = session.TickCombat(start + TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(second, message => message.Contains(AnyBodyState));
    }

    [Fact]
    public async Task Cast_AStunRaisesItsOwnBitOnTheCreatureItLandedOn()
    {
        var session = await SessionAsync(WithSpell(109, new(109, "spell-109",
            CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5, AuraStun: true, DurationMs: 2000)));

        var responses = await CastAsync(session, 109);

        var flushed = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));
        Assert.Contains(flushed, message => message.Contains("\"type\":\"entity.updated\"")
            && message.Contains("\"networkId\":\"Creature:1\"")
            && message.Contains("\"stunned\":1"));
    }

    [Fact]
    public async Task Cast_AChillRaisesItsOwnBitOnTheCreatureItLandedOn()
    {
        var session = await SessionAsync(WithSpell(110, new(110, "spell-110",
            CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5, AuraChill: true, DurationMs: 2000)));

        var responses = await CastAsync(session, 110);

        var flushed = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));
        Assert.Contains(flushed, message => message.Contains("\"type\":\"entity.updated\"")
            && message.Contains("\"networkId\":\"Creature:1\"")
            && message.Contains("\"chilled\":1"));
    }

    [Fact]
    public async Task TickCombat_ClearsACreaturesOwnBitWhenItsAuraRunsOut()
    {
        var session = await SessionAsync(WithSpell(110, new(110, "spell-110",
            CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5, AuraChill: true, DurationMs: 2000)));
        var start = DateTimeOffset.UtcNow;
        await CastAsync(session, 110);

        var expired = session.TickCombat(start + TimeSpan.FromSeconds(3));

        Assert.Contains(expired, message => message.Contains("\"type\":\"entity.updated\"")
            && message.Contains("\"networkId\":\"Creature:1\"")
            && message.Contains("\"chilled\":0"));
    }

    [Fact]
    public async Task Cast_AStunLeavesTheCastersOwnStateAlone()
    {
        var session = await SessionAsync(WithSpell(109, new(109, "spell-109",
            CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5, AuraStun: true, DurationMs: 2000)));

        var responses = await CastAsync(session, 109);

        Assert.DoesNotContain(responses, message => message.Contains("\"type\":\"player.updated\"")
            && message.Contains(AnyBodyState));
    }

    private static GameContent WithSpell(uint id, SpellDefinition spell) => GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells) { [id] = spell },
        StartingSpells = [.. GameContent.Development.StartingSpells, id],
    };

    private static async Task<IReadOnlyList<string>> CastAsync(PlaySession session, uint spellId) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 5, new { spellId, targetId = "Creature:1" })));

    private static async Task<PlaySession> SessionAsync(GameContent content)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, PlayerClassIds.Ranger, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, DualWielding)]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 3, 2, 3, 0)], null, content,
            spellRollUnit: () => 1.0, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
