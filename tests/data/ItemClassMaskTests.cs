namespace Ubiquitous.Data.Tests;

public sealed class ItemClassMaskTests
{
    [Fact]
    public void BitFor_IsOneShiftedByTheClassId()
    {
        Assert.Equal(2u, ItemClassMask.BitFor(PlayerClassIds.Warrior));
        Assert.Equal(4u, ItemClassMask.BitFor(PlayerClassIds.Mage));
        Assert.Equal(8u, ItemClassMask.BitFor(PlayerClassIds.Ranger));
        Assert.Equal(16u, ItemClassMask.BitFor(PlayerClassIds.Monk));
    }

    [Fact]
    public void Allows_RefusesTheWarriorOnlyBucklerForARanger()
    {
        Assert.False(ItemClassMask.Allows(2, PlayerClassIds.Ranger));
        Assert.True(ItemClassMask.Allows(2, PlayerClassIds.Warrior));
    }

    [Theory]
    [InlineData(14u)]
    [InlineData(10u)]
    public void Allows_AdmitsARangerForTheDevCharactersWornWeaponMasks(uint mask)
    {
        Assert.True(ItemClassMask.Allows(mask, PlayerClassIds.Ranger));
        Assert.False(ItemClassMask.Allows(mask, PlayerClassIds.Monk));
    }

    [Fact]
    public void Allows_TreatsZeroAndAMissingColumnAsUnrestricted()
    {
        foreach (var playerClass in PlayerClassIds.All)
        {
            Assert.True(ItemClassMask.Allows(0, playerClass));
            Assert.True(ItemClassMask.Allows(null, playerClass));
        }
    }
}
