namespace Ubiquitous.Scripts.Tests;

public sealed class RelicsKeyTests
{
    private static RitualView View(
        double seconds = 0, bool alive = true, bool arrived = false, int living = 0, bool bossDead = false) =>
        new(TimeSpan.FromSeconds(seconds), alive, arrived, living, bossDead);

    private static RitualView At(double seconds, int living = 3, bool bossDead = false) =>
        View(seconds, arrived: true, living: living, bossDead: bossDead);

    private static uint[] Wave(params uint[] creatures) => creatures;

    // Escort, then Tharrath at the ritual ground: the answer to the tick that opens the first block.
    private static (Ritual Ritual, RitualOrders FirstBlock) AtTheRitualGround()
    {
        var ritual = RelicsKeyRitual.Create();
        ritual.Start();
        ritual.Advance(View(arrived: true));
        return (ritual, ritual.Advance(View(arrived: true))!);
    }

    // Lets every wave still due go out, a tick at a time, with the creatures still standing.
    private static void RunOutWaves(Ritual ritual)
    {
        while (ritual.Advance(At(1000)) is not null) { }
    }

    // Clears a block's last wave, waits out the pause before its boss, and checks the warning comes after it.
    private static void CallsItsBoss(Ritual ritual, RitualBlock block)
    {
        Assert.Null(ritual.Advance(At(500, living: 1)));
        Assert.Equal(RitualStage.Waves, ritual.Stage);
        Assert.NotNull(ritual.Advance(At(500, living: 0)));
        Assert.Equal(RitualStage.BossComing, ritual.Stage);

        Assert.Null(ritual.Advance(At(19.9, living: 0)));
        var boss = ritual.Advance(At(20, living: 0))!;
        Assert.Equal(block.Boss, boss.Boss);
        Assert.Null(boss.Say);

        Assert.Null(ritual.Advance(At(4.9, living: 0)));
        Assert.Equal(block.Warning, ritual.Advance(At(5, living: 0))!.Say);
        Assert.Null(ritual.Advance(At(60, living: 0)));
    }

    [Fact]
    public void TheFirstBlock_StartsAtOnce_ThenCallsHathraadOnceItsWavesAreDead()
    {
        var (ritual, first) = AtTheRitualGround();
        Assert.Equal(Wave(2627, 2628), first.Wave);
        Assert.Null(first.Say);

        Assert.Null(ritual.Advance(At(4.9)));
        Assert.Equal("Here they come, right on time!", ritual.Advance(At(5))!.Say);
        Assert.Null(ritual.Advance(At(34.9)));
        Assert.Equal(Wave(2627, 2628, 2629), ritual.Advance(At(35))!.Wave);
        Assert.Null(ritual.Advance(At(64.9)));
        Assert.Equal(Wave(2627, 2627, 2629), ritual.Advance(At(65))!.Wave);

        CallsItsBoss(ritual, RelicsKeyRitual.Blocks[0]);
        Assert.Equal(RelicsKeyRitual.Hathraad, RelicsKeyRitual.Blocks[0].Boss);
        Assert.Equal("The air is humming... Must be a giant one!", RelicsKeyRitual.Blocks[0].Warning);
    }

    [Fact]
    public void TheSecondBlock_CallsOutTenSecondsAfterHathraad_AndSendsGhostsFromTwentyFive()
    {
        var (ritual, _) = AtTheRitualGround();
        RunOutWaves(ritual);
        CallsItsBoss(ritual, RelicsKeyRitual.Blocks[0]);

        Assert.Null(ritual.Advance(At(0, living: 0, bossDead: true))!.Wave);
        Assert.Equal(1, ritual.Block);
        Assert.Null(ritual.Advance(At(9.9, living: 0)));
        Assert.Equal("More of them are coming!", ritual.Advance(At(10, living: 0))!.Say);
        Assert.Null(ritual.Advance(At(24.9, living: 0)));
        Assert.Equal(Wave(2630, 2631, 2631), ritual.Advance(At(25, living: 0))!.Wave);
        Assert.Equal(Wave(2630, 2631, 2632), ritual.Advance(At(55))!.Wave);
        Assert.Equal(Wave(2630, 2631, 2633), ritual.Advance(At(85))!.Wave);

        CallsItsBoss(ritual, RelicsKeyRitual.Blocks[1]);
        Assert.Equal(RelicsKeyRitual.Kermode, RelicsKeyRitual.Blocks[1].Boss);
        Assert.Equal("An angry spirit approaches!", RelicsKeyRitual.Blocks[1].Warning);
    }

    [Fact]
    public void TheLastBlock_SendsFiveWavesOnTheirOwnTimers_ThenAlrRahjil_ThenTheFarewell()
    {
        var (ritual, _) = AtTheRitualGround();
        RunOutWaves(ritual);
        CallsItsBoss(ritual, RelicsKeyRitual.Blocks[0]);
        ritual.Advance(At(0, living: 0, bossDead: true));
        RunOutWaves(ritual);
        CallsItsBoss(ritual, RelicsKeyRitual.Blocks[1]);

        ritual.Advance(At(0, living: 0, bossDead: true));
        Assert.Equal(2, ritual.Block);
        Assert.Equal("You've got to hold them off!", ritual.Advance(At(10, living: 0))!.Say);
        Assert.Equal(Wave(2628, 2631, 2635), ritual.Advance(At(20, living: 0))!.Wave);
        Assert.Null(ritual.Advance(At(49.9)));
        Assert.Equal(Wave(2628, 2630, 2648), ritual.Advance(At(50))!.Wave);
        Assert.Equal(Wave(2629, 2631, 2634), ritual.Advance(At(90))!.Wave);
        Assert.Equal(Wave(2632, 2634, 2635, 2648), ritual.Advance(At(135))!.Wave);
        Assert.Null(ritual.Advance(At(184.9)));
        Assert.Equal(Wave(2632, 2633, 2635, 2648), ritual.Advance(At(185))!.Wave);

        CallsItsBoss(ritual, RelicsKeyRitual.Blocks[2]);
        Assert.Equal(RelicsKeyRitual.AlrRahjil, RelicsKeyRitual.Blocks[2].Boss);
        Assert.Equal("I'm almost finished!", RelicsKeyRitual.Blocks[2].Warning);

        Assert.Equal("It's done!", ritual.Advance(At(0, living: 0, bossDead: true))!.Say);
        Assert.Null(ritual.Advance(At(9.9, living: 0, bossDead: true)));
        var farewell = ritual.Advance(At(10, living: 0, bossDead: true))!;
        Assert.True(farewell.Completes);
        Assert.Contains("Wyrgast's Library", farewell.Say);
        Assert.Equal(RitualStage.Done, ritual.Stage);
    }

    [Fact]
    public void TheRitual_StartsWithTheEscort_AndTharrathBeginsAtTheGround()
    {
        var ritual = RelicsKeyRitual.Create();
        Assert.Equal("Follow me!", ritual.Start()!.Say);
        Assert.Null(ritual.Start());

        Assert.Null(ritual.Advance(View(seconds: 30)));
        var starting = ritual.Advance(View(arrived: true))!;
        Assert.Equal("I'm starting!", starting.Say);
        Assert.Equal(RelicsKeyRitual.Summoning, starting.KeeperCasts);
    }

    [Fact]
    public void TheRitual_FailsWhenTheKeeperDies_AndCanBeStartedAgainAfterAPause()
    {
        var (ritual, _) = AtTheRitualGround();

        var failed = ritual.Advance(View(seconds: 10, alive: false, living: 3))!;
        Assert.True(failed.Fails);
        Assert.Equal("My spell... failed.", failed.Say);

        Assert.Null(ritual.Advance(View(seconds: 14, alive: false)));
        Assert.True(ritual.Advance(View(seconds: 15, alive: false))!.Resets);
        Assert.Equal(RitualStage.Waiting, ritual.Stage);
        Assert.Equal("Follow me!", ritual.Start()!.Say);
    }

    [Fact]
    public void TheRitual_SendsTheNextWaveOnItsTimer_EvenWithTheLastStillStanding()
    {
        var (ritual, _) = AtTheRitualGround();
        ritual.Advance(At(5));

        Assert.NotNull(ritual.Advance(At(35, living: 2))!.Wave);
    }

    private static CombatTurnContext Turn(
        double health = 1.0,
        double secondsInFight = 60,
        IDictionary<uint, int>? castCount = null,
        IDictionary<uint, double>? secondsSinceCast = null,
        bool legend = false,
        bool woundedAlly = false)
    {
        var since = new Dictionary<uint, TimeSpan>();
        foreach (var (spellId, seconds) in secondsSinceCast ?? new Dictionary<uint, double>())
            since[spellId] = TimeSpan.FromSeconds(seconds);

        return new CombatTurnContext(
            Creature: null,
            HealthFraction: health,
            TimeInFight: TimeSpan.FromSeconds(secondsInFight),
            InMeleeRange: true,
            SinceCast: since,
            CastCount: new Dictionary<uint, int>(castCount ?? new Dictionary<uint, int>()),
            SummonsInReach: 0,
            Marks: new HashSet<string>(StringComparer.Ordinal),
            Legend: legend,
            WoundedAllyInReach: woundedAlly);
    }

    [Fact]
    public void Hathraad_SplitsOffTwoImagesAtEachQuarter_ShoutingOncePerSplit()
    {
        var images = BossTiming.SummonKey(HathraadScript.MirrorImage);

        var first = HathraadScript.Instance.Act(Turn(health: 0.74))!;
        Assert.Equal(HathraadScript.MirrorImage, first.Summon);
        Assert.Equal("Dividedness!", first.Yell);

        var second = HathraadScript.Instance.Act(Turn(health: 0.74, castCount: new Dictionary<uint, int> { [images] = 1 }))!;
        Assert.Equal(HathraadScript.MirrorImage, second.Summon);
        Assert.Null(second.Yell);

        Assert.Null(HathraadScript.Instance.Act(Turn(health: 0.74,
            castCount: new Dictionary<uint, int> { [images] = 2 },
            secondsSinceCast: new Dictionary<uint, double> { [2375] = 1, [2373] = 1 }))?.Summon);
        Assert.Equal(6, Enumerable.Range(0, 7).Count(n =>
            HathraadScript.Instance.Act(Turn(health: 0.1, castCount: new Dictionary<uint, int> { [images] = n }))?.Summon is not null));
    }

    [Fact]
    public void Hathraad_ShoutsTempestAndDrownsTheRoomEveryTenSeconds()
    {
        var tempest = HathraadScript.Instance.Act(Turn(secondsSinceCast: new Dictionary<uint, double> { [2375] = 10 }))!;
        Assert.Equal(2375u, tempest.Cast);
        Assert.Equal("Tempest!", tempest.Yell);

        var ball = HathraadScript.Instance.Act(Turn(secondsSinceCast: new Dictionary<uint, double> { [2375] = 2 }))!;
        Assert.Equal(2373u, ball.Cast);
        Assert.Equal(CastTarget.Enemy, ball.At);
    }

    [Fact]
    public void Kermode_OpensOnTheGrowlInNormalAndTheRoarInLegend_ThenTakesTurns()
    {
        Assert.Equal(2371u, KermodeScript.Instance.Act(Turn())!.Cast);
        Assert.Equal(2370u, KermodeScript.Instance.Act(Turn(legend: true))!.Cast);
        Assert.Equal(2370u, KermodeScript.Instance.Act(Turn(castCount: new Dictionary<uint, int> { [2371] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [2371] = 8 }))!.Cast);
        Assert.Null(KermodeScript.Instance.Act(Turn(castCount: new Dictionary<uint, int> { [2371] = 1 },
            secondsSinceCast: new Dictionary<uint, double> { [2371] = 7 })));
    }

    [Fact]
    public void Kermode_GoesIntoAFrenzyOnceAtThirtyPercent()
    {
        Assert.Equal(2372u, KermodeScript.Instance.Act(Turn(health: 0.3))!.Cast);
        Assert.NotEqual(2372u, KermodeScript.Instance.Act(Turn(health: 0.2, castCount: new Dictionary<uint, int> { [2372] = 1 }))?.Cast);
    }

    [Fact]
    public void AlrRahjil_TurnsThroughSiphonMireAndIgnite_OnAnyoneInTheFight()
    {
        var script = AlrRahjilScript.Instance;
        var siphon = script.Act(Turn())!;
        Assert.Equal(2379u, siphon.Cast);
        Assert.Equal(CastTarget.AnyEnemy, siphon.At);

        Assert.Equal(2384u, script.Act(Turn(castCount: new Dictionary<uint, int> { [2379] = 1 }))!.Cast);

        var mire = script.Act(Turn(castCount: new Dictionary<uint, int> { [2379] = 1, [2384] = 1 }))!;
        Assert.Equal(2382u, mire.Cast);
        Assert.Equal("Tremble in terror!", mire.Yell);
        Assert.Equal(2383u, mire.LeavesPool);

        Assert.Equal(2670u, script.Act(Turn(castCount: new Dictionary<uint, int> { [2379] = 1, [2384] = 1, [2382] = 1 }))!.Cast);
    }

    [Fact]
    public void AlrRahjil_GrowsAtThirtyPercent_BlastsOnce_AndShrinksBack()
    {
        var script = AlrRahjilScript.Instance;
        Assert.Equal(2385u, script.Act(Turn(health: 0.3))!.Cast);

        var blast = script.Act(Turn(health: 0.3, castCount: new Dictionary<uint, int> { [2385] = 1 }))!;
        Assert.Equal(2381u, blast.Cast);
        Assert.Equal("Out, brief life!", blast.Yell);

        Assert.Equal(2386u, script.Act(Turn(health: 0.2, castCount: new Dictionary<uint, int> { [2385] = 1, [2381] = 1 }))!.Cast);
        Assert.NotEqual(2381u, script.Act(Turn(health: 0.1,
            castCount: new Dictionary<uint, int> { [2385] = 1, [2381] = 1, [2386] = 1 }))?.Cast);
    }

    [Fact]
    public void TheBosses_SpeakWhenPulledAndWhenTheyFall()
    {
        Assert.Equal("Heretics must die!", HathraadScript.Instance.PullLine);
        Assert.Equal("No....", HathraadScript.Instance.DeathLine);
        Assert.Equal("You dare disturb me! You will die!", AlrRahjilScript.Instance.PullLine);
        Assert.Equal("I will devour your rotten soul when next we meet in hell!", AlrRahjilScript.Instance.DeathLine);
    }

    [Fact]
    public void TheGhostlyPriest_HealsOnlyWhenSomebodyBesideItIsHurt()
    {
        Assert.NotEqual(2413u, RelicsKeyCreatures.GhostlyPriest.Act(Turn())?.Cast);
        var heal = RelicsKeyCreatures.GhostlyPriest.Act(Turn(woundedAlly: true))!;
        Assert.Equal(2413u, heal.Cast);
        Assert.Equal(CastTarget.WoundedAlly, heal.At);
    }

    [Fact]
    public void TheGhostlyHunter_CallsInOneGhostWolf()
    {
        Assert.Equal(2650u, RelicsKeyCreatures.GhostlyHunter.Act(Turn())!.Summon);
        Assert.Null(RelicsKeyCreatures.GhostlyHunter.Act(Turn(
            castCount: new Dictionary<uint, int> { [BossTiming.SummonKey(2650)] = 1 })));
    }

    [Fact]
    public void EveryRelicsKeyCreatureBoundInContent_HasAScriptInTheCatalogue()
    {
        Assert.All(Ubiquitous.Game.GameContent.RelicsKeyScripts.Values,
            name => Assert.True(ScriptRegistry.Catalogue.ContainsKey(name), name));
    }
}
