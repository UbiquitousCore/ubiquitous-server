namespace Ubiquitous.Data.Tests;

public sealed class EquipmentSlotsTests
{
    [Fact]
    public void Build_PlacesEachItemAtItsOwnSlotIndex()
    {
        var worn = EquipmentSlots.Build(
            (EquipmentSlots.Chest, 3070),
            (EquipmentSlots.Ranged, 4149));

        Assert.Equal(EquipmentSlots.Count, worn.Count);
        Assert.Equal(3070, worn[4]);
        Assert.Equal(4149, worn[15]);
    }

    [Fact]
    public void Build_LeavesEveryUnnamedSlotEmpty_UnlikeAFlatList()
    {
        var worn = EquipmentSlots.Build((EquipmentSlots.Ranged, 4149));

        Assert.Equal(0, worn[EquipmentSlots.Head]);
        Assert.Equal(4149, worn[EquipmentSlots.Ranged]);
    }

    [Fact]
    public void Build_RejectsASlotOutsideTheWornRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EquipmentSlots.Build((EquipmentSlots.Count, 4149)));
    }

    [Fact]
    public void BodySlots_RunFromHeadToToe()
    {
        Assert.Equal(
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
            new[]
            {
                EquipmentSlots.Head, EquipmentSlots.Neck, EquipmentSlots.Shoulder, EquipmentSlots.Hands,
                EquipmentSlots.Chest, EquipmentSlots.Shirt, EquipmentSlots.Back, EquipmentSlots.Legs,
                EquipmentSlots.Feet, EquipmentSlots.Ring1, EquipmentSlots.Ring2,
                EquipmentSlots.Trinket1, EquipmentSlots.Trinket2,
            });
    }

    [Fact]
    public void WeaponSlots_AreTheNumbersTheSpellTablesUse()
    {
        Assert.Equal(13, EquipmentSlots.MainHand);
        Assert.Equal(14, EquipmentSlots.OffHand);
        Assert.Equal(15, EquipmentSlots.Ranged);
        Assert.Equal(16, EquipmentSlots.Count);
    }
}
