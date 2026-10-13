namespace Ubiquitous.Data.Tests;

public class QuestPrerequisiteGateTests
{
    private static QuestDefinition Quest(uint[]? prerequisitesAll = null, uint[]? anyOfQuestIds = null) =>
        new(5001, 9001, 12001, 12002, 1,
            PrerequisitesAll: prerequisitesAll, AnyOfQuestIds: anyOfQuestIds);

    [Fact]
    public void Allows_EverythingWhenTheQuestHasNoPrerequisite()
    {
        var quest = Quest();

        Assert.True(QuestPrerequisiteGate.Allows(quest, completedQuestIds: []));
        Assert.True(QuestPrerequisiteGate.Allows(quest, completedQuestIds: [999]));
    }

    [Fact]
    public void Allows_TreatsNoSuppliedCompletedSetAsUnrestricted()
    {
        var quest = Quest(prerequisitesAll: [6]);

        Assert.True(QuestPrerequisiteGate.Allows(quest, completedQuestIds: null));
    }

    [Fact]
    public void Allows_DeniesAQuestWhoseRequiredQuestIsNotYetFinished()
    {
        var quest = Quest(prerequisitesAll: [6]);

        Assert.False(QuestPrerequisiteGate.Allows(quest, completedQuestIds: []));
        Assert.False(QuestPrerequisiteGate.Allows(quest, completedQuestIds: [5]));
    }

    [Fact]
    public void Allows_TheQuestOnceItsRequiredQuestIsFinished()
    {
        var quest = Quest(prerequisitesAll: [6]);

        Assert.True(QuestPrerequisiteGate.Allows(quest, completedQuestIds: [6]));
    }

    [Fact]
    public void Allows_OnlyOnceEveryRequiredQuestIsFinished()
    {
        var quest = Quest(prerequisitesAll: [6, 7]);

        Assert.False(QuestPrerequisiteGate.Allows(quest, completedQuestIds: [6]));
        Assert.False(QuestPrerequisiteGate.Allows(quest, completedQuestIds: [7]));
        Assert.True(QuestPrerequisiteGate.Allows(quest, completedQuestIds: [6, 7]));
    }

    [Fact]
    public void Allows_AnAnyOfQuestOnceAnySingleOneIsFinished()
    {
        var quest = Quest(anyOfQuestIds: [537, 538, 639, 640]);

        Assert.False(QuestPrerequisiteGate.Allows(quest, completedQuestIds: []));
        Assert.False(QuestPrerequisiteGate.Allows(quest, completedQuestIds: [999]));
        Assert.True(QuestPrerequisiteGate.Allows(quest, completedQuestIds: [639]));
    }

    [Fact]
    public void Allows_OnlyWhenBothTheAndAndTheOrGroupsAreSatisfied()
    {
        var quest = Quest(prerequisitesAll: [6], anyOfQuestIds: [537, 538]);

        Assert.False(QuestPrerequisiteGate.Allows(quest, completedQuestIds: [6]));
        Assert.False(QuestPrerequisiteGate.Allows(quest, completedQuestIds: [537]));
        Assert.True(QuestPrerequisiteGate.Allows(quest, completedQuestIds: [6, 537]));
    }
}
