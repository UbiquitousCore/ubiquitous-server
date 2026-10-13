namespace Ubiquitous.Data.Tests;

public class QuestClassGateTests
{
    private static QuestDefinition Quest(uint allowedClassMask) =>
        new(5001, 9001, 12001, 12002, 1, AllowedClassMask: allowedClassMask);

    [Fact]
    public void Allows_EverythingWhenTheQuestHasNoAllowedClassMask()
    {
        var quest = Quest(allowedClassMask: 0);

        Assert.True(QuestClassGate.Allows(quest, playerClassId: 0));
        Assert.True(QuestClassGate.Allows(quest, playerClassId: 3));
        Assert.True(QuestClassGate.Allows(quest, playerClassId: null));
    }

    [Fact]
    public void Allows_TheClassWhoseBitIsSetInTheMask()
    {
        var quest = Quest(allowedClassMask: 0b0110);

        Assert.True(QuestClassGate.Allows(quest, playerClassId: 1));
        Assert.True(QuestClassGate.Allows(quest, playerClassId: 2));
    }

    [Fact]
    public void Allows_DeniesAClassWhoseBitIsNotSet()
    {
        var quest = Quest(allowedClassMask: 0b0110);

        Assert.False(QuestClassGate.Allows(quest, playerClassId: 0));
        Assert.False(QuestClassGate.Allows(quest, playerClassId: 3));
    }

    [Fact]
    public void Allows_TreatsNoSuppliedClassAsUnrestricted()
    {
        var quest = Quest(allowedClassMask: 0b0110);

        Assert.True(QuestClassGate.Allows(quest, playerClassId: null));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(32)]
    public void Allows_DeniesAClassIdOutsideTheMasksThirtyTwoBits(int classId)
    {
        var quest = Quest(allowedClassMask: 0xFFFFFFFF);

        Assert.False(QuestClassGate.Allows(quest, classId));
    }
}
