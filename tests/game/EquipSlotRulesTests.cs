using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class EquipSlotRulesTests
{
    private static uint? Empty(int slot) => null;

    private static Func<int, uint?> Wearing(params (int Slot, uint Type)[] worn) =>
        slot => worn.FirstOrDefault(entry => entry.Slot == slot) is { Type: not 0 } found
            ? found.Type
            : null;

    [Theory]
    [InlineData(InventoryTypes.Head, EquipmentSlots.Head)]
    [InlineData(InventoryTypes.Neck, EquipmentSlots.Neck)]
    [InlineData(InventoryTypes.Shoulder, EquipmentSlots.Shoulder)]
    [InlineData(InventoryTypes.Shirt, EquipmentSlots.Shirt)]
    [InlineData(InventoryTypes.Chest, EquipmentSlots.Chest)]
    [InlineData(InventoryTypes.Legs, EquipmentSlots.Legs)]
    [InlineData(InventoryTypes.Feet, EquipmentSlots.Feet)]
    [InlineData(InventoryTypes.Hands, EquipmentSlots.Hands)]
    [InlineData(InventoryTypes.Back, EquipmentSlots.Back)]
    [InlineData(InventoryTypes.OffHand, EquipmentSlots.OffHand)]
    [InlineData(InventoryTypes.Ranged, EquipmentSlots.Ranged)]
    [InlineData(InventoryTypes.MainHand, EquipmentSlots.MainHand)]
    [InlineData(InventoryTypes.TwoHand, EquipmentSlots.MainHand)]
    public void EachTypeLandsInItsOwnSlot(uint inventoryType, int expected) =>
        Assert.Equal(expected, EquipSlotRules.SlotFor(inventoryType, Empty));

    [Theory]
    [InlineData(InventoryTypes.None)]
    [InlineData(12u)]
    [InlineData(16u)]
    [InlineData(999u)]
    public void TheTypesThatCannotBeWornHaveNoSlot(uint inventoryType)
    {
        Assert.Equal(EquipSlotRules.NoSlot, EquipSlotRules.SlotFor(inventoryType, Empty));
    }

    [Theory]
    [InlineData(InventoryTypes.Finger, EquipmentSlots.Ring1, EquipmentSlots.Ring2)]
    [InlineData(InventoryTypes.Trinket, EquipmentSlots.Trinket1, EquipmentSlots.Trinket2)]
    public void APairedSlotTakesTheFirstFreeOfTheTwo(uint inventoryType, int first, int second)
    {
        Assert.Equal(first, EquipSlotRules.SlotFor(inventoryType, Empty));
        Assert.Equal(second,
            EquipSlotRules.SlotFor(inventoryType, Wearing((first, inventoryType))));
    }

    [Theory]
    [InlineData(InventoryTypes.Finger, EquipmentSlots.Ring1, EquipmentSlots.Ring2)]
    [InlineData(InventoryTypes.Trinket, EquipmentSlots.Trinket1, EquipmentSlots.Trinket2)]
    public void APairedSlotDisplacesTheFirstWhenBothAreFull(uint inventoryType, int first, int second)
    {
        var full = Wearing((first, inventoryType), (second, inventoryType));
        Assert.Equal(first, EquipSlotRules.SlotFor(inventoryType, full));
    }

    [Theory]
    [InlineData(InventoryTypes.Finger, EquipmentSlots.Ring2)]
    [InlineData(InventoryTypes.Trinket, EquipmentSlots.Trinket2)]
    public void AWearerMayStillNameTheSecondOfAPair(uint inventoryType, int second)
    {
        Assert.True(EquipSlotRules.IsValidSlot(inventoryType, second));
    }

    [Fact]
    public void AOneHanderTakesTheMainHandWhenItIsEmpty() =>
        Assert.Equal(EquipmentSlots.MainHand,
            EquipSlotRules.SlotFor(InventoryTypes.OneHand, Empty));

    [Fact]
    public void AOneHanderReplacesATwoHander()
    {
        var twoHanded = Wearing((EquipmentSlots.MainHand, InventoryTypes.TwoHand));
        Assert.Equal(EquipmentSlots.MainHand,
            EquipSlotRules.SlotFor(InventoryTypes.OneHand, twoHanded));
    }

    [Fact]
    public void AOneHanderDisplacesTheOneHanderInTheMainHand()
    {
        var oneHanded = Wearing((EquipmentSlots.MainHand, InventoryTypes.OneHand));

        Assert.Equal(EquipmentSlots.MainHand,
            EquipSlotRules.SlotFor(InventoryTypes.OneHand, oneHanded));
    }

    [Fact]
    public void AOneHanderDisplacesTheMainHandWhenBothHandsAreFull()
    {
        var bothFull = Wearing(
            (EquipmentSlots.MainHand, InventoryTypes.OneHand),
            (EquipmentSlots.OffHand, InventoryTypes.OneHand));
        Assert.Equal(EquipmentSlots.MainHand,
            EquipSlotRules.SlotFor(InventoryTypes.OneHand, bothFull));
    }

    [Fact]
    public void ANamedSlotIsRefusedWhenItIsNotAHomeForThatType()
    {
        Assert.False(EquipSlotRules.IsValidSlot(InventoryTypes.Ranged, EquipmentSlots.Head));
        Assert.True(EquipSlotRules.IsValidSlot(InventoryTypes.Ranged, EquipmentSlots.Ranged));
    }

    [Fact]
    public void AOneHanderCannotBeNamedIntoTheOffHand()
    {
        Assert.False(EquipSlotRules.IsValidSlot(InventoryTypes.OneHand, EquipmentSlots.OffHand));
        Assert.True(EquipSlotRules.IsValidSlot(InventoryTypes.OneHand, EquipmentSlots.MainHand));
    }

    [Fact]
    public void EveryTypeThatNamesASlotAgreesWithItsOwnValidityCheck()
    {
        for (uint type = 0; type <= InventoryTypes.OffHand; type++)
        {
            var slot = EquipSlotRules.SlotFor(type, Empty);
            if (slot == EquipSlotRules.NoSlot) continue;
            Assert.True(EquipSlotRules.IsValidSlot(type, slot),
                $"Inventory type {type} is auto-equipped to slot {slot}, which its own check refuses.");
        }
    }
}
