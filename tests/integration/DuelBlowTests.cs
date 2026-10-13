using System.Text.Json;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class DuelBlowTests
{
    private const string AId = "player:char-a";
    private const string BId = "player:char-b";

    private sealed record Side(PlaySession Session, List<string> Inbox);

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
                combatRollUnit: () => 1.0, spellRollUnit: () => 0.5, duels: duels, pvpFlags: flags, blows: blows);
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

    private static IEnumerable<JsonElement> BlowsOn(IEnumerable<string> inbox, string type, string attackerKey, string attacker, string target) =>
        PayloadsOf(inbox, type).Where(p =>
            p.GetProperty(attackerKey).GetString() == attacker && p.GetProperty("targetId").GetString() == target);

    private static int? LastOwnHealth(IEnumerable<string> inbox) =>
        PayloadsOf(inbox, WebMessageTypes.PlayerValuesChanged)
            .Select(p => p.GetProperty("changed"))
            .Where(changed => changed.TryGetProperty(WireCodes.UnitValue.Health, out _))
            .Select(changed => (int?)changed.GetProperty(WireCodes.UnitValue.Health).GetInt32())
            .LastOrDefault();

    [Fact]
    public async Task ASwingComesOffTheHealthOfThePlayerItHit_AndBothScreensShowTheSameNumber()
    {
        var (hub, a, b) = await DuellingAsync();
        await SendAsync(hub, a, WebMessageTypes.AttackEngage, new { targetId = BId });
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        await TickBothAsync(hub, now);

        var onVictim = Assert.Single(BlowsOn(b.Inbox, WebMessageTypes.AttackSwingResult, "attackerId", AId, BId));
        var onAttacker = Assert.Single(BlowsOn(a.Inbox, WebMessageTypes.AttackSwingResult, "attackerId", AId, BId));
        var damage = onVictim.GetProperty("damage").GetInt32();
        var final = onVictim.GetProperty("finalHealth").GetInt32();
        Assert.True(damage > 0);
        Assert.Equal(damage, onAttacker.GetProperty("damage").GetInt32());
        Assert.Equal(final, onAttacker.GetProperty("finalHealth").GetInt32());

        Assert.Equal(final, LastOwnHealth(b.Inbox));
        Assert.Contains(PayloadsOf(a.Inbox, WebMessageTypes.EntityValuesChanged), p =>
            p.GetProperty("networkId").GetString() == BId
            && p.GetProperty("changed").TryGetProperty(WireCodes.UnitValue.Health, out var health)
            && health.GetInt32() == final);

        Assert.Empty(BlowsOn(a.Inbox, WebMessageTypes.AttackSwingResult, "attackerId", BId, AId));
        Assert.Empty(BlowsOn(b.Inbox, WebMessageTypes.AttackSwingResult, "attackerId", BId, AId));
        Assert.Null(LastOwnHealth(a.Inbox));
    }

    [Fact]
    public async Task TheSideThatRunsOutOfHealthLoses_AndTheOtherSideWins()
    {
        var (hub, a, b) = await DuellingAsync();
        await SendAsync(hub, a, WebMessageTypes.AttackEngage, new { targetId = BId });
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        for (var pass = 0; pass < 100 && !PayloadsOf(b.Inbox, WebMessageTypes.DuelEnded).Any(); pass++)
        {
            await TickBothAsync(hub, now);
            now += CombatService.SwingInterval;
        }
        await TickBothAsync(hub, now);

        var lost = Assert.Single(PayloadsOf(b.Inbox, WebMessageTypes.DuelEnded));
        Assert.Equal("lost", lost.GetProperty("reason").GetString());
        Assert.Equal(10, lost.GetProperty("health").GetInt32());
        var won = Assert.Single(PayloadsOf(a.Inbox, WebMessageTypes.DuelEnded));
        Assert.Equal("won", won.GetProperty("reason").GetString());

        var finals = BlowsOn(b.Inbox, WebMessageTypes.AttackSwingResult, "attackerId", AId, BId)
            .Select(p => p.GetProperty("finalHealth").GetInt32()).ToList();
        Assert.Equal(1, finals[^1]);
        Assert.DoesNotContain(0, finals);
        Assert.Empty(PayloadsOf(b.Inbox, WebMessageTypes.UnitDied));

        var endedAt = b.Inbox.Count;
        await TickBothAsync(hub, now + CombatService.SwingInterval);
        await TickBothAsync(hub, now + CombatService.SwingInterval * 2);
        Assert.Empty(BlowsOn(b.Inbox.Skip(endedAt), WebMessageTypes.AttackSwingResult, "attackerId", AId, BId));
        var attackAgain = await a.Session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 9, new { targetId = BId })));
        Assert.Contains("invalid_target", string.Concat(attackAgain));
    }

    [Fact]
    public async Task BothSidesTradingBlows_EachLosesOnlyWhatTheOtherLanded()
    {
        var (hub, a, b) = await DuellingAsync();
        await SendAsync(hub, a, WebMessageTypes.AttackEngage, new { targetId = BId });
        await SendAsync(hub, b, WebMessageTypes.AttackEngage, new { targetId = AId });
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        await TickBothAsync(hub, now);
        await TickBothAsync(hub, now + CombatService.SwingInterval);

        var onB = BlowsOn(b.Inbox, WebMessageTypes.AttackSwingResult, "attackerId", AId, BId).ToList();
        var onA = BlowsOn(a.Inbox, WebMessageTypes.AttackSwingResult, "attackerId", BId, AId).ToList();
        Assert.Equal(2, onB.Count);
        Assert.Equal(2, onA.Count);
        Assert.Equal(onB[^1].GetProperty("finalHealth").GetInt32(), LastOwnHealth(b.Inbox));
        Assert.Equal(onA[^1].GetProperty("finalHealth").GetInt32(), LastOwnHealth(a.Inbox));
        var bFinal = onB[^1].GetProperty("finalHealth").GetInt32();
        var bFirst = onB[0].GetProperty("finalHealth").GetInt32();
        Assert.Equal(bFirst - onB[^1].GetProperty("damage").GetInt32(), bFinal);
    }

    private static Task MoveAsync(PlaySessionHub hub, Side side, double x) =>
        SendAsync(hub, side, WebMessageTypes.PlayerMove, new PlayerMovePayload(x, 2, 3, 0, true, MovementState.Walking));

    [Fact]
    public async Task AnAttackOnADuelOpponentOutOfMeleeReach_IsRefusedTheWayOneOnACreatureIs()
    {
        var (hub, a, b) = await DuellingAsync();
        await MoveAsync(hub, b, 12);
        a.Inbox.Clear();

        await SendAsync(hub, a, WebMessageTypes.AttackEngage, new { targetId = BId });

        var refusal = Assert.Single(PayloadsOf(a.Inbox, WebMessageTypes.ProtocolError));
        Assert.Equal("invalid_target", refusal.GetProperty("code").GetString());
        Assert.Equal("Attack target is out of range.", refusal.GetProperty("reason").GetString());
        Assert.Empty(PayloadsOf(a.Inbox, WebMessageTypes.AttackMeleeStart));
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));
        Assert.Empty(BlowsOn(b.Inbox, WebMessageTypes.AttackSwingResult, "attackerId", AId, BId));
    }

    [Fact]
    public async Task ADuelOpponentWhoWalksOutOfReach_IsNotHit_UntilTheyComeBack()
    {
        var (hub, a, b) = await DuellingAsync();
        await SendAsync(hub, a, WebMessageTypes.AttackEngage, new { targetId = BId });
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        await TickBothAsync(hub, now);
        Assert.Single(BlowsOn(b.Inbox, WebMessageTypes.AttackSwingResult, "attackerId", AId, BId));

        await MoveAsync(hub, b, 12);
        var walkedAway = b.Inbox.Count;
        for (var pass = 1; pass <= 4; pass++)
            await TickBothAsync(hub, now + CombatService.SwingInterval * pass);
        Assert.Empty(BlowsOn(b.Inbox.Skip(walkedAway), WebMessageTypes.AttackSwingResult, "attackerId", AId, BId));

        await MoveAsync(hub, b, 2);
        var cameBack = b.Inbox.Count;
        await TickBothAsync(hub, now + CombatService.SwingInterval * 5);
        Assert.Single(BlowsOn(b.Inbox.Skip(cameBack), WebMessageTypes.AttackSwingResult, "attackerId", AId, BId));
    }

    [Fact]
    public async Task ASpellCastAtTheDuelOpponentLandsOnThem()
    {
        var (hub, a, b) = await DuellingAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = 5815u, targetId = BId });
        Assert.DoesNotContain(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed), _ => true);
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));

        var onVictim = Assert.Single(BlowsOn(b.Inbox, WebMessageTypes.SpellOutcome, "casterId", AId, BId));
        var onCaster = Assert.Single(BlowsOn(a.Inbox, WebMessageTypes.SpellOutcome, "casterId", AId, BId));
        var final = onVictim.GetProperty("finalHealth").GetInt32();
        Assert.True(onVictim.GetProperty("damage").GetInt32() > 0);
        Assert.Equal(final, onCaster.GetProperty("finalHealth").GetInt32());
        Assert.Equal(final, LastOwnHealth(b.Inbox));
    }

    [Fact]
    public async Task ACastTimeSpellAtTheDuelOpponentLandsWhenTheCastFinishes()
    {
        var (hub, a, b) = await DuellingAsync();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = 100u, targetId = BId });
        Assert.DoesNotContain(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed), _ => true);
        await TickBothAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));

        var onVictim = Assert.Single(BlowsOn(b.Inbox, WebMessageTypes.SpellOutcome, "casterId", AId, BId));
        Assert.Equal(20, onVictim.GetProperty("damage").GetInt32());
        Assert.Equal(onVictim.GetProperty("finalHealth").GetInt32(), LastOwnHealth(b.Inbox));
        Assert.Single(BlowsOn(a.Inbox, WebMessageTypes.SpellOutcome, "casterId", AId, BId));
    }

    [Fact]
    public async Task ASpellCannotBeCastAtAPlayerOutsideADuel()
    {
        var (hub, a, _) = await DuellingAsync();
        await SendAsync(hub, a, WebMessageTypes.DuelForfeit, new { });
        a.Inbox.Clear();
        await SendAsync(hub, a, WebMessageTypes.SpellRequest, new { spellId = 5815u, targetId = BId });
        Assert.Single(PayloadsOf(a.Inbox, WebMessageTypes.SpellFailed));
    }

    [Fact]
    public async Task AFlaggedPlayerKilledByAnother_Dies_AndTheKillerStopsSwinging()
    {
        var (hub, a, b) = await InWorldAsync();
        await SendAsync(hub, a, WebMessageTypes.PvpFlagSet, new { flagged = true });
        await SendAsync(hub, b, WebMessageTypes.PvpFlagSet, new { flagged = true });
        await SendAsync(hub, a, WebMessageTypes.AttackEngage, new { targetId = BId });
        var now = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10);
        for (var pass = 0; pass < 100 && !PayloadsOf(b.Inbox, WebMessageTypes.UnitDied).Any(); pass++)
        {
            await TickBothAsync(hub, now);
            now += CombatService.SwingInterval;
        }

        Assert.Single(PayloadsOf(b.Inbox, WebMessageTypes.PlayerDeath));
        Assert.Contains(PayloadsOf(a.Inbox, WebMessageTypes.UnitDied), p => p.GetProperty("networkId").GetString() == BId);
        await TickBothAsync(hub, now);
        Assert.Contains(PayloadsOf(a.Inbox, WebMessageTypes.AttackHalted), p => p.GetProperty("networkId").GetString() == AId);
        Assert.False(a.Session.NeedsFastTick);
    }

    private static CharacterRecord Character(string guid, string name) => new(
        guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, [100, 200]);
}
