using Ubiquitous.Data;
using Ubiquitous.Game;

namespace Ubiquitous.Scripts.Tests;

public sealed class CreatureScriptTests
{
    private static CreatureDefinition Creature(bool elite = false, float? hold = null) =>
        new(9500, "prisoner", 20, 1900, LeashRange: hold,
            Rank: elite ? CreatureRank.Elite : CreatureRank.Ordinary);

    [Fact]
    public void Default_HoldsAFightInRangeAndDropsOneOutOfIt()
    {
        var script = DefaultCreatureScript.Instance;
        var near = new GiveUpContext(Creature(), DistanceToTarget: 5, DistanceFromHome: 5,
            TimeInFight: TimeSpan.FromMinutes(1), LeaderStillFighting: false);

        Assert.False(script.GivesUp(near));
        Assert.True(script.GivesUp(near with { DistanceToTarget = 999 }));
    }

    [Fact]
    public void Default_DropsAFightItHasBeenDraggedTooFarFromHomeFor()
    {
        var script = DefaultCreatureScript.Instance;
        var dragged = new GiveUpContext(Creature(), DistanceToTarget: 1, DistanceFromHome: 999,
            TimeInFight: TimeSpan.FromMinutes(1), LeaderStillFighting: false);

        Assert.True(script.GivesUp(dragged));
    }

    [Fact]
    public void AnyScript_HoldsWhileItsOwnLeaderIsStillFighting()
    {
        var hopeless = new GiveUpContext(Creature(), DistanceToTarget: 999, DistanceFromHome: 999,
            TimeInFight: TimeSpan.FromHours(1), LeaderStillFighting: true,
            TimeOutsideMeleeRange: TimeSpan.FromHours(1));

        Assert.False(DefaultCreatureScript.Instance.GivesUp(hopeless));
        Assert.False(DungeonInmateScript.Instance.GivesUp(hopeless));
    }

    [Fact]
    public void DungeonInmate_IgnoresHowFarItHasComeFromItsSpawnPoint()
    {
        var script = DungeonInmateScript.Instance;
        var walkedThroughTheBuilding = new GiveUpContext(Creature(), DistanceToTarget: 2,
            DistanceFromHome: 500, TimeInFight: TimeSpan.FromMinutes(1), LeaderStillFighting: false);

        Assert.False(script.GivesUp(walkedThroughTheBuilding));
        Assert.True(script.GivesUp(walkedThroughTheBuilding with
        {
            DistanceToTarget = 500,
            TimeOutsideMeleeRange = DungeonInmateScript.OutrunTime,
        }));
    }

    [Fact]
    public void DungeonInmate_IsShakenOffOnlyByStayingOutOfReach()
    {
        var script = DungeonInmateScript.Instance;
        var justLeft = new GiveUpContext(Creature(), DistanceToTarget: 500, DistanceFromHome: 0,
            TimeInFight: TimeSpan.FromMinutes(1), LeaderStillFighting: false,
            TimeOutsideMeleeRange: TimeSpan.Zero);

        Assert.False(script.GivesUp(justLeft));
        Assert.False(script.GivesUp(justLeft with { TimeOutsideMeleeRange = null }));
        Assert.True(script.GivesUp(justLeft with { TimeOutsideMeleeRange = DungeonInmateScript.OutrunTime }));
    }

    [Fact]
    public void Guard_RunsToANearbyFightAndIgnoresADistantOne()
    {
        var script = GuardScript.Instance;

        Assert.True(script.Assists(new AssistContext(Creature(), GuardScript.ReactionRange - 1)));
        Assert.False(script.Assists(new AssistContext(Creature(), GuardScript.ReactionRange + 1)));
        Assert.False(DefaultCreatureScript.Instance.Assists(new AssistContext(Creature(), 1)));
    }

    [Fact]
    public void Flees_WhenNearlyDead_ButNeverWhenGuardTier()
    {
        Assert.True(DefaultCreatureScript.Instance.Flees(new FleeContext(Creature(), 0.1)));
        Assert.False(DefaultCreatureScript.Instance.Flees(new FleeContext(Creature(), 0.9)));
        Assert.False(DefaultCreatureScript.Instance.Flees(new FleeContext(Creature(elite: true), 0.01)));
    }

    [Fact]
    public void HoldingAFight_ComesFromTheRowRatherThanTheRank()
    {
        var beyondTheFallback = new GiveUpContext(Creature(), DistanceToTarget: CreatureScript.DefaultHoldRange + 5,
            DistanceFromHome: 0, TimeInFight: TimeSpan.FromMinutes(1), LeaderStillFighting: false);

        Assert.True(DefaultCreatureScript.Instance.GivesUp(beyondTheFallback));
        Assert.True(DefaultCreatureScript.Instance.GivesUp(
            beyondTheFallback with { Creature = Creature(elite: true) }));

        Assert.False(DefaultCreatureScript.Instance.GivesUp(
            beyondTheFallback with { Creature = Creature(hold: (float)CreatureScript.DefaultHoldRange + 10f) }));
    }

    [Fact]
    public void ACreatureCarryingItsOwnHoldRangeUsesItInsteadOfTheFallback()
    {
        var tight = new GiveUpContext(Creature(hold: 10), DistanceToTarget: 20, DistanceFromHome: 0,
            TimeInFight: TimeSpan.FromMinutes(1), LeaderStillFighting: false);

        Assert.True(DefaultCreatureScript.Instance.GivesUp(tight));
        Assert.False(DefaultCreatureScript.Instance.GivesUp(tight with { DistanceToTarget = 5 }));
    }
}

public sealed class TrainingDummyScriptTests
{
    private static readonly CreatureDefinition Dummy =
        new(9001, "practice target", 1, 100, RawExtraFlags: 1u << 7);

    [Fact]
    public void ItDoesNoneOfTheThingsAFightingCreatureDoes()
    {
        var script = TrainingDummyScript.Instance;

        Assert.False(script.NoticesEnemies);
        Assert.False(script.FightsBack);
        Assert.False(script.Chases);
        Assert.False(script.DrawsOthersIn);
        Assert.False(script.Assists(new AssistContext(Dummy, 1)));
    }

    [Fact]
    public void ItLeavesAFightOnAQuietSpellRatherThanOnDistance()
    {
        var script = TrainingDummyScript.Instance;
        var justStepped = new GiveUpContext(Dummy, DistanceToTarget: 1, DistanceFromHome: 0,
            TimeInFight: TimeSpan.FromHours(1), LeaderStillFighting: false,
            TimeOutsideMeleeRange: TimeSpan.Zero);

        Assert.False(script.GivesUp(justStepped));
        Assert.True(script.GivesUp(justStepped with
        {
            TimeOutsideMeleeRange = TrainingDummyScript.CombatExitDelay,
        }));
    }

    [Fact]
    public void DistanceAloneNeverEndsAFightWithIt()
    {
        var script = TrainingDummyScript.Instance;
        var farButBusy = new GiveUpContext(Dummy, DistanceToTarget: 9999, DistanceFromHome: 9999,
            TimeInFight: TimeSpan.FromHours(1), LeaderStillFighting: false,
            TimeOutsideMeleeRange: null);

        Assert.False(script.GivesUp(farButBusy));
    }

    [Fact]
    public void ItIsBoundByItsOwnTemplateFlag_EvenInsideADungeon()
    {
        var registry = new ScriptRegistry([new DungeonDefinition(14, "The Prison", [2666])]);

        Assert.Equal(TrainingDummyScript.Instance,
            registry.For(Dummy, onDungeonMap: false, advertisesQuests: false));
        Assert.Equal(TrainingDummyScript.Instance,
            registry.For(Dummy, onDungeonMap: true, advertisesQuests: false));
    }
}

public sealed class ScriptRegistryTests
{
    private static readonly IReadOnlyList<DungeonDefinition> Rules =
        [new DungeonDefinition(14, "The Prison", [2666])];

    private static CreatureDefinition Named(string name, int faction) =>
        new(1, name, 20, 100, FactionType: faction);

    [Fact]
    public void ACreatureInsideADungeonBehavesLikeOne()
    {
        var registry = new ScriptRegistry(Rules);

        Assert.Equal(DungeonInmateScript.Instance,
            registry.For(Named("prisoner", UnitFactionTypes.Hostile), onDungeonMap: true, advertisesQuests: false));
        Assert.Equal(DefaultCreatureScript.Instance,
            registry.For(Named("prisoner", UnitFactionTypes.Hostile), onDungeonMap: false, advertisesQuests: false));
    }

    [Fact]
    public void ContentNamingAScriptOutrightWinsOverEveryFallback()
    {
        var registry = new ScriptRegistry(Rules,
            new Dictionary<uint, string> { [1] = DefaultCreatureScript.Instance.Name });

        Assert.Equal(DefaultCreatureScript.Instance,
            registry.For(Named("prisoner", UnitFactionTypes.Hostile), onDungeonMap: true, advertisesQuests: false));
    }

    [Fact]
    public void AnUnknownScriptNameFallsBackRatherThanFailing()
    {
        var registry = new ScriptRegistry(Rules, new Dictionary<uint, string> { [1] = "creature.nothingByThatName" });

        Assert.Equal(DefaultCreatureScript.Instance,
            registry.For(Named("town guard", UnitFactionTypes.Hostile), onDungeonMap: false, advertisesQuests: false));
    }

    [Fact]
    public void TheGuardFallbackAnswersOnNameAndOnAdvertisedQuests()
    {
        var registry = new ScriptRegistry(Rules);

        Assert.Equal(GuardScript.Instance,
            registry.For(Named("Test Guard", UnitFactionTypes.Townsfolk), onDungeonMap: false, advertisesQuests: false));
        Assert.Equal(GuardScript.Instance,
            registry.For(Named("Innkeeper", UnitFactionTypes.Townsfolk), onDungeonMap: false, advertisesQuests: true));
        Assert.Equal(DefaultCreatureScript.Instance,
            registry.For(Named("Test Guard Chief", UnitFactionTypes.Hostile), onDungeonMap: false, advertisesQuests: false));
    }

    [Fact]
    public void DungeonMapsAreTheMapsWithARunToFinish()
    {
        var registry = new ScriptRegistry(Rules);

        Assert.True(registry.IsDungeonMap(14));
        Assert.False(registry.IsDungeonMap(1));
    }
}

public sealed class InstanceScriptTests
{
    private static readonly IReadOnlyList<DungeonDefinition> Rules =
        [new DungeonDefinition(14, "The Prison", [2666, 2668])];

    [Fact]
    public void TheRunFinishesOnceEveryNamedBossIsDead()
    {
        var script = new ScriptRegistry(Rules).ForMap(14);

        var halfway = script.Progress(new DungeonProgressContext(14, [2666, 2668], [2668], []));
        Assert.False(halfway.Completed);

        var done = script.Progress(new DungeonProgressContext(14, [2666, 2668], [2666, 2668], []));
        Assert.True(done.Completed);
        Assert.Equal("The Prison", done.Name);
    }

    [Fact]
    public void ABossNobodyHasSeenHoldsTheRunUp()
    {
        var script = new ScriptRegistry(Rules).ForMap(14);

        var stillRunning = script.Progress(new DungeonProgressContext(14, [2666], [2666], []));
        Assert.False(stillRunning.Completed);
    }

    [Fact]
    public void ABossKilledEarlierInTheRunStillCounts()
    {
        var script = new ScriptRegistry(Rules).ForMap(14);

        var done = script.Progress(new DungeonProgressContext(
            14, [2668], [2668], [DungeonMilestones.BossDown(2666)]));

        Assert.True(done.Completed);
    }

    [Fact]
    public void AnEmptyCopyFinishesNothing()
    {
        var script = new ScriptRegistry(Rules).ForMap(14);

        Assert.False(script.Progress(new DungeonProgressContext(14, [], [], [])).Completed);
    }
}

public sealed class DungeonMilestoneTests
{
    [Fact]
    public void ABossDownIsNamedTheSameWayContentSpellsIt()
    {
        Assert.Equal("boss.2668.down", DungeonMilestones.BossDown(2668));
    }

    [Fact]
    public void EveryBossDownIsReachedAsItsOwnMilestone()
    {
        var script = new ScriptRegistry(
            [new DungeonDefinition(14, "The Prison", [2666, 2668, 2667])]).ForMap(14);

        var halfway = script.Progress(new DungeonProgressContext(14, [2666, 2668], [2666], []));

        Assert.Contains(DungeonMilestones.BossDown(2666), halfway.Reached);
        Assert.DoesNotContain(DungeonMilestones.BossDown(2668), halfway.Reached);
        Assert.False(halfway.Completed);
    }

    [Fact]
    public void FinishingTheRunReachesTheFinishAlongsideEveryBoss()
    {
        var script = new ScriptRegistry(
            [new DungeonDefinition(14, "The Prison", [2666, 2668])]).ForMap(14);

        var done = script.Progress(new DungeonProgressContext(14, [2666, 2668], [2666, 2668], []));

        Assert.True(done.Completed);
        Assert.Contains(DungeonMilestones.BossDown(2666), done.Reached);
        Assert.Contains(DungeonMilestones.BossDown(2668), done.Reached);
        Assert.Contains(InstanceService.RunCompleted, done.Reached);
    }
}
