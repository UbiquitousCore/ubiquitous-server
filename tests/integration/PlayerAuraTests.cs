using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PlayerAuraTests
{
    private const string AId = "player:char-a";
    private const string BId = "player:char-b";
    private const uint Slow = 7301;
    private const uint SlowingBolt = 7302;
    private const uint Stun = 7303;
    private const uint Poison = 7304;
    private const uint Weaken = 7305;
    private const uint Bolt = 7306;
    private const uint Shield = 7307;
    private const uint Renew = 7308;
    private const uint Silence = 7309;
    private const int BoltDamage = 20;
    private const int PoisonTick = 10;
    private const int SlowMs = 10000;

    private sealed record Side(PlaySession Session, List<string> Inbox);

    private static readonly GameContent Content = GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [Slow] = new(Slow, "spell-7301", 0, 0, 30f, PowerCost: 0, AuraSlowPercent: 50, DurationMs: SlowMs),
            [SlowingBolt] = new(SlowingBolt, "spell-7302", 0, 0, 30f, PowerCost: 0, EffectDamage: BoltDamage,
                AuraSlowPercent: 50, AuraChill: true, DurationMs: SlowMs),
            [Stun] = new(Stun, "spell-7303", 0, 0, 30f, PowerCost: 0, AuraStun: true, DurationMs: 3000),
            [Poison] = new(Poison, "spell-7304", 0, 0, 30f, PowerCost: 0, AuraPeriodicDamage: PoisonTick),
            [Weaken] = new(Weaken, "spell-7305", 0, 0, 30f, PowerCost: 0, DurationMs: SlowMs,
                AuraAttributeId: AttributeIds.SpellPower, AuraAttributeAmount: -50),
            [Bolt] = new(Bolt, "spell-7306", 0, 0, 30f, PowerCost: 0, EffectDamage: BoltDamage),
            [Shield] = new(Shield, "spell-7307", 0, 0, 0f, PowerCost: 0, DurationMs: 300000,
                AuraAbsorbAmount: 15, AuraAbsorbPercent: 100),
            [Renew] = new(Renew, "spell-7308", 0, 0, 30f, PowerCost: 0, AuraPeriodicHeal: 10),
            [Silence] = new(Silence, "spell-7309", 0, 0, 30f, PowerCost: 0, AuraSilence: true, DurationMs: 3000),
        },
        StartingSpells = [.. GameContent.Development.StartingSpells,
            Slow, SlowingBolt, Stun, Poison, Weaken, Bolt, Shield, Renew, Silence],
    };

    private static async Task<(PlaySessionHub Hub, Side A, Side B)> InWorldAsync()
    {
        var characters = new InMemoryCharacterStore([Character("char-a", "Ayla"), Character("char-b", "Borin")]);
        var duels = new DuelService(countdown: TimeSpan.Zero);
        var flags = new PvpFlagService();
        var blows = new PlayerBlowRelay();
        var hub = new PlaySessionHub();
        Side Join()
        {
            var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters,
                content: Content, combatRollUnit: () => 1.0, spellRollUnit: () => 0.5,
                duels: duels, pvpFlags: flags, blows: blows, developmentEntry: true);
            var inbox = new List<string>();
            hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });
            return new Side(session, inbox);
        }

        var a = Join();
        var b = Join();
        await SendAsync(hub, a, WebMessageTypes.AccountLogin, new LoginRequest("developer", "dev-secret"));
        await SendAsync(hub, a, WebMessageTypes.CharactersChoose, new ChooseCharacterRequest("Ayla"));
        await SendAsync(hub, b, WebMessageTypes.AccountLogin, new LoginRequest("developer", "dev-secret"));
        await SendAsync(hub, b, WebMessageTypes.CharactersChoose, new ChooseCharacterRequest("Borin"));
        a.Inbox.Clear();
        b.Inbox.Clear();
        return (hub, a, b);
    }

    private static async Task<(PlaySessionHub Hub, Side A, Side B)> DuellingAsync()
    {
        var (hub, a, b) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.DuelChallenge, new { targetId = BId });
        await hub.TickAsync(session => session.TickCombat(DateTimeOffset.UtcNow));
        await SendAsync(hub, b, WebMessageTypes.DuelAccept, new { });
        await hub.TickAsync(session => session.TickCombat(DateTimeOffset.UtcNow));
        a.Inbox.Clear();
        b.Inbox.Clear();
        return (hub, a, b);
    }

    private static Task SendAsync(PlaySessionHub hub, Side side, string type, object payload) =>
        hub.SendInOrderAsync(side.Session, () => side.Session.HandleTextAsync(
            WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 1, payload))));

    private static Task CastAsync(PlaySessionHub hub, Side side, uint spellId, string targetId) =>
        SendAsync(hub, side, WebMessageTypes.SpellRequest, new { spellId, targetId });

    private static async Task TickAsync(PlaySessionHub hub, DateTimeOffset at)
    {
        await hub.TickAsync(session => session.TickCombat(at));
        await hub.TickAsync(session => session.TickCombat(at + TimeSpan.FromMilliseconds(DirtyValueTracker.WindowMilliseconds + 100)));
    }

    private static async Task TickEverySecondAsync(PlaySessionHub hub, DateTimeOffset from, int seconds)
    {
        for (var second = 0; second <= seconds; second++) await TickAsync(hub, from + TimeSpan.FromSeconds(second));
    }

    private static IEnumerable<JsonElement> PayloadsOf(IEnumerable<string> inbox, string type) =>
        inbox.Select(message => JsonDocument.Parse(message).RootElement)
            .Where(root => root.GetProperty("type").GetString() == type)
            .Select(root => root.GetProperty("payload").Clone());

    private static IEnumerable<JsonElement> AurasOn(IEnumerable<string> inbox, string type, string unit, uint spellId) =>
        PayloadsOf(inbox, type).Where(p =>
            p.GetProperty("unitId").GetString() == unit && p.GetProperty("spellId").GetUInt32() == spellId);

    private static IEnumerable<JsonElement> OutcomesOf(IEnumerable<string> inbox, uint spellId, string target) =>
        PayloadsOf(inbox, WebMessageTypes.SpellOutcome).Where(p =>
            p.GetProperty("spellId").GetUInt32() == spellId && p.GetProperty("targetId").GetString() == target);

    private static int? LastOwn(IEnumerable<string> inbox, string key) =>
        PayloadsOf(inbox, WebMessageTypes.PlayerValuesChanged)
            .Select(p => p.GetProperty("changed"))
            .Where(changed => changed.TryGetProperty(key, out _))
            .Select(changed => (int?)changed.GetProperty(key).GetInt32())
            .LastOrDefault();

    private static async Task<IReadOnlyList<string>> MoveAsync(Side side, float x, float y, float z) =>
        await side.Session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 2, new PlayerMovePayload(x, y, z, 0, true, MovementState.Walking))));

    private static void AssertLandedOnB(Side a, Side b, uint spellId)
    {
        var applied = Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, spellId));
        Assert.Equal(AId, applied.GetProperty("casterId").GetString());
        Assert.Equal(1, applied.GetProperty("harmful").GetInt32());
        Assert.Single(AurasOn(a.Inbox, WebMessageTypes.AuraApplied, BId, spellId));
        Assert.Empty(AurasOn(a.Inbox, WebMessageTypes.AuraApplied, AId, spellId));
        Assert.Empty(PayloadsOf(b.Inbox, WebMessageTypes.SpellCooldownStarted));
    }

    private static void AssertEndedOnB(Side a, Side b, uint spellId)
    {
        var ended = Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraEnded, BId, spellId));
        Assert.Equal(AId, ended.GetProperty("casterId").GetString());
        Assert.Single(AurasOn(a.Inbox, WebMessageTypes.AuraEnded, BId, spellId));
    }

    [Fact]
    public async Task ASlowCastAtADuelOpponent_HalvesTheirSpeedOnTheirOwnSessionAndTheirOwnScreen()
    {
        var (hub, a, b) = await DuellingAsync();
        await CastAsync(hub, a, Slow, BId);
        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        await TickAsync(hub, now);

        AssertLandedOnB(a, b, Slow);
        Assert.Equal(1, LastOwn(b.Inbox, WireCodes.UnitValue.Slowed));
        Assert.Equal(50, LastOwn(b.Inbox, WireCodes.UnitValue.MoveSpeed));

        var from = b.Session.PlayerPosition!.Value;
        var refused = await MoveAsync(b, from.X + 12, from.Y, from.Z);
        Assert.Contains(refused, message => message.Contains("\"type\":\"move.accepted\""));
        Assert.DoesNotContain(refused, message => message.Contains("\"type\":\"entity.moved\""));
        var halfStep = await MoveAsync(b, from.X + 6, from.Y, from.Z);
        Assert.Contains(halfStep, message => message.Contains("\"type\":\"entity.moved\""));

        await TickAsync(hub, now + TimeSpan.FromMilliseconds(SlowMs + 100));
        AssertEndedOnB(a, b, Slow);
        Assert.Equal(0, LastOwn(b.Inbox, WireCodes.UnitValue.Slowed));
        Assert.Equal(100, LastOwn(b.Inbox, WireCodes.UnitValue.MoveSpeed));
    }

    [Fact]
    public async Task ADamageSpellThatSlows_LandsItsDamageItsSlowAndItsChillOnAFlaggedPlayer()
    {
        var (hub, a, b) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.PvpFlagSet, new { flagged = true });
        await SendAsync(hub, b, WebMessageTypes.PvpFlagSet, new { flagged = true });
        await CastAsync(hub, a, SlowingBolt, BId);
        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        await TickAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));

        Assert.Equal(BoltDamage, Assert.Single(OutcomesOf(b.Inbox, SlowingBolt, BId)).GetProperty("damage").GetInt32());
        AssertLandedOnB(a, b, SlowingBolt);
        Assert.Equal(1, LastOwn(b.Inbox, WireCodes.UnitValue.Slowed));
        Assert.Equal(1, LastOwn(b.Inbox, WireCodes.UnitValue.Chilled));
        Assert.Equal(50, LastOwn(b.Inbox, WireCodes.UnitValue.MoveSpeed));
    }

    [Fact]
    public async Task ADamageSpellThatSlows_SlowsACreatureAndStillDealsItsDamage()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", "Ayla")]),
            new[] { new CreatureSpawnDefinition(7310, 13, 9001, 2, 2, 4, 0) },
            content: Content, spellRollUnit: () => 0.5, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var cast = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 1, new { spellId = SlowingBolt, targetId = "Creature:1" })));

        Assert.Equal(BoltDamage, Assert.Single(OutcomesOf(cast, SlowingBolt, "Creature:1")).GetProperty("damage").GetInt32());
        Assert.Single(AurasOn(cast, WebMessageTypes.AuraApplied, "Creature:1", SlowingBolt));
    }

    [Fact]
    public async Task AStunCastAtADuelOpponent_StopsThemMovingAndCastingUntilItEnds()
    {
        var (hub, a, b) = await DuellingAsync();
        await CastAsync(hub, a, Stun, BId);
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        await TickAsync(hub, now);

        AssertLandedOnB(a, b, Stun);
        Assert.Equal(1, LastOwn(b.Inbox, WireCodes.UnitValue.Stunned));
        Assert.Equal(0, LastOwn(b.Inbox, WireCodes.UnitValue.MoveSpeed));
        var from = b.Session.PlayerPosition!.Value;
        var move = await MoveAsync(b, from.X + 1, from.Y, from.Z);
        Assert.DoesNotContain(move, message => message.Contains("\"type\":\"entity.moved\""));
        b.Inbox.Clear();
        await CastAsync(hub, b, Bolt, AId);
        Assert.Equal(WireCodes.SpellRefusal.Incapacitated,
            Assert.Single(PayloadsOf(b.Inbox, WebMessageTypes.SpellFailed)).GetProperty("reason").GetString());

        await TickAsync(hub, now + TimeSpan.FromMilliseconds(3100));
        AssertEndedOnB(a, b, Stun);
        Assert.Equal(0, LastOwn(b.Inbox, WireCodes.UnitValue.Stunned));
        Assert.Equal(100, LastOwn(b.Inbox, WireCodes.UnitValue.MoveSpeed));
    }

    [Fact]
    public async Task ASilenceCastAtADuelOpponent_StopsThemCastingButNotMoving()
    {
        var (hub, a, b) = await DuellingAsync();
        await CastAsync(hub, a, Silence, BId);
        await TickAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));

        AssertLandedOnB(a, b, Silence);
        b.Inbox.Clear();
        await CastAsync(hub, b, Bolt, AId);
        Assert.Equal(WireCodes.SpellRefusal.Silenced,
            Assert.Single(PayloadsOf(b.Inbox, WebMessageTypes.SpellFailed)).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ADamageOverTimeCastAtADuelOpponent_TicksOnTheirHealth_SeenOnBothScreens()
    {
        var (hub, a, b) = await DuellingAsync();
        await CastAsync(hub, a, Poison, BId);
        await TickEverySecondAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10), 10);

        AssertLandedOnB(a, b, Poison);
        var ticks = OutcomesOf(b.Inbox, Poison, BId).ToList();
        Assert.Equal(3, ticks.Count);
        Assert.All(ticks, tick =>
        {
            Assert.Equal(AId, tick.GetProperty("casterId").GetString());
            Assert.Equal(PoisonTick, tick.GetProperty("damage").GetInt32());
        });
        Assert.Equal(3, OutcomesOf(a.Inbox, Poison, BId).Count());
        Assert.Equal(ticks[^1].GetProperty("finalHealth").GetInt32(), LastOwn(b.Inbox, WireCodes.UnitValue.Health));
        AssertEndedOnB(a, b, Poison);
    }

    [Fact]
    public async Task AShieldTakesAnOpponentsDamageOverTime()
    {
        var (hub, a, b) = await DuellingAsync();
        await CastAsync(hub, b, Shield, "");
        await CastAsync(hub, a, Poison, BId);
        await TickEverySecondAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10), 10);

        var landed = OutcomesOf(b.Inbox, Poison, BId).Select(p => p.GetProperty("damage").GetInt32()).ToArray();
        Assert.Equal([0, 5, PoisonTick], landed);
        var taken = OutcomesOf(b.Inbox, Poison, BId)
            .Select(p => p.TryGetProperty("absorbed", out var absorbed) ? absorbed.GetInt32() : 0).ToArray();
        Assert.Equal([PoisonTick, 5, 0], taken);
        Assert.Single(AurasOn(a.Inbox, WebMessageTypes.AuraEnded, BId, Shield));
    }

    [Fact]
    public async Task AHealOverTimeCastAtAFriendlyPlayer_NamesItsCasterOnEveryTick()
    {
        var (hub, a, b) = await InWorldAsync();
        await CastAsync(hub, a, Renew, BId);
        await TickEverySecondAsync(hub, DateTimeOffset.UtcNow, 10);

        var applied = Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, Renew));
        Assert.Equal(0, applied.GetProperty("harmful").GetInt32());
        Assert.All(OutcomesOf(b.Inbox, Renew, BId), tick => Assert.Equal(AId, tick.GetProperty("casterId").GetString()));
        AssertEndedOnB(a, b, Renew);
    }

    [Fact]
    public async Task AStatDebuffCastAtADuelOpponent_WeakensTheirOwnSpellsUntilItEnds()
    {
        var (hub, a, b) = await DuellingAsync();
        await CastAsync(hub, a, Weaken, BId);
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        await TickAsync(hub, now);
        AssertLandedOnB(a, b, Weaken);

        await CastAsync(hub, b, Bolt, AId);
        await TickAsync(hub, now + TimeSpan.FromMilliseconds(100));
        Assert.Equal(BoltDamage - 5, Assert.Single(OutcomesOf(a.Inbox, Bolt, AId)).GetProperty("damage").GetInt32());

        await TickAsync(hub, now + TimeSpan.FromMilliseconds(SlowMs + 100));
        AssertEndedOnB(a, b, Weaken);
    }

    [Fact]
    public async Task AHarmfulEffectCannotBeCastAtAPlayerWhoIsNotAnOpponent()
    {
        var (hub, a, b) = await InWorldAsync();
        await CastAsync(hub, a, Slow, BId);
        Assert.Single(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        await TickAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));
        Assert.Empty(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, Slow));
    }

    [Fact]
    public async Task TheEndOfADuel_EndsEveryHarmfulEffectTheOpponentLeft_AndNothingElse()
    {
        var (hub, a, b) = await DuellingAsync();
        await CastAsync(hub, b, Shield, "");
        await CastAsync(hub, a, Slow, BId);
        await CastAsync(hub, a, Poison, BId);
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        await TickAsync(hub, now);
        Assert.Equal(50, LastOwn(b.Inbox, WireCodes.UnitValue.MoveSpeed));

        await SendAsync(hub, b, WebMessageTypes.DuelForfeit, new { });
        await TickAsync(hub, now + TimeSpan.FromMilliseconds(100));

        AssertEndedOnB(a, b, Slow);
        AssertEndedOnB(a, b, Poison);
        Assert.Empty(AurasOn(b.Inbox, WebMessageTypes.AuraEnded, BId, Shield));
        Assert.Equal(100, LastOwn(b.Inbox, WireCodes.UnitValue.MoveSpeed));
        await TickEverySecondAsync(hub, now + TimeSpan.FromSeconds(1), 10);
        Assert.Empty(OutcomesOf(b.Inbox, Poison, BId));
    }

    [Fact]
    public async Task TheWinnersOwnSideEndsWhatTheLoserLeftOnThem()
    {
        var (hub, a, b) = await DuellingAsync();
        await CastAsync(hub, b, Slow, AId);
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        await TickAsync(hub, now);
        Assert.Single(AurasOn(a.Inbox, WebMessageTypes.AuraApplied, AId, Slow));

        await SendAsync(hub, b, WebMessageTypes.DuelForfeit, new { });
        await TickAsync(hub, now + TimeSpan.FromMilliseconds(100));

        var ended = Assert.Single(AurasOn(a.Inbox, WebMessageTypes.AuraEnded, AId, Slow));
        Assert.Equal(BId, ended.GetProperty("casterId").GetString());
        Assert.Equal(100, LastOwn(a.Inbox, WireCodes.UnitValue.MoveSpeed));
    }

    [Fact]
    public async Task DyingEndsEveryEffectOnTheBody()
    {
        var (hub, a, b) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.PvpFlagSet, new { flagged = true });
        await SendAsync(hub, b, WebMessageTypes.PvpFlagSet, new { flagged = true });
        await CastAsync(hub, b, Shield, "");
        await CastAsync(hub, a, Slow, BId);
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        await TickAsync(hub, now);
        for (var pass = 0; pass < 200 && !PayloadsOf(b.Inbox, WebMessageTypes.UnitDied).Any(); pass++)
        {
            await CastAsync(hub, a, Bolt, BId);
            await TickAsync(hub, now);
        }

        Assert.Single(PayloadsOf(b.Inbox, WebMessageTypes.UnitDied));
        AssertEndedOnB(a, b, Slow);
        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraEnded, BId, Shield));
    }

    private static CharacterRecord Character(string guid, string name) => new(
        guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, [100, 200]);
}
