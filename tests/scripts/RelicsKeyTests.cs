namespace Ubiquitous.Scripts.Tests;

public sealed class RelicsKeyTests
{
    private static RitualView View(
        double seconds = 0, bool alive = true, bool arrived = false, int living = 0, bool bossDead = false) =>
        new(TimeSpan.FromSeconds(seconds), alive, arrived, living, bossDead);

    private static void RunBlock(Ritual ritual, RitualBlock block, RitualOrders opening)
    {
        Assert.Equal(block.Opening, opening.Say);
        Assert.Equal(block.Waves[0], opening.Wave);
        for (var wave = 1; wave < block.Waves.Count; wave++)
        {
            Assert.Null(ritual.Advance(View(seconds: (30 * wave) - 1, arrived: true, living: 3)));
            Assert.Equal(block.Waves[wave], ritual.Advance(View(seconds: 30 * wave, arrived: true, living: 3))!.Wave);
        }

        Assert.Null(ritual.Advance(View(seconds: 200, arrived: true, living: 1)));
        Assert.Equal(block.Warning, ritual.Advance(View(seconds: 200, arrived: true))!.Say);
        Assert.Null(ritual.Advance(View(seconds: 7, arrived: true)));
        Assert.Equal(block.Boss, ritual.Advance(View(seconds: 8, arrived: true))!.Boss);
        Assert.Null(ritual.Advance(View(seconds: 60, arrived: true)));
    }

    [Fact]
    public void TheRitual_RunsFromTheEscortThroughEveryBlockToTheFarewell()
    {
        var ritual = RelicsKeyRitual.Create();
        Assert.Equal("Follow me!", ritual.Start()!.Say);
        Assert.Null(ritual.Start());

        Assert.Null(ritual.Advance(View(seconds: 30)));
        var starting = ritual.Advance(View(arrived: true))!;
        Assert.Equal("I'm starting!", starting.Say);
        Assert.Equal(RelicsKeyRitual.Summoning, starting.KeeperCasts);
        Assert.Null(ritual.Advance(View(seconds: 2, arrived: true)));

        RunBlock(ritual, RelicsKeyRitual.Blocks[0], ritual.Advance(View(seconds: 3, arrived: true))!);
        RunBlock(ritual, RelicsKeyRitual.Blocks[1], ritual.Advance(View(arrived: true, bossDead: true))!);
        RunBlock(ritual, RelicsKeyRitual.Blocks[2], ritual.Advance(View(arrived: true, bossDead: true))!);

        Assert.Equal("It's done!", ritual.Advance(View(arrived: true, bossDead: true))!.Say);
        Assert.Null(ritual.Advance(View(seconds: 3, arrived: true, bossDead: true)));
        var farewell = ritual.Advance(View(seconds: 4, arrived: true, bossDead: true))!;
        Assert.True(farewell.Completes);
        Assert.Contains("Wyrgast's Library", farewell.Say);
        Assert.Equal(RitualStage.Done, ritual.Stage);
    }

    [Fact]
    public void TheRitual_FailsWhenTheKeeperDies_AndCanBeStartedAgainAfterAPause()
    {
        var ritual = RelicsKeyRitual.Create();
        ritual.Start();
        ritual.Advance(View(arrived: true));
        ritual.Advance(View(seconds: 3, arrived: true));

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
        var ritual = RelicsKeyRitual.Create();
        ritual.Start();
        ritual.Advance(View(arrived: true));
        ritual.Advance(View(seconds: 3, arrived: true));

        Assert.NotNull(ritual.Advance(View(seconds: 30, arrived: true, living: 3))!.Wave);
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
