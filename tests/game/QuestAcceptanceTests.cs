using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class QuestAcceptanceTests
{
    [Fact]
    public void Accept_ReturnsPartialUpdateAndRejectsDuplicate()
    {
        var service = new QuestService();
        var quests = new[] { new QuestDefinition(5001, 9001, 12001, 12002, 1) };

        var accepted = service.Accept(5001, quests);
        var duplicate = service.Accept(5001, quests);

        Assert.Equal("underway", accepted?.Status);
        Assert.Null(duplicate);
    }

    [Fact]
    public void Accept_RefusesAClassRestrictedQuestFromTheWrongClass()
    {
        var service = new QuestService();
        var quests = new[] { new QuestDefinition(5001, 9001, 12001, 12002, 1, AllowedClassMask: 0b0010) };

        Assert.Null(service.Accept(5001, quests, playerClassId: 0));
    }

    [Fact]
    public void Accept_AllowsAClassRestrictedQuestFromAnAllowedClass()
    {
        var service = new QuestService();
        var quests = new[] { new QuestDefinition(5001, 9001, 12001, 12002, 1, AllowedClassMask: 0b0010) };

        Assert.NotNull(service.Accept(5001, quests, playerClassId: 1));
    }

    [Fact]
    public void Accept_DoesNotGateOnClassWhenNoneIsSupplied()
    {
        var service = new QuestService();
        var quests = new[] { new QuestDefinition(5001, 9001, 12001, 12002, 1, AllowedClassMask: 0b0010) };

        Assert.NotNull(service.Accept(5001, quests));
    }

    [Fact]
    public void Accept_RefusesOnceTheLogIsFull()
    {
        var service = new QuestService();
        var quests = Enumerable.Range(1, QuestService.QuestLogSize + 1)
            .Select(id => new QuestDefinition((uint)id, 9001, 1, 2, 1)).ToArray();

        foreach (var quest in quests.Take(QuestService.QuestLogSize))
            Assert.NotNull(service.Accept(quest.Id, quests));

        Assert.Null(service.Accept(quests[^1].Id, quests));
        Assert.Equal(QuestService.QuestLogSize, service.Accepted.Count);
    }

    [Fact]
    public void Accept_RefusesANonRepeatableQuestAlreadyTurnedIn()
    {
        var service = new QuestService();
        var quests = new[] { new QuestDefinition(5001, 9001, 12001, 12002, 1) };
        service.Accept(5001, quests);
        service.Complete(5001, rewardIndex: 0, quests);

        Assert.Null(service.Accept(5001, quests));
    }

    [Fact]
    public void Accept_AllowsARepeatableQuestAlreadyTurnedIn()
    {
        var service = new QuestService();
        var quests = new[] { new QuestDefinition(5001, 9001, 12001, 12002, 1, IsRepeatable: true) };
        service.Accept(5001, quests);
        service.Complete(5001, rewardIndex: 0, quests);

        Assert.NotNull(service.Accept(5001, quests));
    }

    [Fact]
    public void Accept_RefusesBelowTheQuestsMinLevel()
    {
        var service = new QuestService();
        var quests = new[] { new QuestDefinition(5001, 9001, 12001, 12002, MinLevel: 10) };

        var result = service.Accept(5001, quests, playerLevel: 9);

        Assert.Null(result);
        Assert.Empty(service.Accepted);
    }

    [Fact]
    public void Accept_AllowsExactlyAtTheQuestsMinLevel()
    {
        var service = new QuestService();
        var quests = new[] { new QuestDefinition(5001, 9001, 12001, 12002, MinLevel: 10) };

        var result = service.Accept(5001, quests, playerLevel: 10);

        Assert.NotNull(result);
    }

    [Fact]
    public void Accept_RefusesAQuestWhoseRequiredQuestIsNotYetFinished()
    {
        var service = new QuestService();
        var quests = new[] { new QuestDefinition(5012, 9011, 20177, 20178, 1, PrerequisitesAll: [5011]) };

        Assert.Null(service.Accept(5012, quests));
    }

    [Fact]
    public void Accept_AllowsAQuestOnceItsRequiredQuestIsFinished()
    {
        var service = new QuestService();
        var six = new QuestDefinition(5011, 9011, 20166, 20167, 1);
        var seven = new QuestDefinition(5012, 9011, 20177, 20178, 1, PrerequisitesAll: [5011]);
        var quests = new[] { six, seven };
        service.Accept(5011, quests);
        service.Complete(5011, rewardIndex: 0, quests);

        Assert.NotNull(service.Accept(5012, quests));
    }
}
