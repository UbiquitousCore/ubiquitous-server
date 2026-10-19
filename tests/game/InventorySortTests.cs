using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class InventorySortTests
{
    private static ItemDefinition Item(
        uint id, string name, byte group = 0, uint quality = 0, uint worth = 0, uint level = 0, uint stack = 1) =>
        new(id, name, group, 0, 0, SellPrice: worth, Quality: quality, ItemLevel: level, MaxStack: stack);

    private static Func<int, ItemDefinition?> Lookup(params ItemDefinition[] items) =>
        entry => items.FirstOrDefault(item => item.Id == entry);

    [Fact]
    public void Sort_OrdersByGroupThenQualityWorthLevelAndName()
    {
        var describe = Lookup(
            Item(1, "Zeal", group: 2),
            Item(2, "Bolt", group: 0, quality: 3),
            Item(3, "Axe", group: 0, quality: 1, worth: 50),
            Item(4, "Cap", group: 0, quality: 1, worth: 10, level: 9),
            Item(5, "Bow", group: 0, quality: 1, worth: 10, level: 2),
            Item(6, "Arm", group: 0, quality: 1, worth: 10, level: 2));
        var inventory = new InventoryService();
        foreach (var (id, entry, slot) in new[] { ("z", 1, 0), ("b", 2, 1), ("ax", 3, 2), ("c", 4, 3), ("bo", 5, 4), ("ar", 6, 5) })
            inventory.Seed(new InventoryItemState(id, entry, 1, slot));

        var result = inventory.Sort("bag", describe);

        Assert.Equal(["ar", "bo", "c", "ax", "b", "z"], result.Items.Select(item => item.Id));
        Assert.Equal([0, 1, 2, 3, 4, 5], result.Items.Select(item => item.Slot));
    }

    [Fact]
    public void Sort_PacksIntoTheFirstSlotsLeavingEveryEmptySlotAfter()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("a", 1, 1, 9));
        inventory.Seed(new InventoryItemState("b", 2, 1, 4));

        inventory.Sort("bag", Lookup(Item(1, "A"), Item(2, "B")));

        Assert.Equal([0, 1], inventory.Items.OrderBy(item => item.Slot).Select(item => item.Slot));
    }

    [Fact]
    public void Sort_CombinesPartialStacksUpToTheItemsLimitAndDropsTheEmptiedOnes()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("p1", 7, 15, 0));
        inventory.Seed(new InventoryItemState("p2", 7, 12, 3));
        inventory.Seed(new InventoryItemState("p3", 7, 4, 5));

        var result = inventory.Sort("bag", Lookup(Item(7, "Potion", stack: 20)));

        Assert.Equal([20, 11], result.Items.Select(item => item.Count));
        Assert.Equal(["p3"], result.RemovedIds);
        Assert.Equal(31, inventory.Items.Sum(item => item.Count));
        Assert.All(inventory.Items, item => Assert.True(item.Count <= 20));
    }

    [Fact]
    public void Sort_LeavesAStackAlreadyOverItsLimitWhole()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("big", 7, 50, 0));
        inventory.Seed(new InventoryItemState("small", 7, 5, 1));

        var result = inventory.Sort("bag", Lookup(Item(7, "Potion", stack: 20)));

        Assert.Equal([50, 5], result.Items.Select(item => item.Count));
    }

    [Fact]
    public void Sort_NeverCombinesItemsThatDoNotStack()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("s1", 8, 1, 0));
        inventory.Seed(new InventoryItemState("s2", 8, 1, 1));

        var result = inventory.Sort("bag", Lookup(Item(8, "Sword", stack: 0)));

        Assert.Equal(2, result.Items.Count);
        Assert.Empty(result.RemovedIds!);
    }

    [Fact]
    public void Sort_AlsoPacksTheBankIndependentlyOfTheBag()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("bag-item", 100, 1, 5));
        inventory.Seed(new InventoryItemState("bank-item-b", 200, 1, 3, "bank"));
        inventory.Seed(new InventoryItemState("bank-item-a", 100, 1, 1, "bank"));

        var result = inventory.Sort("bank", Lookup(Item(100, "A"), Item(200, "B")));

        Assert.True(result.Accepted);
        Assert.Equal(["bank-item-a", "bank-item-b"], result.Items.Select(item => item.Id));
        Assert.Equal([0, 1], result.Items.Select(item => item.Slot));
        Assert.Equal(5, inventory.Items.Single(item => item.Id == "bag-item").Slot);
    }

    [Fact]
    public void Sort_RefusesAnyAreaOtherThanBagOrBank()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("item", 100, 1, 0));

        var result = inventory.Sort("vault", Lookup());

        Assert.False(result.Accepted);
        Assert.Equal(0, inventory.Items.Single().Slot);
    }
}
