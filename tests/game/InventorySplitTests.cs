
namespace Ubiquitous.Game.Tests;

public sealed class InventorySplitTests
{
    [Fact]
    public void Split_ReducesSourceAndAddsNewItem()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("item", 100, 5, 0));

        var result = inventory.Split("item", new StorageSlotState("bag", 0), 2, new StorageSlotState("bag", 3), 12);

        Assert.True(result.Accepted);
        Assert.Equal(3, result.Source!.Count);
        Assert.Equal(2, result.Added!.Count);
    }

    [Fact]
    public void Split_IntoAnOccupiedSlot_LandsInTheFirstEmptyOne()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("item", 100, 5, 0));
        inventory.Seed(new InventoryItemState("other", 200, 1, 1));

        var result = inventory.Split("item", new StorageSlotState("bag", 0), 2, new StorageSlotState("bag", 1), 12);

        Assert.Equal(2, result.Added!.Slot);
    }

    [Fact]
    public void Split_WithNoEmptySlot_IsRefused()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("item", 100, 5, 0));
        inventory.Seed(new InventoryItemState("other", 200, 1, 1));

        var result = inventory.Split("item", new StorageSlotState("bag", 0), 2, new StorageSlotState("bag", 1), 2);

        Assert.False(result.Accepted);
        Assert.Equal(5, inventory.Items.Single(item => item.Id == "item").Count);
    }
}
