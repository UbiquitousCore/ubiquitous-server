using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class QuestServiceTests
{
    private static readonly Dictionary<uint, NpcDefinition> Npcs = new()
    {
        [9001] = new(9001, NpcFlags.None, OffersQuests: true),
    };

    private static readonly QuestDefinition[] Quests =
    [
        new(5001, 9001, 12001, 12002, 1),
    ];

    [Fact]
    public void GetStatus_ReturnsNoneForANonQuestGiver()
    {
        var npcs = new Dictionary<uint, NpcDefinition> { [9001] = new(9001, NpcFlags.Vendor) };

        Assert.Null(new QuestService().GetStatus("Creature:1", 9001, npcs, Quests));
    }

    [Fact]
    public void GetStatus_ReturnsAvailableForAQuestGiverWithAnUntakenOffer()
    {
        var result = new QuestService().GetStatus("Creature:1", 9001, Npcs, Quests);

        Assert.Equal(QuestMark.Offered, result?.Status);
    }

    [Fact]
    public void GetStatus_ReturnsNoneForAGiverWithNothingToOffer()
    {
        var result = new QuestService().GetStatus("Creature:1", 9001, Npcs, []);

        Assert.Equal(QuestMark.None, result?.Status);
    }

    [Fact]
    public void GetStatus_ReturnsIncompleteOnceTheQuestIsAcceptedButNotYetFinishable()
    {
        var service = new QuestService();
        var withObjective = new[]
        {
            new QuestDefinition(5001, 9001, 12001, 12002, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 1, ObjectiveCreature: 200)]),
        };
        service.Accept(5001, withObjective);

        var result = service.GetStatus("Creature:1", 9001, Npcs, withObjective);

        Assert.Equal(QuestMark.Underway, result?.Status);
    }

    [Fact]
    public void GetStatus_ReturnsFinishedOnceTheHeldQuestCanBeCompleted()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);

        var result = service.GetStatus("Creature:1", 9001, Npcs, Quests);

        Assert.Equal(QuestMark.ReadyToHandIn, result?.Status);
    }

    [Fact]
    public void GetStatus_PrefersAHeldQuestOverAnUntakenOfferFromTheSameGiver()
    {
        var service = new QuestService();
        var both = new[]
        {
            new QuestDefinition(5001, 9001, 12001, 12002, 1),
            new QuestDefinition(5002, 9001, 13001, 13002, 1),
        };
        service.Accept(5001, both);

        var result = service.GetStatus("Creature:1", 9001, Npcs, both);

        Assert.Equal(QuestMark.ReadyToHandIn, result?.Status);
    }

    [Fact]
    public void Complete_RecordsANonRepeatableQuestAsCompletedButNotARepeatableOne()
    {
        var service = new QuestService();
        var repeatable = new QuestDefinition(5002, 9001, 12001, 12002, 1, IsRepeatable: true);
        service.Accept(5001, Quests);
        service.Accept(5002, [repeatable]);

        service.Complete(5001, rewardIndex: 0, Quests);
        service.Complete(5002, rewardIndex: 0, [repeatable]);

        Assert.Contains(5001u, service.Completed);
        Assert.DoesNotContain(5002u, service.Completed);
    }

    [Fact]
    public void GetStatus_ReturnsNoneForAGiverWhoseOnlyQuestWasAlreadyCompleted()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);
        service.Complete(5001, rewardIndex: 0, Quests);

        var result = service.GetStatus("Creature:1", 9001, Npcs, Quests);

        Assert.Equal(QuestMark.None, result?.Status);
    }

    [Fact]
    public void GetStatus_HidesAClassRestrictedOfferFromAWrongClass()
    {
        var restricted = new[]
        {
            new QuestDefinition(5001, 9001, 12001, 12002, 1, AllowedClassMask: 0b0010),
        };

        var result = new QuestService().GetStatus("Creature:1", 9001, Npcs, restricted, playerClassId: 0);

        Assert.Equal(QuestMark.None, result?.Status);
    }

    [Fact]
    public void GetStatus_OffersAClassRestrictedQuestToAnAllowedClass()
    {
        var restricted = new[]
        {
            new QuestDefinition(5001, 9001, 12001, 12002, 1, AllowedClassMask: 0b0010),
        };

        var result = new QuestService().GetStatus("Creature:1", 9001, Npcs, restricted, playerClassId: 1);

        Assert.Equal(QuestMark.Offered, result?.Status);
    }

    [Fact]
    public void GetStatus_DoesNotHideAnythingWhenNoClassIsSupplied()
    {
        var restricted = new[]
        {
            new QuestDefinition(5001, 9001, 12001, 12002, 1, AllowedClassMask: 0b0010),
        };

        var result = new QuestService().GetStatus("Creature:1", 9001, Npcs, restricted);

        Assert.Equal(QuestMark.Offered, result?.Status);
    }

    [Fact]
    public void GetStatus_ReturnsNoneForAGiverWhoseOnlyOfferIsNotYetUnlockedByItsRequiredQuest()
    {
        var npcs = new Dictionary<uint, NpcDefinition> { [9011] = new(9011, NpcFlags.None, OffersQuests: true) };
        var gated = new[] { new QuestDefinition(5012, 9011, 20177, 20178, 1, PrerequisitesAll: [5011]) };

        var result = new QuestService().GetStatus("Creature:1", 9011, npcs, gated);

        Assert.Equal(QuestMark.None, result?.Status);
    }

    [Fact]
    public void GetStatus_ReturnsAvailableOnceTheRequiredQuestIsFinished()
    {
        var npcs = new Dictionary<uint, NpcDefinition> { [9011] = new(9011, NpcFlags.None, OffersQuests: true) };
        var six = new QuestDefinition(5011, 9011, 20166, 20167, 1);
        var seven = new QuestDefinition(5012, 9011, 20177, 20178, 1, PrerequisitesAll: [5011]);
        var quests = new[] { six, seven };
        var service = new QuestService();
        service.Accept(5011, quests);
        service.Complete(5011, rewardIndex: 0, quests);

        var result = service.GetStatus("Creature:1", 9011, npcs, quests);

        Assert.Equal(QuestMark.Offered, result?.Status);
    }

    [Fact]
    public void GetStatus_HidesAnOfferAboveThePlayersLevel()
    {
        var npcs = new Dictionary<uint, NpcDefinition> { [9016] = new(9016, NpcFlags.None, OffersQuests: true) };
        var tooHigh = new[] { new QuestDefinition(7013, 9016, 20240, 20241, MinLevel: 3) };

        var result = new QuestService().GetStatus("Creature:1", 9016, npcs, tooHigh, playerLevel: 1);

        Assert.Equal(QuestMark.None, result?.Status);
    }

    [Fact]
    public void GetStatus_OffersAQuestOnceThePlayerReachesItsMinLevel()
    {
        var npcs = new Dictionary<uint, NpcDefinition> { [9016] = new(9016, NpcFlags.None, OffersQuests: true) };
        var tooHigh = new[] { new QuestDefinition(7013, 9016, 20240, 20241, MinLevel: 3) };

        var result = new QuestService().GetStatus("Creature:1", 9016, npcs, tooHigh, playerLevel: 3);

        Assert.Equal(QuestMark.Offered, result?.Status);
    }

    [Fact]
    public void GetStatus_DoesNotHideAnythingWhenNoLevelIsSupplied()
    {
        var npcs = new Dictionary<uint, NpcDefinition> { [9016] = new(9016, NpcFlags.None, OffersQuests: true) };
        var tooHigh = new[] { new QuestDefinition(7013, 9016, 20240, 20241, MinLevel: 3) };

        var result = new QuestService().GetStatus("Creature:1", 9016, npcs, tooHigh);

        Assert.Equal(QuestMark.Offered, result?.Status);
    }

    private static readonly Dictionary<uint, NpcDefinition> RangerChainNpcs = new()
    {
        [9012] = new(9012, NpcFlags.Gossip, OffersQuests: true),
        [9013] = new(9013, NpcFlags.Gossip, OffersQuests: true),
    };

    private static readonly QuestDefinition[] RangerChain =
    [
        new(5013, 9012, 27195, 27196, 0, AllowedClassMask: 8),
        new(5014, 9013, 26920, 26921, 0, PrerequisitesAll: [5013]),
    ];

    private static readonly CreatureQuestBindingDefinition[] RangerChainFinishers =
    [
        new(9013, 5013),
        new(9013, 5014),
    ];

    [Fact]
    public void GetStatus_PutsAHeldQuestsMarkOnItsFinisherRatherThanItsGiver()
    {
        var service = new QuestService();
        service.Accept(5013, RangerChain, playerClassId: PlayerClassIds.Ranger);

        var giver = service.GetStatus("Creature:1", 9012, RangerChainNpcs, RangerChain,
            PlayerClassIds.Ranger, playerLevel: 1, RangerChainFinishers, []);
        var finisher = service.GetStatus("Creature:2", 9013, RangerChainNpcs, RangerChain,
            PlayerClassIds.Ranger, playerLevel: 1, RangerChainFinishers, []);

        Assert.Equal(QuestMark.None, giver?.Status);
        Assert.Equal(QuestMark.ReadyToHandIn, finisher?.Status);
    }

    [Fact]
    public void GetStatus_PutsAnUnfinishedHeldQuestsMarkOnItsFinisherToo()
    {
        var chain = new[]
        {
            new QuestDefinition(5013, 9012, 27195, 27196, 0, AllowedClassMask: 8,
                Objectives: [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 1, ObjectiveCreature: 1226)]),
        };
        var service = new QuestService();
        service.Accept(5013, chain, playerClassId: PlayerClassIds.Ranger);

        var giver = service.GetStatus("Creature:1", 9012, RangerChainNpcs, chain,
            PlayerClassIds.Ranger, playerLevel: 1, RangerChainFinishers, []);
        var finisher = service.GetStatus("Creature:2", 9013, RangerChainNpcs, chain,
            PlayerClassIds.Ranger, playerLevel: 1, RangerChainFinishers, []);

        Assert.Equal(QuestMark.None, giver?.Status);
        Assert.Equal(QuestMark.Underway, finisher?.Status);
    }

    [Fact]
    public void GetStatus_MarksAFinisherThatIsNotAQuestGiverAtAll()
    {
        var npcs = new Dictionary<uint, NpcDefinition> { [9013] = new(9013, NpcFlags.Gossip) };
        var service = new QuestService();
        service.Accept(5013, RangerChain, playerClassId: PlayerClassIds.Ranger);

        var result = service.GetStatus("Creature:2", 9013, npcs, RangerChain,
            PlayerClassIds.Ranger, playerLevel: 1, RangerChainFinishers, []);

        Assert.Equal(QuestMark.ReadyToHandIn, result?.Status);
    }

    [Fact]
    public void GetStatus_KeepsMarkingTheGiverForAQuestWithNoFinisherBindingAnywhere()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);

        var result = service.GetStatus("Creature:1", 9001, Npcs, Quests,
            playerClassId: null, playerLevel: null, RangerChainFinishers, []);

        Assert.Equal(QuestMark.ReadyToHandIn, result?.Status);
    }

    [Fact]
    public void GetStatus_NeverMarksAnNpcGiverForAQuestFinishedAtAStaticObject()
    {
        var quests = new[] { new QuestDefinition(5003, 9001, 14001, 14002, 1) };
        var service = new QuestService();
        service.Accept(5003, quests);

        var result = service.GetStatus("Creature:1", 9001, Npcs, quests,
            playerClassId: null, playerLevel: null,
            questFinishersByCreature: [],
            questFinishersByStaticObject: [new StaticObjectQuestBindingDefinition(9101, 5003)]);

        Assert.Equal(QuestMark.None, result?.Status);
    }

    [Fact]
    public void GetStatus_PrefersATurnInMarkOverAnInProgressOneAtTheSameFinisher()
    {
        var quests = new[]
        {
            new QuestDefinition(5013, 9012, 27195, 27196, 0),
            new QuestDefinition(5014, 9013, 26920, 26921, 0,
                Objectives: [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 1, ObjectiveCreature: 1226)]),
        };
        var service = new QuestService();
        service.Accept(5014, quests);
        service.Accept(5013, quests);

        var result = service.GetStatus("Creature:2", 9013, RangerChainNpcs, quests,
            playerClassId: null, playerLevel: null, RangerChainFinishers, []);

        Assert.Equal(QuestMark.ReadyToHandIn, result?.Status);
    }

    [Theory]
    [InlineData(QuestMark.None, "none")]
    [InlineData(QuestMark.Offered, "offered")]
    [InlineData(QuestMark.Underway, "underway")]
    [InlineData(QuestMark.ReadyToHandIn, "complete")]
    public void WireStatus_SendsThisProjectsOwnWords(QuestMark status, string wire)
    {
        Assert.Equal(wire, QuestService.WireStatus(status));
    }

    [Fact]
    public void Load_RestoresAnAcceptedQuestsSlotAndObjectiveProgress()
    {
        var withObjective = new[]
        {
            new QuestDefinition(5001, 9001, 12001, 12002, 1,
                Objectives: [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 5, ObjectiveCreature: 100)]),
        };
        var service = new QuestService();
        service.Load(
            [new PersistedQuestLogEntry(5001, Slot: 3, [new PersistedQuestObjective(0, 2)])],
            completed: []);

        var log = service.Log(withObjective);

        var entry = Assert.Single(log);
        Assert.Equal(5001u, entry.QuestId);
        Assert.Equal(3, entry.Slot);
        Assert.Equal(2, Assert.Single(entry.Objectives).Count);
    }

    [Fact]
    public void Load_RestoresCompletedQuestsSoANonRepeatableOneCannotBeAcceptedAgain()
    {
        var service = new QuestService();
        service.Load([], completed: [5001]);

        Assert.Null(service.Accept(5001, Quests));
        Assert.Contains(5001u, service.Completed);
    }

    [Fact]
    public void Load_PicksTheNextFreeSlotAboveEveryRestoredQuestsOwnSlot()
    {
        var quests = new[]
        {
            new QuestDefinition(5001, 9001, 12001, 12002, 1),
            new QuestDefinition(5002, 9001, 13001, 13002, 1),
        };
        var service = new QuestService();
        service.Load([new PersistedQuestLogEntry(5001, Slot: 4, [])], completed: []);

        var accepted = service.Accept(5002, quests);

        Assert.Equal(5, accepted?.Slot);
    }

    [Fact]
    public void ToPersisted_RoundTripsAcceptedProgressAndCompletedIds()
    {
        var quests = new[]
        {
            new QuestDefinition(5001, 9001, 12001, 12002, 1,
                Objectives: [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 5, ObjectiveCreature: 100)]),
            new QuestDefinition(5002, 9001, 13001, 13002, 1),
        };
        var original = new QuestService();
        original.Accept(5001, quests);
        original.RecordKill(100, quests);
        original.Accept(5002, quests);
        original.Complete(5002, 0, quests);

        var persisted = original.ToPersisted();
        var restored = new QuestService();
        restored.Load(persisted.Accepted, persisted.Completed);

        var entry = Assert.Single(restored.Log(quests));
        Assert.Equal(5001u, entry.QuestId);
        Assert.Equal(1, Assert.Single(entry.Objectives).Count);
        Assert.Contains(5002u, restored.Completed);
        Assert.Null(restored.Accept(5002, quests));
    }
}
