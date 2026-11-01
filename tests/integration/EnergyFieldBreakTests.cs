using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class EnergyFieldBreakTests
{
    private const string AId = "player:char-a";
    private const string BId = "player:char-b";
    private const uint Shield = 7400;
    private const uint Flashover = 7401;
    private const uint LegacyShield = 7402;
    private const uint Poison = 7403;

    private sealed record Side(PlaySession Session, List<string> Inbox);

    private static readonly GameContent Content = GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [Flashover] = new(Flashover, "spell-7401", 0, 0, 0f, PowerCost: 0, DurationMs: 8000,
                AuraAttributeId: AttributeIds.SpellPower, AuraAttributeAmount: -40, AuraAttributeIsPercent: true,
                EffectTarget: SpellEffectTargets.SingleEnemy),
            [Shield] = new(Shield, "spell-7400", 0, 0, 0f, PowerCost: 0, DurationMs: 300000,
                AuraAbsorbAmount: 15, AuraAbsorbPercent: 100,
                AuraBreakTriggerSpellId: Flashover, AuraBreakTriggerChancePercent: 100),
            [LegacyShield] = new(LegacyShield, "spell-7402", 0, 0, 0f, PowerCost: 0, DurationMs: 300000,
                AuraAbsorbAmount: 15, AuraAbsorbPercent: 100,
                ExtraEffects:
                [
                    new(LegacyShield, "spell-7402", 0, 0, 0f, DurationMs: 300000,
                        AuraTriggerSpellId: Flashover, AuraTriggerChancePercent: 100,
                        EffectTarget: SpellEffectTargets.Self),
                ]),
            [Poison] = new(Poison, "spell-7403", 0, 0, 30f, PowerCost: 0, AuraPeriodicDamage: 10),
        },
        StartingSpells = [.. GameContent.Development.StartingSpells, Shield, LegacyShield, Flashover, Poison],
    };

    private static async Task<(PlaySessionHub Hub, Side A, Side B)> DuellingAsync()
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

    private static CharacterRecord Character(string guid, string name) => new(
        guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, [100, 200]);

    [Fact]
    public async Task CastingAShieldDoesNotApplyItsBreakPenalty()
    {
        var (hub, _, b) = await DuellingAsync();
        await CastAsync(hub, b, Shield, "");
        await TickAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));

        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, Shield));
        Assert.Empty(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, Flashover));
    }

    [Fact]
    public async Task BreakingAShieldAppliesItsBreakPenalty()
    {
        var (hub, a, b) = await DuellingAsync();
        await CastAsync(hub, b, Shield, "");
        await CastAsync(hub, a, Poison, BId);
        await TickEverySecondAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10), 10);

        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraEnded, BId, Shield));
        var penalty = Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, Flashover));
        Assert.Equal(1, penalty.GetProperty("harmful").GetInt32());
    }

    [Fact]
    public async Task AShieldThatExpiresWithoutBreakingAppliesNoPenalty()
    {
        var (hub, _, b) = await DuellingAsync();
        await CastAsync(hub, b, Shield, "");
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        await TickAsync(hub, now);

        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, Shield));
        await TickAsync(hub, now + TimeSpan.FromMilliseconds(300000 + 100));
        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraEnded, BId, Shield));
        Assert.Empty(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, Flashover));
    }

    [Fact]
    public async Task ALegacyShieldExtraTriggerAlsoWaitsForTheBreak()
    {
        var (hub, a, b) = await DuellingAsync();
        await CastAsync(hub, b, LegacyShield, "");
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        await TickAsync(hub, now);

        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, LegacyShield));
        Assert.Empty(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, Flashover));

        await CastAsync(hub, a, Poison, BId);
        await TickEverySecondAsync(hub, now, 10);

        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraEnded, BId, LegacyShield));
        Assert.Single(AurasOn(b.Inbox, WebMessageTypes.AuraApplied, BId, Flashover));
    }
}
