namespace Ubiquitous.Game.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public void Seed_DoesNotDuplicateItemIds()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("item", 100, 1, 0));
        inventory.Seed(new InventoryItemState("item", 100, 2, 1));

        Assert.Single(inventory.Items);
        Assert.Equal(1, inventory.Items[0].Count);
    }

    [Fact]
    public void Move_UpdatesBagSlotWhenSourceMatches()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("item", 100, 1, 0));

        var result = inventory.Move(new StorageSlotState("bag", 0), new StorageSlotState("bag", 3), bagCapacity: 12, bankCapacity: 24);

        Assert.True(result.Accepted);
        Assert.Equal(3, inventory.Items[0].Slot);
    }

    [Fact]
    public void Move_OntoAnOccupiedSlotExchangesTheTwo()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("moved", 100, 1, 0));
        inventory.Seed(new InventoryItemState("sitting", 200, 1, 0, "bank"));

        var result = inventory.Move(new StorageSlotState("bag", 0), new StorageSlotState("bank", 0), bagCapacity: 12, bankCapacity: 24);

        Assert.True(result.Accepted);
        Assert.Equal("sitting", result.Displaced?.Id);
        Assert.Equal(("bank", 0), (inventory.Items.Single(one => one.Id == "moved").Area, inventory.Items.Single(one => one.Id == "moved").Slot));
        Assert.Equal(("bag", 0), (inventory.Items.Single(one => one.Id == "sitting").Area, inventory.Items.Single(one => one.Id == "sitting").Slot));
    }

    [Fact]
    public void Move_OntoAnEmptySlotDisplacesNothing()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("item", 100, 1, 0));

        var result = inventory.Move(new StorageSlotState("bag", 0), new StorageSlotState("bag", 3), bagCapacity: 12, bankCapacity: 24);

        Assert.True(result.Accepted);
        Assert.Null(result.Displaced);
    }

    [Fact]
    public void Add_TakesFirstFreeSlot()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("item", 100, 1, 0));

        var added = inventory.Add(200, 3, 16);

        Assert.NotNull(added);
        Assert.Equal(1, added.Slot);
        Assert.Equal(3, added.Count);
    }

    [Fact]
    public void Add_ReturnsNullWhenBagIsFull()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("item", 100, 1, 0));
        inventory.Seed(new InventoryItemState("other", 200, 1, 1));

        Assert.Null(inventory.Add(300, 1, 2));
    }
}
