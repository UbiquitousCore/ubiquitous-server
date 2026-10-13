using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PvpFlagTests
{
    private static (PlaySession A, PlaySession B) FlaggingSessionsAsync(
        Func<double>? combatRollUnitA = null, Func<double>? combatRollUnitB = null, PvpFlagService? pvpFlags = null)
    {
        var characters = new InMemoryCharacterStore(
        [
            Character("char-a", 1, "Ayla"),
            Character("char-b", 1, "Borin"),
        ]);
        var flags = pvpFlags ?? new PvpFlagService();
        var blows = new PlayerBlowRelay();
        var sessionA = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters,
            combatRollUnit: combatRollUnitA ?? (() => 1.0), pvpFlags: flags, blows: blows);
        var sessionB = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters,
            combatRollUnit: combatRollUnitB ?? (() => 1.0), pvpFlags: flags, blows: blows);
        var hub = new PlaySessionHub();
        hub.Register(sessionA, _ => Task.CompletedTask);
        hub.Register(sessionB, _ => Task.CompletedTask);
        return (sessionA, sessionB);
    }

    private static async Task EnterWorldAsync(PlaySession session, string characterName)
    {
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(characterName))));
    }

    private static CharacterRecord Character(string guid, int accountId, string name) => new(
        guid, accountId, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, [100, 200]);

    [Fact]
    public async Task TwoFlaggedPlayers_CanFightEachOther()
    {
        var (sessionA, sessionB) = FlaggingSessionsAsync();
        await EnterWorldAsync(sessionA, "Ayla");
        await EnterWorldAsync(sessionB, "Borin");
        var aId = "player:char-a";
        var bId = "player:char-b";

        var flagA = await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PvpFlagSet, 2, new { flagged = true })));
        Assert.Contains(flagA, m => m.Contains("\"type\":\"pvp.flagChanged\"") && m.Contains("\"flagged\":true"));

        var flagB = await sessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PvpFlagSet, 2, new { flagged = true })));
        Assert.Contains(flagB, m => m.Contains("\"type\":\"pvp.flagChanged\"") && m.Contains("\"flagged\":true"));

        var attackA = await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = bId })));
        Assert.Contains(attackA, m => m.Contains("\"type\":\"attack.meleeStart\""));

        var attackB = await sessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = aId })));
        Assert.Contains(attackB, m => m.Contains("\"type\":\"attack.meleeStart\""));

        var now = DateTimeOffset.UtcNow + CombatService.SwingInterval;
        var onA = sessionA.TickCombat(now).ToList();
        var onB = sessionB.TickCombat(now).ToList();
        onA.AddRange(sessionA.TickCombat(now + TimeSpan.FromMilliseconds(500)));
        onB.AddRange(sessionB.TickCombat(now + TimeSpan.FromMilliseconds(500)));
        Assert.Contains(onB, m => m.Contains("\"type\":\"attack.swingResult\"")
            && m.Contains($"\"attackerId\":\"{aId}\"") && m.Contains($"\"targetId\":\"{bId}\""));
        Assert.Contains(onA, m => m.Contains("\"type\":\"attack.swingResult\"")
            && m.Contains($"\"attackerId\":\"{bId}\"") && m.Contains($"\"targetId\":\"{aId}\""));
    }

    [Fact]
    public async Task AnUnflaggedPlayer_CannotBeAttackedByAFlaggedOne()
    {
        var (sessionA, sessionB) = FlaggingSessionsAsync();
        await EnterWorldAsync(sessionA, "Ayla");
        await EnterWorldAsync(sessionB, "Borin");

        await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PvpFlagSet, 2, new { flagged = true })));

        var attackA = await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "player:char-b" })));
        Assert.Contains("invalid_target", attackA.Single());

        var attackB = await sessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "player:char-a" })));
        Assert.Contains("invalid_target", attackB.Single());
    }

    [Fact]
    public async Task Unflagging_StopsCombatEligibilityForANewAttack()
    {
        var (sessionA, sessionB) = FlaggingSessionsAsync();
        await EnterWorldAsync(sessionA, "Ayla");
        await EnterWorldAsync(sessionB, "Borin");
        var bId = "player:char-b";

        await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PvpFlagSet, 2, new { flagged = true })));
        await sessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PvpFlagSet, 2, new { flagged = true })));

        var attackBefore = await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = bId })));
        Assert.Contains(attackBefore, m => m.Contains("\"type\":\"attack.meleeStart\""));

        var unflag = await sessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PvpFlagSet, 4, new { flagged = false })));
        Assert.Contains(unflag, m => m.Contains("\"type\":\"pvp.flagChanged\"") && m.Contains("\"flagged\":false"));

        var attackAfter = await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 6, new { targetId = bId })));
        Assert.Contains("invalid_target", attackAfter.Single());
    }

    [Fact]
    public async Task ANonParticipant_CannotBeAttackedThroughSomeoneElsesFlag()
    {
        var characters = new InMemoryCharacterStore(
        [
            Character("char-a", 1, "Ayla"),
            Character("char-b", 1, "Borin"),
            Character("char-c", 1, "Cyra"),
        ]);
        var flags = new PvpFlagService();
        var sessionA = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters,
            combatRollUnit: () => 1.0, pvpFlags: flags);
        var sessionC = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters,
            combatRollUnit: () => 1.0, pvpFlags: flags);
        await EnterWorldAsync(sessionA, "Ayla");
        await EnterWorldAsync(sessionC, "Cyra");

        await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PvpFlagSet, 2, new { flagged = true })));

        var aAttacksC = await sessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "player:char-c" })));
        Assert.Contains("invalid_target", aAttacksC.Single());
    }
}
