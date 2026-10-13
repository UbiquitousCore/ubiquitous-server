using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class QuestObjectiveTests
{
    private static readonly QuestObjectiveDefinition KillTwoWolves =
        new(0, QuestObjectiveKind.Killed, GoalCount: 2, ObjectiveCreature: 9001);

    private static readonly QuestDefinition[] KillQuest =
    [
        new(5001, 9002, 12001, 12002, 1, [KillTwoWolves]),
    ];

    [Fact]
    public void Accept_StartsEveryObjectiveAtZero()
    {
        var service = new QuestService();

        var result = service.Accept(5001, KillQuest);

        var objective = Assert.Single(result!.Objectives);
        Assert.Equal(0, objective.Count);
        Assert.Equal(2, objective.GoalCount);
        Assert.False(result.ReadyToHandIn);
    }

    [Fact]
    public void RecordKill_IncrementsAMatchingObjectiveAndReportsTheDelta()
    {
        var service = new QuestService();
        service.Accept(5001, KillQuest);

        var events = service.RecordKill(9001, KillQuest);

        var update = Assert.Single(events);
        Assert.Equal(5001u, update.QuestId);
        var counter = Assert.Single(update.Counters);
        Assert.Equal(QuestObjectiveKind.Killed, counter.Kind);
        Assert.Equal(1, counter.Count);
        Assert.False(update.ReadyToHandIn);
    }

    [Fact]
    public void RecordKill_MarksTheQuestFinishableOnceEveryObjectiveIsMet()
    {
        var service = new QuestService();
        service.Accept(5001, KillQuest);

        service.RecordKill(9001, KillQuest);
        var second = service.RecordKill(9001, KillQuest).Single();

        Assert.True(second.ReadyToHandIn);
        Assert.Equal(2, second.Objectives.Single().Count);
    }

    [Fact]
    public void RecordKill_DoesNotOverflowPastTheGoalCount()
    {
        var service = new QuestService();
        service.Accept(5001, KillQuest);
        service.RecordKill(9001, KillQuest);
        service.RecordKill(9001, KillQuest);

        Assert.Empty(service.RecordKill(9001, KillQuest));
    }

    [Fact]
    public void RecordKill_IgnoresAKillThatMatchesNoAcceptedQuest()
    {
        var service = new QuestService();
        service.Accept(5001, KillQuest);

        Assert.Empty(service.RecordKill(4242, KillQuest));
    }

    [Fact]
    public void RecordKill_IgnoresAQuestThatWasNeverAccepted()
    {
        var service = new QuestService();

        Assert.Empty(service.RecordKill(9001, KillQuest));
    }

    private static readonly QuestObjectiveDefinition InteractWithSignpost =
        new(0, QuestObjectiveKind.Triggered, GoalCount: 1, ObjectiveObject: 700);

    private static readonly QuestDefinition[] InteractQuest =
    [
        new(5003, 9002, 12001, 12002, 1, [InteractWithSignpost]),
    ];

    [Fact]
    public void RecordInteraction_IncrementsAMatchingObjectiveAndReportsTheDelta()
    {
        var service = new QuestService();
        service.Accept(5003, InteractQuest);

        var events = service.RecordInteraction(700, InteractQuest);

        var update = Assert.Single(events);
        Assert.Equal(5003u, update.QuestId);
        var counter = Assert.Single(update.Counters);
        Assert.Equal(QuestObjectiveKind.Triggered, counter.Kind);
        Assert.Equal(1, counter.Count);
        Assert.True(update.ReadyToHandIn);
    }

    [Fact]
    public void RecordInteraction_DoesNotOverflowPastTheGoalCount()
    {
        var service = new QuestService();
        service.Accept(5003, InteractQuest);
        service.RecordInteraction(700, InteractQuest);

        Assert.Empty(service.RecordInteraction(700, InteractQuest));
    }

    [Fact]
    public void RecordInteraction_IgnoresAnInteractionThatMatchesNoAcceptedQuest()
    {
        var service = new QuestService();
        service.Accept(5003, InteractQuest);

        Assert.Empty(service.RecordInteraction(4242, InteractQuest));
    }

    [Fact]
    public void RecordInteraction_IgnoresAQuestThatWasNeverAccepted()
    {
        var service = new QuestService();

        Assert.Empty(service.RecordInteraction(700, InteractQuest));
    }

    [Fact]
    public void RecordKill_CreditsEveryAcceptedQuestThatSharesTheSameTarget()
    {
        var quests = new[]
        {
            new QuestDefinition(5001, 9002, 12001, 12002, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, 1, ObjectiveCreature: 9001)]),
            new QuestDefinition(5002, 9002, 13001, 13002, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, 1, ObjectiveCreature: 9001)]),
        };
        var service = new QuestService();
        service.Accept(5001, quests);
        service.Accept(5002, quests);

        var events = service.RecordKill(9001, quests);

        Assert.Equal(2, events.Count);
        Assert.Contains(events, update => update.QuestId == 5001 && update.ReadyToHandIn);
        Assert.Contains(events, update => update.QuestId == 5002 && update.ReadyToHandIn);
    }

    [Fact]
    public void Complete_RefusesAQuestWhoseObjectivesAreNotYetMet()
    {
        var service = new QuestService();
        service.Accept(5001, KillQuest);

        Assert.Null(service.Complete(5001, 0, KillQuest));

        service.RecordKill(9001, KillQuest);
        service.RecordKill(9001, KillQuest);
        Assert.NotNull(service.Complete(5001, 0, KillQuest));
    }

    [Fact]
    public void Complete_StillWorksImmediatelyForAQuestWithNoObjectives()
    {
        var quests = new[] { new QuestDefinition(5001, 9002, 12001, 12002, 1) };
        var service = new QuestService();
        service.Accept(5001, quests);

        Assert.NotNull(service.Complete(5001, 0, quests));
    }

    [Fact]
    public void Log_ReturnsEveryAcceptedQuestInSlotOrderWithItsCurrentProgress()
    {
        var quests = new[]
        {
            new QuestDefinition(5001, 9002, 12001, 12002, 1),
            new QuestDefinition(5002, 9002, 13001, 13002, 1, [KillTwoWolves]),
        };
        var service = new QuestService();
        service.Accept(5002, quests);
        service.Accept(5001, quests);
        service.RecordKill(9001, quests);

        var log = service.Log(quests);

        Assert.Equal(2, log.Count);
        Assert.Equal(5002u, log[0].QuestId);
        Assert.Equal(0, log[0].Slot);
        Assert.Equal(1, log[0].Objectives.Single().Count);
        Assert.Equal(5001u, log[1].QuestId);
        Assert.Equal(1, log[1].Slot);
        Assert.True(log[1].ReadyToHandIn);
    }

    [Fact]
    public void Log_OmitsAQuestThatWasNeverAccepted()
    {
        var service = new QuestService();

        Assert.Empty(service.Log(KillQuest));
    }

    private static readonly QuestDefinition[] HeldQuest =
    [
        new(5001, 9002, 12001, 12002, 1,
            [new QuestObjectiveDefinition(0, QuestObjectiveKind.Held, GoalCount: 3, ObjectiveItem: 200)]),
    ];

    [Fact]
    public void Accept_CreditsItemsAlreadyHeldWhenTheCallerReportsThem()
    {
        var service = new QuestService();

        var result = service.Accept(5001, HeldQuest, heldCountFor: _ => 2);

        Assert.Equal(2, result!.Objectives.Single().Count);
        Assert.False(result.ReadyToHandIn);
    }

    [Fact]
    public void Accept_StartsAtZeroWithoutAHeldCountFunction()
    {
        var service = new QuestService();

        var result = service.Accept(5001, HeldQuest);

        Assert.Equal(0, result!.Objectives.Single().Count);
    }

    [Fact]
    public void RefreshHeldObjectives_MovesAnObjectiveUpAndDownWithWhatIsActuallyHeld()
    {
        var service = new QuestService();
        service.Accept(5001, HeldQuest);

        var up = service.RefreshHeldObjectives(_ => 2, HeldQuest).Single();
        Assert.Equal(2, up.Objectives.Single().Count);
        Assert.False(up.ReadyToHandIn);

        var down = service.RefreshHeldObjectives(_ => 1, HeldQuest).Single();
        Assert.Equal(1, down.Objectives.Single().Count);
    }

    [Fact]
    public void RefreshHeldObjectives_CapsAtTheRequiredCountEvenWhenMoreAreHeld()
    {
        var service = new QuestService();
        service.Accept(5001, HeldQuest);

        var result = service.RefreshHeldObjectives(_ => 99, HeldQuest).Single();

        Assert.Equal(3, result.Objectives.Single().Count);
        Assert.True(result.ReadyToHandIn);
    }

    [Fact]
    public void RefreshHeldObjectives_ReportsNothingWhenTheHeldCountHasNotChanged()
    {
        var service = new QuestService();
        service.Accept(5001, HeldQuest, heldCountFor: _ => 2);

        Assert.Empty(service.RefreshHeldObjectives(_ => 2, HeldQuest));
    }

    [Fact]
    public void RefreshHeldObjectives_IgnoresAQuestThatWasNeverAccepted()
    {
        var service = new QuestService();

        Assert.Empty(service.RefreshHeldObjectives(_ => 5, HeldQuest));
    }

    [Fact]
    public void Complete_RefusesAHeldQuestUntilEnoughIsCarried()
    {
        var service = new QuestService();
        service.Accept(5001, HeldQuest);

        Assert.Null(service.Complete(5001, 0, HeldQuest));

        service.RefreshHeldObjectives(_ => 3, HeldQuest);
        Assert.NotNull(service.Complete(5001, 0, HeldQuest));
    }

    private static readonly QuestDefinition[] CastQuest =
    [
        new(5014, 9013, 26920, 26921, 0,
            [new QuestObjectiveDefinition(
                0, QuestObjectiveKind.Triggered, GoalCount: 1,
                ObjectiveStringId: 26927, ObjectiveSpell: 5692)]),
    ];

    [Fact]
    public void RecordSpellCast_AdvancesAndFinishesASpellObjective()
    {
        var service = new QuestService();
        service.Accept(5014, CastQuest);

        var update = Assert.Single(service.RecordSpellCast(5692, "Creature:1", CastQuest));

        Assert.Equal(5014u, update.QuestId);
        var counter = Assert.Single(update.Counters);
        Assert.Equal(QuestObjectiveKind.Triggered, counter.Kind);
        Assert.Equal(1, counter.Count);
        Assert.True(update.ReadyToHandIn);
        Assert.NotNull(service.Complete(5014, 0, CastQuest));
    }

    [Fact]
    public void RecordSpellCast_IgnoresADifferentSpell()
    {
        var service = new QuestService();
        service.Accept(5014, CastQuest);

        Assert.Empty(service.RecordSpellCast(100, "Creature:1", CastQuest));
    }

    [Fact]
    public void RecordSpellCast_AcceptsAnyTargetWhenTheSlotNamesNone()
    {
        var service = new QuestService();
        service.Accept(5014, CastQuest);

        Assert.Single(service.RecordSpellCast(5692, "Creature:99", CastQuest));
    }

    [Fact]
    public void RecordSpellCast_CountsEachTargetOnlyOnce()
    {
        var threeCasts = new QuestDefinition[]
        {
            new(5014, 9013, 26920, 26921, 0,
                [new QuestObjectiveDefinition(
                    0, QuestObjectiveKind.Triggered, GoalCount: 3, ObjectiveSpell: 5692)]),
        };
        var service = new QuestService();
        service.Accept(5014, threeCasts);

        Assert.Single(service.RecordSpellCast(5692, "Creature:1", threeCasts));
        Assert.Empty(service.RecordSpellCast(5692, "Creature:1", threeCasts));

        var second = Assert.Single(service.RecordSpellCast(5692, "Creature:2", threeCasts));
        Assert.Equal(2, second.Counters.Single().Count);
        Assert.False(second.ReadyToHandIn);
    }

    [Fact]
    public void RecordSpellCast_LeavesAStaticObjectSlotToTheInteractionDriver()
    {
        var boundQuest = new QuestDefinition[]
        {
            new(5001, 9002, 12001, 12002, 0,
                [new QuestObjectiveDefinition(
                    0, QuestObjectiveKind.Triggered, GoalCount: 1,
                    ObjectiveObject: 700, ObjectiveSpell: 5692)]),
        };
        var service = new QuestService();
        service.Accept(5001, boundQuest);

        Assert.Empty(service.RecordSpellCast(5692, "Creature:1", boundQuest));
        Assert.Single(service.RecordInteraction(700, boundQuest));
    }

    [Fact]
    public void RecordSpellCast_IgnoresAQuestThatWasNeverAccepted()
    {
        Assert.Empty(new QuestService().RecordSpellCast(5692, "Creature:1", CastQuest));
    }
}
