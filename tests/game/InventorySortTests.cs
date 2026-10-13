namespace Ubiquitous.Game.Tests;

public sealed class InventorySortTests
{
    [Fact]
    public void Sort_PacksItemsByEntryThenOriginalSlotStartingAtZero()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("c", 300, 1, 5));
        inventory.Seed(new InventoryItemState("a", 100, 1, 4));
        inventory.Seed(new InventoryItemState("b1", 200, 1, 1));
        inventory.Seed(new InventoryItemState("b2", 200, 1, 0));

        var result = inventory.Sort("bag");

        Assert.True(result.Accepted);
        Assert.Equal(["a", "b2", "b1", "c"], result.Items.Select(item => item.Id));
        Assert.Equal([0, 1, 2, 3], result.Items.Select(item => item.Slot));
        Assert.Equal(0, inventory.Items.Single(item => item.Id == "a").Slot);
        Assert.Equal(3, inventory.Items.Single(item => item.Id == "c").Slot);
    }

    [Fact]
    public void Sort_AlsoPacksTheBankIndependentlyOfTheBag()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("bag-item", 100, 1, 5));
        inventory.Seed(new InventoryItemState("bank-item-b", 200, 1, 3, "bank"));
        inventory.Seed(new InventoryItemState("bank-item-a", 100, 1, 1, "bank"));

        var result = inventory.Sort("bank");

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

        var result = inventory.Sort("vault");

        Assert.False(result.Accepted);
        Assert.Equal(0, inventory.Items.Single().Slot);
    }
}
