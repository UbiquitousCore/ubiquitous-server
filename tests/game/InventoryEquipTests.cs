using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class InventoryEquipTests
{
    private const int BagSlots = 12;

    private static readonly Dictionary<int, uint> Types = new()
    {
        [4149] = InventoryTypes.Ranged,
        [3907] = InventoryTypes.OneHand,
        [1053] = InventoryTypes.Chest,
        [1060] = InventoryTypes.Legs,
        [1063] = InventoryTypes.Feet,
        [1213] = InventoryTypes.OffHand,
        [9001] = InventoryTypes.TwoHand,
        [9002] = InventoryTypes.Finger,
        [100] = InventoryTypes.None,
    };

    private static uint TypeOf(int entry) => Types.GetValueOrDefault(entry, InventoryTypes.None);

    private static InventoryService WithBag(params (string Id, int Entry, int Slot)[] items)
    {
        var inventory = new InventoryService();
        foreach (var (id, entry, slot) in items)
            inventory.Seed(new InventoryItemState(id, entry, 1, slot));
        return inventory;
    }

    private static InventoryService Wearing(
        InventoryService inventory, string id, int entry, int slot)
    {
        inventory.Seed(new InventoryItemState(id, entry, 1, slot, InventoryService.EquipmentArea));
        return inventory;
    }

    private static InventoryEquipResult Equip(
        InventoryService inventory, string id, int fromSlot, int? toSlot = null) =>
        inventory.Equip(id, new StorageSlotState("bag", fromSlot), toSlot, TypeOf, BagSlots);

    private static (string, int, int)[] FullBag() =>
        [.. Enumerable.Range(0, BagSlots).Select(slot => ($"filler-{slot}", 100, slot))];

    [Fact]
    public void AnEquippedItemLandsInTheSlotItsOwnTypeNames()
    {
        var inventory = WithBag(("bow", 4149, 0));

        var result = Equip(inventory, "bow", 0);

        Assert.True(result.Accepted);
        Assert.Equal(ItemErrors.Ok, result.Code);
        Assert.Equal(InventoryService.EquipmentArea, result.Item!.Area);
        Assert.Equal(EquipmentSlots.Ranged, result.Item.Slot);
    }

    [Fact]
    public void EquipmentIsAPositionalArrayAndNotAListOfWhatIsWorn()
    {
        var inventory = WithBag(("bow", 4149, 0));

        var equipment = Equip(inventory, "bow", 0).Equipment;

        Assert.Equal(EquipmentSlots.Count, equipment.Count);
        Assert.Equal(4149, equipment[EquipmentSlots.Ranged]);
        Assert.Equal(0, equipment[EquipmentSlots.Head]);
        Assert.Equal(1, equipment.Count(entry => entry != 0));
    }

    [Fact]
    public void EachWornPieceKeepsItsOwnSlotAcrossSeveralEquips()
    {
        var inventory = WithBag(("boots", 1063, 0), ("chest", 1053, 1), ("legs", 1060, 2));

        Equip(inventory, "boots", 0);
        Equip(inventory, "chest", 1);
        var equipment = Equip(inventory, "legs", 2).Equipment;

        Assert.Equal(1053, equipment[EquipmentSlots.Chest]);
        Assert.Equal(1060, equipment[EquipmentSlots.Legs]);
        Assert.Equal(1063, equipment[EquipmentSlots.Feet]);
        Assert.Equal(0, equipment[EquipmentSlots.Head]);
    }

    [Fact]
    public void SomethingWornNowhereIsRefused()
    {
        var inventory = WithBag(("potion", 100, 0));

        var result = Equip(inventory, "potion", 0);

        Assert.False(result.Accepted);
        Assert.Equal(ItemErrors.WrongSlot, result.Code);
        Assert.Equal("bag", inventory.Items[0].Area);
    }

    [Fact]
    public void ANamedSlotThatIsNotAHomeForThatItemIsRefused()
    {
        var inventory = WithBag(("bow", 4149, 0));

        var result = Equip(inventory, "bow", 0, toSlot: EquipmentSlots.Head);

        Assert.False(result.Accepted);
        Assert.Equal(ItemErrors.WrongSlot, result.Code);
        Assert.Equal("bag", inventory.Items[0].Area);
    }

    [Fact]
    public void ANamedSecondRingIsHonouredEvenThoughTheFirstIsFree()
    {
        var inventory = WithBag(("ring", 9002, 0));

        var result = Equip(inventory, "ring", 0, toSlot: EquipmentSlots.Ring2);

        Assert.True(result.Accepted);
        Assert.Equal(EquipmentSlots.Ring2, result.Item!.Slot);
    }

    [Fact]
    public void ReplacingWornGearPutsTheOldPieceInTheSlotTheNewOneCameFrom()
    {
        var inventory = WithBag(("old", 1053, 0), ("new", 1053, 7));
        Equip(inventory, "old", 0);

        var result = Equip(inventory, "new", 7);

        Assert.True(result.Accepted);
        Assert.Equal(EquipmentSlots.Chest, result.Item!.Slot);
        Assert.Equal("bag", result.Displaced!.Area);
        Assert.Equal(7, result.Displaced.Slot);
        Assert.Equal(1053, result.Equipment[EquipmentSlots.Chest]);
    }

    [Fact]
    public void ReplacingWornGearSucceedsEvenWithEveryOtherBagSlotFull()
    {
        var items = FullBag();
        items[3] = ("new", 1053, 3);
        var inventory = Wearing(WithBag(items), "old", 1053, EquipmentSlots.Chest);

        var result = Equip(inventory, "new", 3);

        Assert.True(result.Accepted);
        Assert.Equal(3, result.Displaced!.Slot);
        Assert.Equal("new", inventory.WornAt(EquipmentSlots.Chest)!.Id);
    }

    [Fact]
    public void ATwoHanderIntoAnEmptyMainHandPushesTheOffHandIntoTheBag()
    {
        var inventory = Wearing(WithBag(("greatsword", 9001, 0)), "buckler", 1213, EquipmentSlots.OffHand);

        var result = Equip(inventory, "greatsword", 0);

        Assert.True(result.Accepted);
        Assert.Equal(EquipmentSlots.MainHand, result.Item!.Slot);
        var pushed = Assert.Single(result.AlsoRemoved);
        Assert.Equal("buckler", pushed.Item.Id);
        Assert.Equal("bag", pushed.Item.Area);
        Assert.Equal(InventoryService.EquipmentArea, pushed.From.Area);
        Assert.Equal(EquipmentSlots.OffHand, pushed.From.Index);
        Assert.Equal(0, result.Equipment[EquipmentSlots.OffHand]);
        Assert.Equal(9001, result.Equipment[EquipmentSlots.MainHand]);
    }

    [Fact]
    public void AnOffHandIntoAnEmptySlotPushesOffATwoHandedMainHand()
    {
        var inventory = Wearing(WithBag(("buckler", 1213, 0)), "greatsword", 9001, EquipmentSlots.MainHand);

        var result = Equip(inventory, "buckler", 0);

        Assert.True(result.Accepted);
        Assert.Equal(EquipmentSlots.OffHand, result.Item!.Slot);
        var pushed = Assert.Single(result.AlsoRemoved);
        Assert.Equal("greatsword", pushed.Item.Id);
        Assert.Equal(0, result.Equipment[EquipmentSlots.MainHand]);
    }

    [Fact]
    public void ATwoHanderOverAnOccupiedMainHandStowsTheOffHandFirstAndGivesUpIfItCannot()
    {
        var items = FullBag();
        items[0] = ("greatsword", 9001, 0);
        var inventory = Wearing(
            Wearing(WithBag(items), "knife", 3907, EquipmentSlots.MainHand),
            "buckler", 1213, EquipmentSlots.OffHand);

        var result = Equip(inventory, "greatsword", 0);

        Assert.False(result.Accepted);
        Assert.Equal(ItemErrors.InventoryFull, result.Code);
        Assert.Equal("knife", inventory.WornAt(EquipmentSlots.MainHand)!.Id);
        Assert.Equal("buckler", inventory.WornAt(EquipmentSlots.OffHand)!.Id);
        Assert.Equal("bag", inventory.Items.Single(item => item.Id == "greatsword").Area);
    }

    [Fact]
    public void TakingSomethingOffPutsItInTheFirstFreeBagSlot()
    {
        var inventory = Wearing(WithBag(("filler", 100, 0)), "bow", 4149, EquipmentSlots.Ranged);

        var result = inventory.Unequip("bow",
            new StorageSlotState(InventoryService.EquipmentArea, EquipmentSlots.Ranged), null, BagSlots);

        Assert.True(result.Accepted);
        Assert.Equal("bag", result.Item!.Area);
        Assert.Equal(1, result.Item.Slot);
        Assert.Equal(0, result.Equipment[EquipmentSlots.Ranged]);
    }

    [Fact]
    public void TakingSomethingOffIntoAFullBagIsRefused()
    {
        var inventory = Wearing(WithBag(FullBag()), "bow", 4149, EquipmentSlots.Ranged);

        var result = inventory.Unequip("bow",
            new StorageSlotState(InventoryService.EquipmentArea, EquipmentSlots.Ranged), null, BagSlots);

        Assert.False(result.Accepted);
        Assert.Equal(ItemErrors.InventoryFull, result.Code);
        Assert.NotNull(inventory.WornAt(EquipmentSlots.Ranged));
    }

    [Fact]
    public void TakingSomethingOffOntoAnOccupiedSlotIsRefusedRatherThanSwapped()
    {
        var inventory = Wearing(WithBag(("filler", 100, 4)), "bow", 4149, EquipmentSlots.Ranged);

        var result = inventory.Unequip("bow",
            new StorageSlotState(InventoryService.EquipmentArea, EquipmentSlots.Ranged),
            new StorageSlotState("bag", 4), BagSlots);

        Assert.False(result.Accepted);
        Assert.Equal(ItemErrors.DestinationOccupied, result.Code);
    }

    [Fact]
    public void AStaleSourceSlotIsRefused()
    {
        var inventory = WithBag(("bow", 4149, 0));

        var result = Equip(inventory, "bow", fromSlot: 5);

        Assert.False(result.Accepted);
        Assert.Equal(ItemErrors.ItemNotInSlot, result.Code);
    }

    [Fact]
    public void WornGearAdoptedFromACharacterRecordKeepsItsSlots()
    {
        var inventory = new InventoryService();

        inventory.SeedWorn(EquipmentSlots.Build(
            (EquipmentSlots.MainHand, 3907), (EquipmentSlots.Ranged, 4149)));

        Assert.Equal(3907, inventory.WornAt(EquipmentSlots.MainHand)!.Entry);
        Assert.Equal(4149, inventory.WornAt(EquipmentSlots.Ranged)!.Entry);
        Assert.Null(inventory.WornAt(EquipmentSlots.Head));
        Assert.Equal(4149, inventory.WornEquipment()[EquipmentSlots.Ranged]);
    }

    [Fact]
    public void AdoptingWornGearNeverOverwritesAStoredRow()
    {
        var inventory = Wearing(new InventoryService(), "saved-bow", 4149, EquipmentSlots.Ranged);

        inventory.SeedWorn(EquipmentSlots.Build((EquipmentSlots.Ranged, 9999)));

        Assert.Equal("saved-bow", inventory.WornAt(EquipmentSlots.Ranged)!.Id);
        Assert.Equal(4149, inventory.WornEquipment()[EquipmentSlots.Ranged]);
    }

    [Fact]
    public void WornItemsDoNotCountAgainstBagSpace()
    {
        var inventory = Wearing(WithBag(("filler", 100, 0)), "bow", 4149, EquipmentSlots.Ranged);

        Assert.Equal(BagSlots - 1, inventory.FreeSlots(BagSlots));
    }

    [Fact]
    public void WornItemsSurviveASaveAndLoadRoundTrip()
    {
        var inventory = Wearing(WithBag(("filler", 100, 0)), "bow", 4149, EquipmentSlots.Ranged);

        var reloaded = new InventoryService();
        reloaded.Load(inventory.ToPersisted());

        Assert.Equal(4149, reloaded.WornEquipment()[EquipmentSlots.Ranged]);
        Assert.Equal("bow", reloaded.WornAt(EquipmentSlots.Ranged)!.Id);
    }

    [Fact]
    public void ResettleWorn_MovesGearSavedInAnOldSlotToItsOwnSlot()
    {
        var inventory = Wearing(new InventoryService(), "legs", 1060, EquipmentSlots.Shirt);

        inventory.ResettleWorn(TypeOf);

        Assert.Equal("legs", inventory.WornAt(EquipmentSlots.Legs)?.Id);
        Assert.Null(inventory.WornAt(EquipmentSlots.Shirt));
    }

    [Fact]
    public void ResettleWorn_DropsAStrayCopyOfWhatIsAlreadyWornInItsSlot()
    {
        var inventory = new InventoryService();
        Wearing(inventory, "feet-new", 1063, EquipmentSlots.Feet);
        Wearing(inventory, "feet-old", 1063, EquipmentSlots.Back);

        inventory.ResettleWorn(TypeOf);

        Assert.Equal("feet-new", inventory.WornAt(EquipmentSlots.Feet)?.Id);
        Assert.Null(inventory.WornAt(EquipmentSlots.Back));
        Assert.Single(inventory.ToPersisted());
    }

    [Fact]
    public void ResettleWorn_KeepsADifferentItemItCannotPlace()
    {
        var inventory = new InventoryService();
        Wearing(inventory, "chest", 1053, EquipmentSlots.Chest);
        Wearing(inventory, "other-chest", 1054, EquipmentSlots.Legs);

        inventory.ResettleWorn(entry => entry == 1054 ? InventoryTypes.Chest : TypeOf(entry));

        Assert.Equal(2, inventory.ToPersisted().Count);
    }

    [Fact]
    public void ResettleWorn_LeavesGearOfAnUnknownTypeWhereItIs()
    {
        var inventory = Wearing(new InventoryService(), "mystery", 7777, EquipmentSlots.Shirt);

        inventory.ResettleWorn(TypeOf);

        Assert.Equal("mystery", inventory.WornAt(EquipmentSlots.Shirt)?.Id);
    }
}
