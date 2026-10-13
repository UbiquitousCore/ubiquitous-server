using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class QuestListServiceTests
{
    [Fact]
    public void List_ReturnsOnlyQuestsBelongingToGiver()
    {
        var result = new QuestListService().List(9001,
            [new QuestDefinition(1, 9001, 10, 11, 1), new QuestDefinition(2, 9002, 20, 21, 1)],
            new Dictionary<uint, NpcDefinition> { [9001] = new(9001, NpcFlags.None, OffersQuests: true) });

        Assert.Single(result!.Quests);
        Assert.Equal((uint)1, result.Quests[0].Id);
    }

    [Fact]
    public void List_HidesAClassRestrictedQuestFromTheWrongClass()
    {
        var result = new QuestListService().List(9001,
            [new QuestDefinition(1, 9001, 10, 11, 1, AllowedClassMask: 0b0010)],
            new Dictionary<uint, NpcDefinition> { [9001] = new(9001, NpcFlags.None, OffersQuests: true) },
            playerClassId: 0);

        Assert.Empty(result!.Quests);
    }

    [Fact]
    public void List_KeepsAClassRestrictedQuestForAnAllowedClass()
    {
        var result = new QuestListService().List(9001,
            [new QuestDefinition(1, 9001, 10, 11, 1, AllowedClassMask: 0b0010)],
            new Dictionary<uint, NpcDefinition> { [9001] = new(9001, NpcFlags.None, OffersQuests: true) },
            playerClassId: 1);

        Assert.Single(result!.Quests);
    }

    [Fact]
    public void List_HidesAQuestAlreadyAccepted()
    {
        var result = new QuestListService().List(9001,
            [new QuestDefinition(1, 9001, 10, 11, 1), new QuestDefinition(2, 9001, 20, 21, 1)],
            new Dictionary<uint, NpcDefinition> { [9001] = new(9001, NpcFlags.None, OffersQuests: true) },
            accepted: [1]);

        Assert.Single(result!.Quests);
        Assert.Equal((uint)2, result.Quests[0].Id);
    }

    [Fact]
    public void List_HidesAQuestAlreadyCompleted()
    {
        var result = new QuestListService().List(9001,
            [new QuestDefinition(1, 9001, 10, 11, 1)],
            new Dictionary<uint, NpcDefinition> { [9001] = new(9001, NpcFlags.None, OffersQuests: true) },
            completed: [1]);

        Assert.Empty(result!.Quests);
    }

    [Fact]
    public void List_HidesAQuestWhoseRequiredQuestIsNotYetFinished()
    {
        var result = new QuestListService().List(9011,
            [
                new QuestDefinition(5011, 9011, 20010, 20011, 1),
                new QuestDefinition(5012, 9011, 20020, 20021, 1, PrerequisitesAll: [5011]),
            ],
            new Dictionary<uint, NpcDefinition> { [9011] = new(9011, NpcFlags.None, OffersQuests: true) },
            completed: []);

        Assert.Single(result!.Quests);
        Assert.Equal((uint)5011, result.Quests[0].Id);
    }

    [Fact]
    public void List_OffersAQuestOnceItsRequiredQuestIsFinished()
    {
        var result = new QuestListService().List(9011,
            [new QuestDefinition(5012, 9011, 20020, 20021, 1, PrerequisitesAll: [5011])],
            new Dictionary<uint, NpcDefinition> { [9011] = new(9011, NpcFlags.None, OffersQuests: true) },
            completed: [5011]);

        Assert.Single(result!.Quests);
        Assert.Equal((uint)5012, result.Quests[0].Id);
    }

    [Fact]
    public void List_HidesAQuestAboveThePlayersLevel()
    {
        var result = new QuestListService().List(9016,
            [new QuestDefinition(7013, 9016, 20240, 20241, MinLevel: 3)],
            new Dictionary<uint, NpcDefinition> { [9016] = new(9016, NpcFlags.None, OffersQuests: true) },
            playerLevel: 1);

        Assert.Empty(result!.Quests);
    }

    [Fact]
    public void List_OffersAQuestOnceThePlayerReachesItsMinLevel()
    {
        var result = new QuestListService().List(9016,
            [new QuestDefinition(7013, 9016, 20240, 20241, MinLevel: 3)],
            new Dictionary<uint, NpcDefinition> { [9016] = new(9016, NpcFlags.None, OffersQuests: true) },
            playerLevel: 3);

        Assert.Single(result!.Quests);
        Assert.Equal((uint)7013, result.Quests[0].Id);
    }
}
