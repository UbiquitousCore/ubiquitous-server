
namespace Ubiquitous.Game.Tests;

public sealed class InventorySplitTests
{
    [Fact]
    public void Split_ReducesSourceAndAddsNewItem()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("item", 100, 5, 0));

        var result = inventory.Split("item", new StorageSlotState("bag", 0), 2, new StorageSlotState("bag", 3));

        Assert.True(result.Accepted);
        Assert.Equal(3, result.Source!.Count);
        Assert.Equal(2, result.Added!.Count);
    }
}
