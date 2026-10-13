namespace Ubiquitous.Data.Tests;

public sealed class StartingLoadoutTests
{
    private static CharacterStartingLoadout Loadout(params StartingLoadoutItem[] items) =>
        new(PlayerRaceIds.Human, PlayerClassIds.Ranger, 1, 0f, 0f, 0f, 0f,
            new StartingAttributes(1, 1, 1, 1), items, [], []);

    [Fact]
    public void Item_CountsSlotsBelowTheWornCountAsWorn()
    {
        Assert.True(new StartingLoadoutItem(1053, EquipmentSlots.Chest, 0).IsWorn);
        Assert.True(new StartingLoadoutItem(4149, EquipmentSlots.Ranged, 0).IsWorn);
        Assert.False(new StartingLoadoutItem(1168, EquipmentSlots.Count, 5).IsWorn);
    }

    [Fact]
    public void Item_CountsABagIndexFromTheFirstBagSlot()
    {
        Assert.Equal(0, new StartingLoadoutItem(1168, 16, 5).BagIndex);
        Assert.Equal(1, new StartingLoadoutItem(1168, 17, 5).BagIndex);
        Assert.Equal(3, new StartingLoadoutItem(3008, 19, 5).BagIndex);
    }

    [Fact]
    public void WornEquipment_PlacesEachWornEntryAtItsOwnSlotAndDropsTheCarriedOnes()
    {
        var worn = Loadout(
            new StartingLoadoutItem(1053, EquipmentSlots.Chest, 0),
            new StartingLoadoutItem(4149, EquipmentSlots.Ranged, 0),
            new StartingLoadoutItem(1168, 17, 5)).WornEquipment();

        Assert.Equal(EquipmentSlots.Count, worn.Count);
        Assert.Equal(1053, worn[EquipmentSlots.Chest]);
        Assert.Equal(4149, worn[EquipmentSlots.Ranged]);
        Assert.Equal(0, worn[EquipmentSlots.Head]);
        Assert.DoesNotContain(1168, worn);
    }

    [Fact]
    public void BagItems_KeepsOnlyTheCarriedEntries_InSlotOrder()
    {
        var bag = Loadout(
            new StartingLoadoutItem(3008, 19, 5),
            new StartingLoadoutItem(1053, EquipmentSlots.Chest, 0),
            new StartingLoadoutItem(1168, 17, 5),
            new StartingLoadoutItem(3000, 18, 20)).BagItems;

        Assert.Equal([1168u, 3000u, 3008u], bag.Select(item => item.ItemEntry));
        Assert.Equal([5, 20, 5], bag.Select(item => item.Count));
    }
}
