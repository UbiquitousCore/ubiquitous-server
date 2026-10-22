namespace Ubiquitous.Scripts.Tests;

public sealed class InmateScriptTests
{
    private static CombatTurnContext Turn(
        double health = 1.0,
        double secondsInFight = 60,
        bool inMelee = true,
        IDictionary<uint, int>? castCount = null,
        IDictionary<uint, double>? secondsSinceCast = null,
        IReadOnlyCollection<uint>? allies = null)
    {
        var since = new Dictionary<uint, TimeSpan>();
        foreach (var (spellId, seconds) in secondsSinceCast ?? new Dictionary<uint, double>())
            since[spellId] = TimeSpan.FromSeconds(seconds);

        return new CombatTurnContext(
            Creature: null,
            HealthFraction: health,
            TimeInFight: TimeSpan.FromSeconds(secondsInFight),
            InMeleeRange: inMelee,
            SinceCast: since,
            CastCount: new Dictionary<uint, int>(castCount ?? new Dictionary<uint, int>()),
            SummonsInReach: 0,
            Marks: new HashSet<string>(StringComparer.Ordinal),
            AlliesInReach: allies);
    }

    [Fact]
    public void TheOrcPrisoner_LustsForBloodOnceForEachStepItsHealthFallsThrough()
    {
        var orc = PrisonInmates.ViciousOrcPrisoner;

        Assert.Null(orc.Act(Turn(health: 0.9)));
        Assert.Equal(2650u, orc.Act(Turn(health: 0.79))!.Cast);
        Assert.Null(orc.Act(Turn(health: 0.7, castCount: new Dictionary<uint, int> { [2650] = 1 })));
        Assert.Equal(2650u, orc.Act(Turn(health: 0.3, castCount: new Dictionary<uint, int> { [2650] = 1 }))!.Cast);
        Assert.Null(orc.Act(Turn(health: 0.1, castCount: new Dictionary<uint, int> { [2650] = 4 })));
    }

    [Fact]
    public void TheViciousHumanPrisoner_DrainsAnOrcBesideItOnlyOnceItIsHurt_AndThenHealsItself()
    {
        var human = PrisonInmates.ViciousHumanPrisoner;
        uint[] orcNearby = [2676];

        Assert.NotEqual(2666u, human.Act(Turn(health: 0.9, allies: orcNearby))?.Cast);
        Assert.NotEqual(2666u, human.Act(Turn(health: 0.4))?.Cast);

        var drain = human.Act(Turn(health: 0.4, allies: orcNearby))!;
        Assert.Equal(2666u, drain.Cast);
        Assert.Equal(CastTarget.Ally, drain.At);
        Assert.Equal(2676u, drain.AllyEntry);

        var heal = human.Act(Turn(
            health: 0.4, allies: orcNearby,
            castCount: new Dictionary<uint, int> { [2666] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [2666] = 1 }))!;
        Assert.Equal(2667u, heal.Cast);
        Assert.Equal(CastTarget.Self, heal.At);
    }

    [Fact]
    public void TheFleshGolem_HealsItselfAfterEverySiphon()
    {
        var golem = PrisonInmates.FleshGolem;

        var siphon = golem.Act(Turn(secondsInFight: 5))!;
        Assert.Equal(2653u, siphon.Cast);

        var heal = golem.Act(Turn(
            castCount: new Dictionary<uint, int> { [2653] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [2653] = 0.5 }))!;
        Assert.Equal(2655u, heal.Cast);
    }

    [Fact]
    public void TheHound_BitesOnlyWhatItCanReach()
    {
        Assert.Null(PrisonInmates.RottenJailHound.Act(Turn(inMelee: false)));
        Assert.Equal(2641u, PrisonInmates.RottenJailHound.Act(Turn())!.Cast);
    }

    [Fact]
    public void TheHumanPrisoner_ThrowsWhoeverItLikes()
    {
        var throwing = PrisonInmates.HumanPrisoner.Act(Turn())!;

        Assert.Equal(2659u, throwing.Cast);
        Assert.Equal(CastTarget.AnyEnemy, throwing.At);
    }

    [Fact]
    public void TheCapo_EnticesOnlyOnceAsTheFightOpens()
    {
        var capo = PrisonInmates.HumanPrisonerCapo;

        Assert.Equal(2663u, capo.Act(Turn(secondsInFight: 0))!.Cast);
        Assert.NotEqual(2663u, capo.Act(Turn(castCount: new Dictionary<uint, int> { [2663] = 1 }))?.Cast);
    }

    [Fact]
    public void ABoneGolem_BreaksIntoTwoLesserGolems()
    {
        Assert.Equal([2692u, 2692u], PrisonInmates.BoneGolem.DiesInto);
    }

    [Fact]
    public void EveryPrisonInmateBoundInContent_HasAScriptInTheCatalogue()
    {
        var bound = Ubiquitous.Game.GameContent.Development.CreatureScripts
            .Where(pair => pair.Value.StartsWith("inmate.", StringComparison.Ordinal));

        Assert.All(bound, pair => Assert.True(ScriptRegistry.Catalogue.ContainsKey(pair.Value), pair.Value));
        Assert.Equal(PrisonInmates.All.Count, bound.Count());
    }
}
