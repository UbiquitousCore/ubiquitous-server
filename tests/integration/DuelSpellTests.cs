using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class DuelSpellTests
{
    private const string AId = "player:char-a";
    private const string BId = "player:char-b";
    private const uint ChillingBolt = 950;
    private const uint SlowChillingBolt = 960;
    private const uint Shield = 970;
    private const uint StunningCharge = 980;
    private const uint Revive = 990;
    private const int StunMs = 3000;
    private const int BoltDamage = 20;
    private const int ChillMs = 2000;

    private sealed record Side(PlaySession Session, List<string> Inbox);

    private static readonly GameContent Content = GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [ChillingBolt] = new(ChillingBolt, "spell-950", 0, 0, 30f, PowerCost: 0, EffectDamage: BoltDamage,
                AuraChill: true, DurationMs: ChillMs),
            [SlowChillingBolt] = new(SlowChillingBolt, "spell-960", 1500, 0, 30f, PowerCost: 0, EffectDamage: BoltDamage,
                AuraChill: true, DurationMs: ChillMs),
            [Shield] = new(Shield, "spell-970", 0, 0, 0f, PowerCost: 0, DurationMs: 300000,
                AuraAbsorbAmount: 30, AuraAbsorbPercent: 100),
            [StunningCharge] = new(StunningCharge, "spell-980", 0, 0, 30f, PowerCost: 0,
                EffectTarget: SpellEffectTargets.SingleEnemy,
                ExtraEffects:
                [
                    new(StunningCharge, "spell-980", 0, 0, 30f, PowerCost: 0, AuraStun: true, DurationMs: StunMs,
                        EffectTarget: SpellEffectTargets.SingleEnemy),
                ]),
            [Revive] = new(Revive, "spell-990", 0, 0, 30f, PowerCost: 0, EffectTarget: 13),
        },
        StartingSpells = [.. GameContent.Development.StartingSpells, ChillingBolt, SlowChillingBolt, Shield,
            StunningCharge, Revive],
    };

    private static async Task<(PlaySessionHub Hub, Side A, Side B)> InWorldAsync()
    {
        var characters = new InMemoryCharacterStore(
        [
            Character("char-a", "Ayla"),
            Character("char-b", "Borin"),
        ]);
        var duels = new DuelService(countdown: TimeSpan.Zero);
        var flags = new PvpFlagService();
        var blows = new PlayerBlowRelay();
        var hub = new PlaySessionHub();
        Side Join()
        {
            var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters,
                content: Content, combatRollUnit: () => 1.0, spellRollUnit: () => 0.5,
                duels: duels, pvpFlags: flags, blows: blows);
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

    private static async Task TickBothAsync(PlaySessionHub hub, DateTimeOffset now)
    {
        await hub.TickAsync(session => session.TickCombat(now));
        await hub.TickAsync(session => session.TickCombat(now + TimeSpan.FromMilliseconds(750)));
        await hub.TickAsync(session => session.TickCombat(now + TimeSpan.FromMilliseconds(1500)));
    }

    private static IEnumerable<JsonElement> PayloadsOf(IEnumerable<string> inbox, string type) =>
        inbox.Select(message => JsonDocument.Parse(message).RootElement)
            .Where(root => root.GetProperty("type").GetString() == type)
            .Select(root => root.GetProperty("payload").Clone());

    private static IEnumerable<JsonElement> OutcomesOn(IEnumerable<string> inbox, string caster, string target) =>
        PayloadsOf(inbox, WebMessageTypes.SpellOutcome).Where(p =>
            p.GetProperty("casterId").GetString() == caster && p.GetProperty("targetId").GetString() == target);

    private static IEnumerable<JsonElement> AurasOn(IEnumerable<string> inbox, string type, string unit, uint spellId) =>
        PayloadsOf(inbox, type).Where(p =>
            p.GetProperty("unitId").GetString() == unit && p.GetProperty("spellId").GetUInt32() == spellId);

    private static IEnumerable<JsonElement> Emotes(IEnumerable<string> inbox, string by) =>
        PayloadsOf(inbox, WebMessageTypes.EmotePerformed).Where(p => p.GetProperty("networkId").GetString() == by);

    private static bool? LastOwnChill(IEnumerable<string> inbox) =>
        PayloadsOf(inbox, WebMessageTypes.PlayerValuesChanged)
            .Select(p => p.GetProperty("changed"))
            .Where(changed => changed.TryGetProperty(WireCodes.UnitValue.Chilled, out _))
            .Select(changed => (bool?)(changed.GetProperty(WireCodes.UnitValue.Chilled).GetInt32() != 0))
            .LastOrDefault();

    [Fact]
    public async Task ADamageSpellThatChills_LandsItsDamageAndItsChillOnTheDuelOpponent_HeldByTheirSession()
    {
        var (hub, a, b) = await DuellingAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = ChillingBolt, targetId = BId });
        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        await TickBothAsync(hub, now);

        var onVictim = Assert.Single(OutcomesOn(b.Inbox, AId, BId));
        Assert.Equal(BoltDamage, onVictim.GetProperty("damage").GetInt32());
        Assert.Single(OutcomesOn(a.Inbox, AId, BId));

        var chill = Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, ChillingBolt));
        Assert.Equal(AId, chill.GetProperty("casterId").GetString());
        Assert.Equal(1, chill.GetProperty("harmful").GetInt32());
        Assert.Single(AurasOn(a.Inbox, WebMessageTypes.AuraApplied, BId, ChillingBolt));
        Assert.True(LastOwnChill(b.Inbox));

        await TickBothAsync(hub, now + TimeSpan.FromMilliseconds(ChillMs + 100));
        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraEnded, BId, ChillingBolt));
        Assert.Single(AurasOn(a.Inbox, WebMessageTypes.AuraEnded, BId, ChillingBolt));
        Assert.False(LastOwnChill(b.Inbox));
    }

    [Fact]
    public async Task ACastTimeDamageSpellThatChills_LandsOnAFlaggedPlayerWhenTheCastFinishes()
    {
        var (hub, a, b) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.PvpFlagSet, new { flagged = true });
        await SendAsync(hub, b, WebMessageTypes.PvpFlagSet, new { flagged = true });
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = SlowChillingBolt, targetId = BId });
        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        Assert.Equal(BoltDamage, Assert.Single(OutcomesOn(b.Inbox, AId, BId)).GetProperty("damage").GetInt32());
        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, SlowChillingBolt));
    }

    [Fact]
    public async Task ADamageSpellThatChills_StillCannotBeCastAtAPlayerWhoIsNotAnOpponent()
    {
        var (hub, a, b) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = ChillingBolt, targetId = BId });
        Assert.Single(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));
        Assert.Empty(OutcomesOn(b.Inbox, AId, BId));
        Assert.Empty(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, ChillingBolt));
    }

    [Fact]
    public async Task ASpellWhoseEffectIsAllInItsExtras_LandsThemOnTheDuelOpponent()
    {
        var (hub, a, b) = await DuellingAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = StunningCharge, targetId = BId });
        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));

        var stun = Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, StunningCharge));
        Assert.Equal(AId, stun.GetProperty("casterId").GetString());
    }

    [Fact]
    public async Task ASpellMeantForFriends_StillCannotBeAimedAtTheDuelOpponent()
    {
        var (hub, a, _) = await DuellingAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = Revive, targetId = BId });
        Assert.Single(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
    }

    [Fact]
    public async Task AShieldIsRaisedOnTheCasterWithNobodySelected()
    {
        var (hub, a, _) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = Shield, targetId = "" });

        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        var raised = Assert.Single(AurasOn(a.Inbox, WebMessageTypes.AuraApplied, AId, Shield));
        Assert.Equal(0, raised.GetProperty("harmful").GetInt32());
        Assert.Equal(300000, raised.GetProperty("remaining").GetInt32());
    }

    [Fact]
    public async Task AShieldTakesAnOpponentsBlowsUntilItIsSpent_ThenEnds()
    {
        var (hub, a, b) = await DuellingAsync();
        await SendAsync(hub, b, WebMessageTypes.SpellRequest, new { spellId = Shield, targetId = AId });
        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, Shield));
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);

        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = ChillingBolt, targetId = BId });
        await TickBothAsync(hub, now);
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = ChillingBolt, targetId = BId });
        await TickBothAsync(hub, now + TimeSpan.FromSeconds(2));

        var landed = OutcomesOn(b.Inbox, AId, BId).Select(p => p.GetProperty("damage").GetInt32()).ToArray();
        Assert.Equal([0, 10], landed);
        int[] Absorbed(IEnumerable<string> inbox) => OutcomesOn(inbox, AId, BId)
            .Select(p => p.TryGetProperty("absorbed", out var taken) ? taken.GetInt32() : 0).ToArray();
        Assert.Equal([20, 10], Absorbed(b.Inbox));
        Assert.Equal([20, 10], Absorbed(a.Inbox));
        await TickBothAsync(hub, now + TimeSpan.FromSeconds(4));
        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraEnded, BId, Shield));
        Assert.Single(AurasOn(a.Inbox, WebMessageTypes.AuraEnded, BId, Shield));
    }

    [Fact]
    public async Task ThePlayerWhoLosesADuelBegs_OnTheirOwnScreenAndTheWinners()
    {
        var (hub, a, b) = await DuellingAsync();
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        for (var pass = 0; pass < 100 && !PayloadsOf(b.Inbox, WebMessageTypes.DuelEnded).Any(); pass++)
        {
            await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = ChillingBolt, targetId = BId });
            await TickBothAsync(hub, now);
            now += TimeSpan.FromSeconds(2);
        }
        await TickBothAsync(hub, now);

        Assert.Equal("lost", Assert.Single(PayloadsOf(b.Inbox, WebMessageTypes.DuelEnded)).GetProperty("reason").GetString());
        var begged = Assert.Single(Emotes(b.Inbox, BId));
        Assert.Equal("beg", begged.GetProperty("name").GetString());
        var seen = Assert.Single(Emotes(a.Inbox, BId));
        Assert.Equal(begged.GetProperty("id").GetInt32(), seen.GetProperty("id").GetInt32());
        Assert.Empty(Emotes(a.Inbox, AId));
        Assert.Empty(Emotes(b.Inbox, AId));
    }

    [Fact]
    public async Task APlayerWhoForfeitsADuelDoesNotBeg()
    {
        var (hub, a, b) = await DuellingAsync();
        await SendAsync(hub, b, WebMessageTypes.DuelForfeit, new { });
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));

        Assert.Single(PayloadsOf(b.Inbox, WebMessageTypes.DuelEnded));
        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.EmotePerformed));
        Assert.Empty(PayloadsOf(b.Inbox, WebMessageTypes.EmotePerformed));
    }

    private static CharacterRecord Character(string guid, string name) => new(
        guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, [100, 200]);
}
