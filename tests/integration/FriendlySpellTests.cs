using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class FriendlySpellTests
{
    private const string AId = "player:char-a";
    private const string BId = "player:char-b";
    private const uint InstantHeal = 900;
    private const uint CastTimeHeal = 901;
    private const uint CastTimeBuff = 902;
    private const uint HealOverTime = 903;
    private const uint Buff = 904;
    private const uint SpeedUp = 905;
    private const uint FlyingHeal = 906;

    private sealed record Side(PlaySession Session, List<string> Inbox);

    private static readonly GameContent Content = GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [InstantHeal] = new(InstantHeal, "spell-900", 0, 0, 30f, PowerCost: 0, EffectHeal: 40),
            [CastTimeHeal] = new(CastTimeHeal, "spell-901", 1500, 0, 30f, PowerCost: 0, EffectHeal: 40),
            [CastTimeBuff] = new(CastTimeBuff, "spell-902", 1500, 0, 30f, PowerCost: 0, DurationMs: 10000,
                AuraAttributeId: AttributeIds.Stamina, AuraAttributeAmount: 5),
            [HealOverTime] = new(HealOverTime, "spell-903", 0, 0, 30f, PowerCost: 0, AuraPeriodicHeal: 10),
            [Buff] = new(Buff, "spell-904", 0, 0, 30f, PowerCost: 0, DurationMs: 4000,
                AuraAttributeId: AttributeIds.Stamina, AuraAttributeAmount: 5),
            [SpeedUp] = new(SpeedUp, "spell-905", 0, 0, 30f, PowerCost: 0, DurationMs: 4000, AuraSpeedupPercent: 30),
            [FlyingHeal] = new(FlyingHeal, "spell-906", 0, 0, 30f, PowerCost: 0, EffectHeal: 40, Speed: 20f),
        },
        StartingSpells = [.. GameContent.Development.StartingSpells, InstantHeal, CastTimeHeal, CastTimeBuff,
            HealOverTime, Buff, SpeedUp, FlyingHeal],
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

    private static async Task<int> HurtThenMakePeaceAsync(PlaySessionHub hub, Side a, Side b)
    {
        await SendAsync(hub, a, WebMessageTypes.DuelChallenge, new { targetId = BId });
        await hub.TickAsync(session => session.TickCombat(DateTimeOffset.UtcNow));
        await SendAsync(hub, b, WebMessageTypes.DuelAccept, new { });
        await hub.TickAsync(session => session.TickCombat(DateTimeOffset.UtcNow));
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = 5815u, targetId = BId });
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));
        var hurt = PayloadsOf(b.Inbox, WebMessageTypes.SpellOutcome).Last().GetProperty("finalHealth").GetInt32();
        await SendAsync(hub, a, WebMessageTypes.DuelForfeit, new { });
        await hub.TickAsync(session => session.TickCombat(DateTimeOffset.UtcNow));
        a.Inbox.Clear();
        b.Inbox.Clear();
        return hurt;
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

    private static IEnumerable<JsonElement> HealsOn(IEnumerable<string> inbox, string caster, string target) =>
        PayloadsOf(inbox, WebMessageTypes.SpellOutcome).Where(p =>
            p.GetProperty("casterId").GetString() == caster
            && p.GetProperty("targetId").GetString() == target
            && p.GetProperty("result").GetString() == WireCodes.Outcome.Heal);

    private static int? LastOwnHealth(IEnumerable<string> inbox) =>
        PayloadsOf(inbox, WebMessageTypes.PlayerValuesChanged)
            .Select(p => p.GetProperty("changed"))
            .Where(changed => changed.TryGetProperty(WireCodes.UnitValue.Health, out _))
            .Select(changed => (int?)changed.GetProperty(WireCodes.UnitValue.Health).GetInt32())
            .LastOrDefault();

    [Fact]
    public async Task AHealCastAtAFriendlyPlayerLandsOnThem_AndBothScreensShowTheSameNumber()
    {
        var (hub, a, b) = await InWorldAsync();
        var hurt = await HurtThenMakePeaceAsync(hub, a, b);

        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = InstantHeal, targetId = BId });
        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));

        var onTarget = Assert.Single(HealsOn(b.Inbox, AId, BId));
        var onCaster = Assert.Single(HealsOn(a.Inbox, AId, BId));
        var final = onTarget.GetProperty("finalHealth").GetInt32();
        Assert.Equal(hurt + onTarget.GetProperty("damage").GetInt32(), final);
        Assert.True(final > hurt);
        Assert.Equal(final, onCaster.GetProperty("finalHealth").GetInt32());
        Assert.Equal(final, LastOwnHealth(b.Inbox));
        Assert.Contains(PayloadsOf(a.Inbox, WebMessageTypes.EntityValuesChanged), p =>
            p.GetProperty("networkId").GetString() == BId
            && p.GetProperty("changed").TryGetProperty(WireCodes.UnitValue.Health, out var health)
            && health.GetInt32() == final);
        var released = Assert.Single(PayloadsOf(a.Inbox, WebMessageTypes.SpellReleased));
        Assert.Equal(BId, Assert.Single(released.GetProperty("targets").EnumerateArray()).GetString());
        Assert.Empty(HealsOn(a.Inbox, AId, AId));
    }

    [Fact]
    public async Task ACastTimeHealAtAFriendlyPlayerLandsOnThemWhenTheCastFinishes()
    {
        var (hub, a, b) = await InWorldAsync();
        var hurt = await HurtThenMakePeaceAsync(hub, a, b);

        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = CastTimeHeal, targetId = BId });
        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        var onTarget = Assert.Single(HealsOn(b.Inbox, AId, BId));
        Assert.True(onTarget.GetProperty("finalHealth").GetInt32() > hurt);
        Assert.Equal(onTarget.GetProperty("finalHealth").GetInt32(), LastOwnHealth(b.Inbox));
        Assert.Single(HealsOn(a.Inbox, AId, BId));
    }

    [Fact]
    public async Task AHealWithNobodyTargetedLandsOnTheCaster()
    {
        var (hub, a, b) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = InstantHeal, targetId = "" });
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));

        Assert.Single(HealsOn(a.Inbox, AId, AId));
        Assert.Empty(HealsOn(b.Inbox, AId, BId));
    }

    [Fact]
    public async Task AHealCastAtADuelOpponentLandsOnTheCasterInstead()
    {
        var (hub, a, b) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.DuelChallenge, new { targetId = BId });
        await hub.TickAsync(session => session.TickCombat(DateTimeOffset.UtcNow));
        await SendAsync(hub, b, WebMessageTypes.DuelAccept, new { });
        await hub.TickAsync(session => session.TickCombat(DateTimeOffset.UtcNow));
        a.Inbox.Clear();
        b.Inbox.Clear();

        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = InstantHeal, targetId = BId });
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));

        Assert.Single(HealsOn(a.Inbox, AId, AId));
        Assert.Empty(HealsOn(b.Inbox, AId, BId));
        Assert.Empty(HealsOn(a.Inbox, AId, BId));
    }

    [Fact]
    public async Task ACastTimeBuffLandsOnTheCasterWhenTheCastFinishes()
    {
        var (hub, a, _) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = CastTimeBuff, targetId = "" });
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        var applied = Assert.Single(PayloadsOf(a.Inbox, WebMessageTypes.AuraApplied));
        Assert.Equal(AId, applied.GetProperty("unitId").GetString());
    }

    private static IEnumerable<JsonElement> AurasOn(IEnumerable<string> inbox, string type, uint spellId, string unit) =>
        PayloadsOf(inbox, type).Where(p =>
            p.GetProperty("spellId").GetUInt32() == spellId && p.GetProperty("unitId").GetString() == unit);

    [Fact]
    public async Task AHealOverTimeCastAtAFriendlyPlayerTicksAndEndsOnThem()
    {
        var (hub, a, b) = await InWorldAsync();
        var hurt = await HurtThenMakePeaceAsync(hub, a, b);

        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = HealOverTime, targetId = BId });
        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        var now = DateTimeOffset.UtcNow;
        for (var second = 0; second < 12; second++)
            await hub.TickAsync(session => session.TickCombat(now + TimeSpan.FromSeconds(second)));

        var applied = Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, HealOverTime, BId));
        Assert.Equal(AId, applied.GetProperty("casterId").GetString());
        Assert.Single(AurasOn(a.Inbox, WebMessageTypes.AuraApplied, HealOverTime, BId));
        Assert.Empty(AurasOn(a.Inbox, WebMessageTypes.AuraApplied, HealOverTime, AId));
        Assert.Empty(PayloadsOf(b.Inbox, WebMessageTypes.SpellCooldownStarted));

        var ticks = PayloadsOf(b.Inbox, WebMessageTypes.SpellOutcome)
            .Where(p => p.GetProperty("targetId").GetString() == BId
                && p.GetProperty("result").GetString() == WireCodes.Outcome.PeriodicHeal)
            .ToList();
        Assert.Equal(3, ticks.Count);
        Assert.True(ticks[^1].GetProperty("finalHealth").GetInt32() > hurt);
        Assert.Equal(ticks[^1].GetProperty("finalHealth").GetInt32(), LastOwnHealth(b.Inbox));
        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraEnded, HealOverTime, BId));
        Assert.Single(AurasOn(a.Inbox, WebMessageTypes.AuraEnded, HealOverTime, BId));
    }

    [Fact]
    public async Task ABuffCastAtAFriendlyPlayerIsHeldAndExpiresOnThem()
    {
        var (hub, a, b) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = Buff, targetId = BId });
        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        var now = DateTimeOffset.UtcNow;
        for (var second = 0; second < 7; second++)
            await hub.TickAsync(session => session.TickCombat(now + TimeSpan.FromSeconds(second)));

        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, Buff, BId));
        Assert.Single(AurasOn(a.Inbox, WebMessageTypes.AuraApplied, Buff, BId));
        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraEnded, Buff, BId));
        Assert.Empty(AurasOn(a.Inbox, WebMessageTypes.AuraApplied, Buff, AId));
    }

    [Fact]
    public async Task ASpeedUpCastAtAFriendlyPlayerSpeedsThemUp()
    {
        var (hub, a, b) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = SpeedUp, targetId = BId });
        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));

        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, SpeedUp, BId));
        Assert.Contains(PayloadsOf(b.Inbox, WebMessageTypes.PlayerValuesChanged),
            p => p.GetProperty("changed").TryGetProperty(WireCodes.UnitValue.SpedUp, out var up) && up.GetInt32() == 1);
    }

    [Fact]
    public async Task AReleaseCarriesTheSpellsProjectileSpeed()
    {
        var (hub, a, _) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = FlyingHeal, targetId = BId });

        var released = Assert.Single(PayloadsOf(a.Inbox, WebMessageTypes.SpellReleased));
        Assert.Equal(20f, released.GetProperty("speed").GetSingle());
    }

    [Fact]
    public async Task AnotherPlayerSeesACastStartAndGoOff()
    {
        var (hub, a, b) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = CastTimeHeal, targetId = "" });

        var casting = Assert.Single(PayloadsOf(b.Inbox, WebMessageTypes.SpellCasting));
        Assert.Equal(AId, casting.GetProperty("casterId").GetString());
        Assert.Equal(CastTimeHeal, casting.GetProperty("spellId").GetUInt32());

        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));
        var released = Assert.Single(PayloadsOf(b.Inbox, WebMessageTypes.SpellReleased));
        Assert.Equal(AId, released.GetProperty("casterId").GetString());
    }

    [Fact]
    public async Task AnotherPlayerSeesACastInterrupted()
    {
        var (hub, a, b) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = CastTimeHeal, targetId = "" });
        await SendAsync(hub, a, WebMessageTypes.SpellAbort, new { spellId = CastTimeHeal });

        var interrupted = Assert.Single(PayloadsOf(b.Inbox, WebMessageTypes.SpellInterrupted));
        Assert.Equal(AId, interrupted.GetProperty("casterId").GetString());
    }

    [Fact]
    public async Task AnotherPlayerCanBeTargeted()
    {
        var (hub, a, _) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.TargetChange, new { networkId = BId });

        var changed = Assert.Single(PayloadsOf(a.Inbox, WebMessageTypes.TargetChanged));
        Assert.Equal(BId, changed.GetProperty("networkId").GetString());
    }

    private static CharacterRecord Character(string guid, string name) => new(
        guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, [100, 200]);
}
