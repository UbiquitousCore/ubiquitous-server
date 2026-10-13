using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class QuestCompletionTests
{
    private static readonly QuestDefinition[] Quests = [new(5001, 9001, 12001, 12002, 1)];

    private static readonly Dictionary<uint, QuestRewardDefinition> Rewards = new()
    {
        [5001] = new(5001, 250,
            [new QuestRewardItem(101, 1, 5101, 1)],
            [new QuestRewardItem(102, 1, 5102, 2), new QuestRewardItem(103, 1, 5103, 2)]),
    };

    [Fact]
    public void Complete_RequiresAcceptanceAndTransitionsToFinished()
    {
        var service = new QuestService();

        Assert.Null(service.Complete(5001, 0, Quests));
        service.Accept(5001, Quests);
        var result = service.Complete(5001, 0, Quests);

        Assert.Equal("complete", result?.Update.Status);
    }

    [Fact]
    public void Complete_PaysTheFixedRewardsPlusTheChosenPlinth()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);

        var result = service.Complete(5001, 1, Quests, Rewards);

        Assert.NotNull(result);
        Assert.Equal(250, result.Money);
        Assert.Equal([101u, 103u], result.Items.Select(item => item.Entry));
    }

    [Fact]
    public void Complete_RefusesAPlinthTheQuestDoesNotOffer()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);

        Assert.Null(service.Complete(5001, 2, Quests, Rewards));
        Assert.NotNull(service.Complete(5001, 0, Quests, Rewards));
    }

    [Fact]
    public void Complete_RefusesAHandInThatWouldNotFitInTheBag()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);

        Assert.Null(service.Complete(5001, 0, Quests, Rewards, freeSlots: 1));
        Assert.NotNull(service.Complete(5001, 0, Quests, Rewards, freeSlots: 2));
    }

    [Fact]
    public void Complete_PaysNothingForAQuestWithNoRewardRow()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);

        var result = service.Complete(5001, 0, Quests,
            new Dictionary<uint, QuestRewardDefinition>());

        Assert.NotNull(result);
        Assert.Equal(0, result.Money);
        Assert.Empty(result.Items);
    }

    private static readonly Dictionary<uint, QuestRewardDefinition> TollRewards = new()
    {
        [5001] = new(5001, -100, [], []),
    };

    [Fact]
    public void Complete_RefusesAHandInWhoseTollThePlayerCannotAfford()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);

        Assert.Null(service.Complete(5001, 0, Quests, TollRewards, currentMoney: 99));
        Assert.NotNull(service.Complete(5001, 0, Quests, TollRewards, currentMoney: 100));
    }

    [Fact]
    public void Complete_ChargesTheTollWhenThePlayerCanAffordIt()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);

        var result = service.Complete(5001, 0, Quests, TollRewards, currentMoney: 250);

        Assert.Equal(-100, result!.Money);
    }

    [Fact]
    public void Complete_NeverChecksAffordabilityForAQuestThatPays()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);

        var result = service.Complete(5001, 1, Quests, Rewards, currentMoney: 0);

        Assert.NotNull(result);
    }

    private static readonly QuestDefinition[] LevelledQuests =
        [new(5002, 9001, 12001, 12002, 1, Level: 20)];

    private static readonly Dictionary<uint, QuestRewardDefinition> LevelledRewards = new()
    {
        [5002] = new(5002, 0, [], [], Xp: 1000),
    };

    [Fact]
    public void Complete_PaysQuestXpScaledAgainstThePlayersLevel()
    {
        var service = new QuestService();
        service.Accept(5002, LevelledQuests);

        var result = service.Complete(5002, 0, LevelledQuests, LevelledRewards, playerLevel: 10);

        Assert.Equal(1200, result!.Xp);
    }

    [Fact]
    public void Complete_PaysAFractionOfTheXpToACharacterFarPastTheQuest()
    {
        var service = new QuestService();
        service.Accept(5002, LevelledQuests);

        var result = service.Complete(5002, 0, LevelledQuests, LevelledRewards, playerLevel: 40);

        Assert.Equal(100, result!.Xp);
    }

    [Fact]
    public void Complete_PaysNoXpForARewardRowThatCarriesNone()
    {
        var service = new QuestService();
        service.Accept(5001, Quests);

        Assert.Equal(0, service.Complete(5001, 0, Quests, Rewards, playerLevel: 1)!.Xp);
    }
}
