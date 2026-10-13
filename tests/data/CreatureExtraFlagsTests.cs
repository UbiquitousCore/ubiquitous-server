namespace Ubiquitous.Data.Tests;

public sealed class CreatureExtraFlagsTests
{
    [Fact]
    public void IsTrainingDummy_TrueWhenTheFlagBitIsSet()
    {
        var creature = new CreatureDefinition(42, "test-dummy", 1, 83, RawExtraFlags: 1u << 7);

        Assert.True(CreatureExtraFlags.IsTrainingDummy(creature));
    }

    [Fact]
    public void IsTrainingDummy_FalseWithoutTheBit_WhateverTheEntryId()
    {
        var creature = new CreatureDefinition(1226, "test-dummy", 1, 99999, RawExtraFlags: 1u << 15);

        Assert.False(CreatureExtraFlags.IsTrainingDummy(creature));
    }

    [Fact]
    public void IsTrainingDummy_FalseForAPlainCreatureAndForNull()
    {
        Assert.False(CreatureExtraFlags.IsTrainingDummy(new CreatureDefinition(9001, "test-rogue", 2, 109)));
        Assert.False(CreatureExtraFlags.IsTrainingDummy(null));
    }

    [Fact]
    public void IsTrainingDummy_IgnoresUnrelatedBitsInTheSameColumn()
    {
        var creature = new CreatureDefinition(42, "test-boss", 60, 300000, RawExtraFlags: (1u << 6) | (1u << 15));

        Assert.False(CreatureExtraFlags.IsTrainingDummy(creature));
    }
}
