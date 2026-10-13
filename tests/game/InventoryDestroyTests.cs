
namespace Ubiquitous.Game.Tests;

public sealed class InventoryDestroyTests
{
    [Fact]
    public void Destroy_RemovesMatchingItemAndCount()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("item", 100, 1, 0));
        var result = inventory.Destroy("item", new StorageSlotState("bag", 0), 1);
        Assert.True(result.Accepted);
        Assert.Empty(inventory.Items);
    }
}
