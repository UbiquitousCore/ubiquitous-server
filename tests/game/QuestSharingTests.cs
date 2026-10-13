using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class QuestSharingTests
{
    private static readonly QuestDefinition[] Quests =
    [
        new(5001, 9001, 12001, 12002, 1),
        new(5002, 9001, 12003, 12004, 10),
        new(5003, 9001, 12005, 12006, 1, AllowedClassMask: 0b0010),
        new(5004, 9001, 12007, 12008, 1, PrerequisitesAll: [5001]),
    ];

    [Fact]
    public void A_QuestNotYetHeld_CanBeTaken()
    {
        Assert.Equal(QuestTakeFit.Takes, new QuestService().CanTake(5001, Quests, playerLevel: 5, playerClassId: 1));
    }

    [Fact]
    public void A_QuestAlreadyHeld_OrHandedIn_IsAlreadyHad()
    {
        var holding = new QuestService();
        holding.Accept(5001, Quests);
        var finished = new QuestService();
        finished.Load([], [5001u]);

        Assert.Equal(QuestTakeFit.AlreadyHas, holding.CanTake(5001, Quests));
        Assert.Equal(QuestTakeFit.AlreadyHas, finished.CanTake(5001, Quests));
    }

    [Fact]
    public void Level_Class_AndAnUnfinishedEarlierQuest_EachMakeItNotEligible()
    {
        var service = new QuestService();

        Assert.Equal(QuestTakeFit.NotEligible, service.CanTake(5002, Quests, playerLevel: 5));
        Assert.Equal(QuestTakeFit.NotEligible, service.CanTake(5003, Quests, playerClassId: 0));
        Assert.Equal(QuestTakeFit.NotEligible, service.CanTake(5004, Quests));
        Assert.Equal(QuestTakeFit.NotEligible, service.CanTake(9999, Quests));
    }

    [Fact]
    public void Holds_IsTrueOnlyForAQuestInTheLog()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);

        Assert.True(service.Holds(5001));
        Assert.False(service.Holds(5002));
    }
}
