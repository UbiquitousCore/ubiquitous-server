namespace Ubiquitous.Game.Tests;

public sealed class ThreatSwitchTests
{
    private const string Mob = "Creature:1";
    private const string Holder = "player:char-a";
    private const string Challenger = "player:char-b";

    private static (CombatService Combat, LocalHostilityLog Log) HeldBy(double holderHostility)
    {
        var log = new LocalHostilityLog();
        var combat = new CombatService();
        combat.UseHostilityLog(log);
        log.AddFighter(Mob, Holder);
        log.AddFighter(Mob, Challenger);
        log.Add(Mob, Holder, holderHostility);
        Assert.Equal(Holder, combat.RetaliationTarget(Mob));
        return (combat, log);
    }

    [Fact]
    public void RetaliationTarget_KeepsItsVictimWhileAChallengerIsWithinTheMargin()
    {
        var (combat, log) = HeldBy(100);

        log.Add(Mob, Challenger, 100 * (1 + CombatService.ThreatSwitchMargin) - 1);

        Assert.Equal(Holder, combat.RetaliationTarget(Mob));
    }

    [Fact]
    public void RetaliationTarget_SwitchesOnceAChallengerPassesTheMargin()
    {
        var (combat, log) = HeldBy(100);

        log.Add(Mob, Challenger, 100 * (1 + CombatService.ThreatSwitchMargin) + 1);

        Assert.Equal(Challenger, combat.RetaliationTarget(Mob));
    }
}
