using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class QuestDestroyTests
{
    private static readonly IReadOnlyList<QuestDefinition> Quests =
        [new QuestDefinition(5001, 9001, 12001, 12002, 1)];

    [Fact]
    public void Destroy_RemovesAcceptedQuestWhenSlotMatches()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);

        Assert.True(service.Destroy(0, 5001));
        Assert.False(service.Destroy(0, 5001));
    }

    [Fact]
    public void Destroy_RejectsMismatchedSlot()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);

        Assert.False(service.Destroy(3, 5001));
    }

    [Fact]
    public void Destroy_RejectsUnacceptedQuest()
    {
        Assert.False(new QuestService().Destroy(0, 5001));
    }

    [Fact]
    public void Destroy_AllowsReacceptAfterAbandon()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);
        service.Destroy(0, 5001);

        Assert.NotNull(service.Accept(5001, Quests));
    }
}
