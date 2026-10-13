using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class DuelTests
{
    private const string AylaId = "player:char-a";
    private const string BorinId = "player:char-b";

    private static async Task<(TestClient A, TestClient B)> DuellistsAsync()
    {
        var characters = new InMemoryCharacterStore(
        [
            Character("char-a", 1, "Ayla"),
            Character("char-b", 1, "Borin"),
        ]);
        var duels = new DuelService(countdown: TimeSpan.Zero);
        var blows = new PlayerBlowRelay();
        var hub = new PlaySessionHub();
        var a = Join(hub, characters, duels, blows);
        var b = Join(hub, characters, duels, blows);
        await a.EnterAsync("Ayla");
        await b.EnterAsync("Borin");
        return (a, b);
    }

    private static TestClient Join(PlaySessionHub hub, InMemoryCharacterStore characters, DuelService duels, PlayerBlowRelay? blows = null)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters,
            combatRollUnit: () => 1.0, duels: duels, blows: blows);
        hub.Register(session, _ => Task.CompletedTask);
        return new TestClient(session);
    }

    private static CharacterRecord Character(string guid, int accountId, string name) => new(
        guid, accountId, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, [100, 200]);

    [Fact]
    public async Task RequestAcceptAndFight_LandsDamageOnTheOpponentPlayer()
    {
        var (a, b) = await DuellistsAsync();

        await a.SendAsync(WebMessageTypes.DuelChallenge, new { targetId = BorinId });
        Assert.True(b.Tick(DateTimeOffset.UtcNow).Has(WebMessageTypes.DuelChallenged, AylaId));

        var accepted = await b.SendAsync(WebMessageTypes.DuelAccept);
        Assert.True(accepted.Has(WebMessageTypes.DuelStarted, AylaId));
        Assert.True(a.Tick(DateTimeOffset.UtcNow).Has(WebMessageTypes.DuelStarted, BorinId));

        Assert.True((await a.SendAsync(WebMessageTypes.AttackEngage, new { targetId = BorinId }))
            .Has(WebMessageTypes.AttackMeleeStart));
        Assert.True((await b.SendAsync(WebMessageTypes.AttackEngage, new { targetId = AylaId }))
            .Has(WebMessageTypes.AttackMeleeStart));

        var now = DateTimeOffset.UtcNow + CombatService.SwingInterval;
        var onA = a.Tick(now);
        var onB = b.Tick(now);
        foreach (var offset in new[] { 700, 1400 })
        {
            onA = onA.Plus(a.Tick(now + TimeSpan.FromMilliseconds(offset)));
            onB = onB.Plus(b.Tick(now + TimeSpan.FromMilliseconds(offset)));
        }

        Assert.Contains(onB.All(WebMessageTypes.AttackSwingResult),
            m => m.Text("attackerId") == AylaId && m.Text("targetId") == BorinId);
        Assert.Contains(onA.All(WebMessageTypes.AttackSwingResult),
            m => m.Text("attackerId") == BorinId && m.Text("targetId") == AylaId);
        Assert.True(onA.Has(WebMessageTypes.PlayerValuesChanged));
        Assert.True(onB.Has(WebMessageTypes.PlayerValuesChanged));
    }

    [Fact]
    public async Task Decline_RefusesTheDuelAndNotifiesTheRequester()
    {
        var (a, b) = await DuellistsAsync();

        await a.SendAsync(WebMessageTypes.DuelChallenge, new { targetId = BorinId });
        await b.SendAsync(WebMessageTypes.DuelDecline);

        Assert.True(a.Tick(DateTimeOffset.UtcNow).Has(WebMessageTypes.DuelDeclined, BorinId));

        var attack = await a.SendAsync(WebMessageTypes.AttackEngage, new { targetId = BorinId });
        Assert.Equal(WireCodes.ProtocolError.InvalidTarget, attack.Single(WebMessageTypes.ProtocolError).Text("code"));
    }

    [Fact]
    public async Task ANonParticipant_CannotBeAttackedThroughSomeoneElsesDuel()
    {
        var characters = new InMemoryCharacterStore(
        [
            Character("char-a", 1, "Ayla"),
            Character("char-b", 1, "Borin"),
            Character("char-c", 1, "Cyra"),
        ]);
        var duels = new DuelService(countdown: TimeSpan.Zero);
        var hub = new PlaySessionHub();
        var a = Join(hub, characters, duels);
        var b = Join(hub, characters, duels);
        var c = Join(hub, characters, duels);
        await a.EnterAsync("Ayla");
        await b.EnterAsync("Borin");
        await c.EnterAsync("Cyra");

        await a.SendAsync(WebMessageTypes.DuelChallenge, new { targetId = BorinId });
        await b.SendAsync(WebMessageTypes.DuelAccept);

        var aAttacksC = await a.SendAsync(WebMessageTypes.AttackEngage, new { targetId = "player:char-c" });
        Assert.Equal(WireCodes.ProtocolError.InvalidTarget, aAttacksC.Single(WebMessageTypes.ProtocolError).Text("code"));

        var cAttacksA = await c.SendAsync(WebMessageTypes.AttackEngage, new { targetId = AylaId });
        Assert.Equal(WireCodes.ProtocolError.InvalidTarget, cAttacksA.Single(WebMessageTypes.ProtocolError).Text("code"));
    }

    [Fact]
    public async Task Forfeit_EndsTheDuelImmediatelyAndLeavesHealthUntouched()
    {
        var (a, b) = await DuellistsAsync();

        await a.SendAsync(WebMessageTypes.DuelChallenge, new { targetId = BorinId });
        await b.SendAsync(WebMessageTypes.DuelAccept);

        var forfeit = await a.SendAsync(WebMessageTypes.DuelForfeit);
        Assert.Equal("forfeited", forfeit.Single(WebMessageTypes.DuelEnded).Text("reason"));

        var opponent = b.Tick(DateTimeOffset.UtcNow);
        Assert.Equal("opponentForfeited", opponent.Single(WebMessageTypes.DuelEnded).Text("reason"));

        var attack = await a.SendAsync(WebMessageTypes.AttackEngage, new { targetId = BorinId });
        Assert.Equal(WireCodes.ProtocolError.InvalidTarget, attack.Single(WebMessageTypes.ProtocolError).Text("code"));
    }

    [Fact]
    public async Task LosingADuelRevivesTheLoserAtAFlatAmountInsteadOfLeavingThemDead()
    {
        var (a, b) = await DuellistsAsync();

        await a.SendAsync(WebMessageTypes.DuelChallenge, new { targetId = BorinId });
        await b.SendAsync(WebMessageTypes.DuelAccept);
        await a.SendAsync(WebMessageTypes.AttackEngage, new { targetId = BorinId });

        var now = DateTimeOffset.UtcNow + CombatService.SwingInterval;
        var log = new Received([]);
        for (var swing = 0; swing < 60 && !log.Has(WebMessageTypes.DuelEnded); swing++)
        {
            a.Tick(now);
            log = log.Plus(b.Tick(now));
            now += CombatService.SwingInterval;
        }

        var ended = log.Single(WebMessageTypes.DuelEnded);
        Assert.Equal("lost", ended.Text("reason"));
        Assert.True(ended.Mentions("\"health\":10"));

        var attackAfterLoss = await b.SendAsync(WebMessageTypes.AttackEngage, new { targetId = "Creature:1" });
        Assert.DoesNotContain(attackAfterLoss.Messages, m => m.Mentions("You are dead"));
    }
}
