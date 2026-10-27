namespace Ubiquitous.Scripts.Tests;

public sealed class BossScriptTests
{
    private static CombatTurnContext Turn(
        double health = 1.0,
        double secondsInFight = 60,
        bool inMelee = true,
        int summonsInReach = 0,
        IEnumerable<string>? marks = null,
        IDictionary<uint, double>? secondsSinceCast = null,
        IDictionary<uint, int>? castCount = null)
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
            SummonsInReach: summonsInReach,
            Marks: new HashSet<string>(marks ?? [], StringComparer.Ordinal));
    }

    [Fact]
    public void TheExecutioner_OpensOnItsArmourAndThenAlternates()
    {
        var script = ExecutionerScript.Instance;

        var first = script.Act(Turn());
        Assert.NotNull(first!.Cast);
        Assert.Equal(CastTarget.Self, first.At);
        var armour = first.Cast!.Value;

        var second = script.Act(Turn(
            castCount: new Dictionary<uint, int> { [armour] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [armour] = 60 }));

        Assert.NotNull(second!.Cast);
        Assert.NotEqual(armour, second.Cast!.Value);
        Assert.Null(first.Yell);
        Assert.Equal("Cyclone Slash!", second.Yell);
    }

    [Fact]
    public void TheExecutioner_HoldsItsSteadyDamageWhileItIsCovered()
    {
        var script = ExecutionerScript.Instance;
        var opener = script.Act(Turn())!;
        var cover = opener.Sets!;

        var covered = script.Act(Turn(
            marks: [cover],
            castCount: new Dictionary<uint, int> { [opener.Cast!.Value] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [opener.Cast!.Value] = 1 }));

        Assert.Null(covered);
    }

    [Fact]
    public void TheExecutioner_ReapsOnceTheCoverHasWornOff()
    {
        var script = ExecutionerScript.Instance;

        var reap = script.Act(Turn(
            castCount: new Dictionary<uint, int> { [2674] = 1, [2675] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [2674] = 5, [2675] = 5 }));

        Assert.NotNull(reap!.Cast);
        Assert.Equal(CastTarget.Enemy, reap.At);
    }

    [Fact]
    public void TheExecutioner_ReapsOnlyWithinReach()
    {
        var script = ExecutionerScript.Instance;
        var outOfReach = script.Act(Turn(
            inMelee: false,
            castCount: new Dictionary<uint, int> { [2674] = 1, [2675] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [2674] = 5, [2675] = 5 }));

        Assert.Null(outOfReach);
    }

    private static Dictionary<uint, int> ArielRotting() => new() { [2651] = 1 };

    [Fact]
    public void Ariel_OpensByRottingTheWholePartyAndOnlyOnce()
    {
        var opener = ArielScript.Instance.Act(Turn(secondsInFight: 0.5))!;
        Assert.Equal(2651u, opener.Cast);
        Assert.Equal(CastTarget.Self, opener.At);

        Assert.Null(ArielScript.Instance.Act(Turn(secondsInFight: 0.5, castCount: ArielRotting())));
    }

    [Fact]
    public void Ariel_RootsSomebodyOnceTheFightHasSettled()
    {
        var root = ArielScript.Instance.Act(Turn(secondsInFight: 30, castCount: ArielRotting()))!;

        Assert.NotNull(root.Cast);
        Assert.NotEqual(2651u, root.Cast);
        Assert.Equal(CastTarget.AnyEnemy, root.At);
    }

    [Fact]
    public void Ariel_EndsHerRotAsTheLongCastBegins_AndDetonatesHerselfOnceItHasLanded()
    {
        var script = ArielScript.Instance;
        var starts = script.Act(Turn(health: 0.05, castCount: ArielRotting()))!;
        Assert.Equal(2651u, starts.Ends);
        var ending = starts.Sets!;

        var detonation = script.Act(Turn(health: 0.05, marks: [ending], castCount: ArielRotting()))!;
        Assert.Equal(2657u, detonation.Cast);
        Assert.True(detonation.HoldsStill, "she neither swings nor walks through the long cast");

        var herself = script.Act(Turn(
            health: 0.05, marks: [ending],
            castCount: new Dictionary<uint, int> { [2651] = 1, [2657] = 1 }))!;
        Assert.Equal(2797u, herself.Cast);
        Assert.Equal(CastTarget.Self, herself.At);
    }

    [Fact]
    public void Ariel_StartsHerEndingOnceAndThenLetsItRun()
    {
        var script = ArielScript.Instance;

        var starts = script.Act(Turn(health: 0.05))!;
        Assert.NotNull(starts.Cast);
        Assert.NotNull(starts.Sets);
        var ending = starts.Sets!;

        var runs = script.Act(Turn(health: 0.05, marks: [ending]))!;
        Assert.NotNull(runs.Cast);
        Assert.NotEqual(starts.Cast!.Value, runs.Cast!.Value);

        var already = script.Act(Turn(
            health: 0.05, marks: [ending],
            castCount: new Dictionary<uint, int> { [runs.Cast!.Value] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [runs.Cast!.Value] = 1 }));

        Assert.True(already is null || already.Cast != runs.Cast);
    }

    [Fact]
    public void Ariel_DoesNotStartHerEndingWhileSheIsStillHealthy()
    {
        var healthy = ArielScript.Instance.Act(Turn(health: 0.8, secondsInFight: 0.5, castCount: ArielRotting()));
        Assert.Null(healthy);
    }

    [Fact]
    public void Cosmo_CyclesHisThreeCursesRatherThanRepeatingOne()
    {
        var script = CosmoScript.Instance;
        var cast = new Dictionary<uint, int>();
        var since = new Dictionary<uint, double>();
        var order = new List<uint>();

        for (var i = 0; i < 3; i++)
        {
            var action = script.Act(Turn(castCount: cast, secondsSinceCast: since))!;
            var spellId = action.Cast!.Value;
            order.Add(spellId);
            cast[spellId] = cast.TryGetValue(spellId, out var n) ? n + 1 : 1;
            since[spellId] = 60;
        }

        Assert.Equal(3, order.Distinct().Count());
    }

    [Fact]
    public void Cosmo_WaitsBetweenCurses()
    {
        var script = CosmoScript.Instance;
        var first = script.Act(Turn())!;

        var straightAfter = script.Act(Turn(
            castCount: new Dictionary<uint, int> { [first.Cast!.Value] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [first.Cast!.Value] = 0.2 }));

        Assert.Null(straightAfter);
    }

    [Fact]
    public void Cosmo_ChangesShapeWhenHeIsPushed_AndOnlyOnce()
    {
        var script = CosmoScript.Instance;

        var change = script.Act(Turn(health: 0.5))!;
        Assert.NotNull(change.Cast);
        Assert.Equal(CastTarget.Self, change.At);
        var demon = change.Sets!;

        var wearing = script.Act(Turn(health: 0.5, marks: [demon]));
        Assert.True(wearing is null || wearing.Sets != demon);
    }

    [Fact]
    public void Cosmo_ChangesBackWhenHeIsNearlyBeaten()
    {
        var script = CosmoScript.Instance;
        var demon = script.Act(Turn(health: 0.5))!.Sets!;

        var back = script.Act(Turn(health: 0.1, marks: [demon]))!;

        Assert.NotNull(back.Cast);
        Assert.Equal(demon, back.Clears);
        Assert.NotNull(back.Sets);
    }

    [Fact]
    public void Cosmo_BroughtStraightToNearlyDead_DoesNotChangeShapeOnTheWay()
    {
        var first = CosmoScript.Instance.Act(Turn(health: 0.05))!;

        Assert.NotNull(first.Sets);
    }

    [Fact]
    public void Cosmo_PullsPuppetsOutOfTheFloorWhileHeWearsTheShape()
    {
        var script = CosmoScript.Instance;
        var demon = script.Act(Turn(health: 0.5))!.Sets!;

        var summon = script.Act(Turn(
            health: 0.5, marks: [demon],
            castCount: new Dictionary<uint, int> { [2637] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [2637] = 1 }))!;

        Assert.NotNull(summon.Summon);
        Assert.Equal("The dead fight for ME!", summon.Yell);
        Assert.Equal(CosmoScript.PuppetCrumblesAfter, summon.SummonLasts);
    }

    [Fact]
    public void Cosmo_DropsHisFlameCurseOnAnyoneButAimsTheOtherCursesAtHisTarget()
    {
        var script = CosmoScript.Instance;
        var cast = new Dictionary<uint, int>();
        var aims = new Dictionary<uint, CastTarget>();
        for (var i = 0; i < 3; i++)
        {
            var action = script.Act(Turn(castCount: cast))!;
            aims[action.Cast!.Value] = action.At;
            cast[action.Cast!.Value] = 1;
        }

        Assert.Equal(CastTarget.AnyEnemy, aims[2634]);
        Assert.Equal(CastTarget.Enemy, aims[2632]);
        Assert.Equal(CastTarget.Enemy, aims[2633]);
    }

    [Fact]
    public void Cosmo_EndsTheDemonShapeWhenHeChangesBack()
    {
        var demon = CosmoScript.Instance.Act(Turn(health: 0.5))!.Sets!;

        var back = CosmoScript.Instance.Act(Turn(health: 0.1, marks: [demon]))!;

        Assert.Equal(2635u, back.Ends);
    }

    [Fact]
    public void Cosmo_LeavesAFreshPuppetStandingLongEnoughToBeKilled()
    {
        var script = CosmoScript.Instance;
        var demon = script.Act(Turn(health: 0.5))!.Sets!;

        var fresh = script.Act(Turn(
            health: 0.5, marks: [demon], summonsInReach: 1,
            castCount: new Dictionary<uint, int> { [2637] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [2637] = 1, [BossTiming.SummonKey(2697)] = 2 }));

        Assert.True(fresh is null || fresh.At != CastTarget.OwnSummon);
    }

    [Fact]
    public void Cosmo_SummonsAgainOnlyOnceThePuppetTimerHasRunOut()
    {
        var script = CosmoScript.Instance;
        var demon = script.Act(Turn(health: 0.5))!.Sets!;

        var soon = script.Act(Turn(
            health: 0.5, marks: [demon],
            castCount: new Dictionary<uint, int> { [2637] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [2637] = 1, [BossTiming.SummonKey(2697)] = 3 }));

        Assert.True(soon?.Summon is null);
    }

    [Fact]
    public void Cosmo_YellsAsHeChangesShape()
    {
        var change = CosmoScript.Instance.Act(Turn(health: 0.5))!;

        Assert.Equal("The demons stir within me!", change.Yell);
    }

    [Fact]
    public void Cosmo_ShieldsHimselfOnceAfterChangingBack_ThenCursesAgain()
    {
        var script = CosmoScript.Instance;
        var demon = script.Act(Turn(health: 0.5))!.Sets!;
        var back = script.Act(Turn(health: 0.2, marks: [demon]))!;
        var reverted = back.Sets!;

        var shield = script.Act(Turn(health: 0.2, marks: [reverted]))!;
        Assert.Equal(2638u, shield.Cast);
        Assert.Equal(CastTarget.Self, shield.At);

        var curse = script.Act(Turn(
            health: 0.2, marks: [reverted],
            castCount: new Dictionary<uint, int> { [2638] = 1 }))!;
        Assert.Equal(CastTarget.Enemy, curse.At);
    }

    [Fact]
    public void Cosmo_EatsAPuppetLeftStandingBesideHim_AndThenTakesTheHealth()
    {
        var script = CosmoScript.Instance;
        var demon = script.Act(Turn(health: 0.5))!.Sets!;

        var eats = script.Act(Turn(
            health: 0.5, marks: [demon], summonsInReach: 1,
            secondsSinceCast: new Dictionary<uint, double> { [BossTiming.SummonKey(2697)] = 10 }))!;
        Assert.Equal(CastTarget.OwnSummon, eats.At);
        Assert.NotNull(eats.Cast);
        Assert.Equal("Give me your power!", eats.Yell);

        var feeds = script.Act(Turn(
            health: 0.5, marks: [demon], summonsInReach: 0,
            castCount: new Dictionary<uint, int> { [eats.Cast!.Value] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [eats.Cast!.Value] = 1 }))!;

        Assert.Equal(CastTarget.Self, feeds.At);
        Assert.NotEqual(eats.Cast!.Value, feeds.Cast!.Value);
    }

    [Fact]
    public void Cosmo_InHisFirstShape_NeitherSummonsNorEats()
    {
        var caster = CosmoScript.Instance.Act(Turn(health: 0.9, summonsInReach: 2))!;

        Assert.Null(caster.Summon);
        Assert.Equal(CastTarget.Enemy, caster.At);
    }

    [Fact]
    public void Cosmo_TakesTheDemonShapeAtThreeQuarters()
    {
        Assert.Equal(2635u, CosmoScript.Instance.Act(Turn(health: 0.74))!.Cast);
        Assert.NotEqual(2635u, CosmoScript.Instance.Act(Turn(health: 0.76))!.Cast);
    }

    [Fact]
    public void Cosmo_RaisesAPuppetOnlyEveryOtherTurn_LeavingTheTurnBetweenForEatingIt()
    {
        var demon = CosmoScript.Instance.Act(Turn(health: 0.5))!.Sets!;
        var erodeJustCast = new Dictionary<uint, int> { [2637] = 1 };

        Assert.Null(CosmoScript.Instance.Act(Turn(
            health: 0.5, marks: [demon], castCount: erodeJustCast,
            secondsSinceCast: new Dictionary<uint, double> { [2637] = 1, [BossTiming.SummonKey(2697)] = 15 }))?.Summon);
        Assert.NotNull(CosmoScript.Instance.Act(Turn(
            health: 0.5, marks: [demon], castCount: erodeJustCast,
            secondsSinceCast: new Dictionary<uint, double> { [2637] = 1, [BossTiming.SummonKey(2697)] = 20 }))!.Summon);
    }

    [Fact]
    public void Cosmo_ShedsTheDemonShapeAndTheShieldWhenHisFightResets()
    {
        Assert.Equal([2635u, 2638u], CosmoScript.Instance.ClearedOnReset);
    }

    [Fact]
    public void Ariel_SeedsSomebodyAboutEveryTenSeconds()
    {
        Dictionary<uint, int> Cast() => new() { [2651] = 1, [2652] = 1 };

        Assert.Null(ArielScript.Instance.Act(Turn(castCount: Cast(),
            secondsSinceCast: new Dictionary<uint, double> { [2652] = 9 })));
        Assert.Equal(2652u, ArielScript.Instance.Act(Turn(castCount: Cast(),
            secondsSinceCast: new Dictionary<uint, double> { [2652] = 10 }))!.Cast);
    }

    [Fact]
    public void TheExecutioner_MendsUnderHisArmourOnlyInLegend()
    {
        var armoured = new Dictionary<uint, int> { [2674] = 1 };
        var since = new Dictionary<uint, double> { [2674] = 1 };

        Assert.Equal(2781u, ExecutionerScript.Instance.Act(Turn(castCount: armoured, secondsSinceCast: since) with { Legend = true })!.Cast);
        Assert.NotEqual(2781u, ExecutionerScript.Instance.Act(Turn(castCount: armoured, secondsSinceCast: since))?.Cast);
        Assert.NotEqual(2781u, ExecutionerScript.Instance.Act(Turn(
            castCount: new Dictionary<uint, int> { [2674] = 1, [2781] = 1 }, secondsSinceCast: since) with
        { Legend = true })?.Cast);
    }

    [Fact]
    public void Andare_ShoutsAtTheOtherPrisonersButNeverFights()
    {
        var andare = AndareScript.Instance;

        Assert.Contains("I'll tell you again: the judgment on me is not justice!", andare.Shouts);
        Assert.False(andare.FightsBack);
        Assert.False(andare.NoticesEnemies);
    }
}
