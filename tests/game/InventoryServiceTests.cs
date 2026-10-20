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

        var added = Assert.Single(inventory.Add(200, 3, 16)!);

        Assert.Equal(1, added.Slot);
        Assert.Equal(3, added.Count);
    }

    [Fact]
    public void Add_TopsUpPartStacksBeforeOpeningASlot()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("scraps", 300, 18, 0));

        var touched = inventory.Add(300, 5, 16, stackLimit: 20)!;

        Assert.Equal([("scraps", 20, 0), (touched[1].Id, 3, 1)], touched.Select(item => (item.Id, item.Count, item.Slot)));
        Assert.Equal(2, inventory.Items.Count);
    }

    [Fact]
    public void Add_NeverMakesAStackOverItsLimit()
    {
        var inventory = new InventoryService();

        var touched = inventory.Add(300, 45, 16, stackLimit: 20)!;

        Assert.Equal([20, 20, 5], touched.Select(item => item.Count));
    }

    [Fact]
    public void Add_TakesNothingWhenThePartThatDoesNotStackHasNoRoom()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("scraps", 300, 18, 0));
        inventory.Seed(new InventoryItemState("other", 200, 1, 1));

        Assert.Null(inventory.Add(300, 5, 2, stackLimit: 20));
        Assert.Equal(18, inventory.Items.Single(item => item.Id == "scraps").Count);
        Assert.Single(inventory.Add(300, 2, 2, stackLimit: 20)!);
    }

    [Fact]
    public void Combine_PoursADroppedStackIntoAPartStackOfTheSameItem()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("a", 300, 15, 0));
        inventory.Seed(new InventoryItemState("b", 300, 8, 4, "bank"));

        var all = inventory.Combine(new StorageSlotState("bank", 4), new StorageSlotState("bag", 0), 20)!;

        Assert.Equal(20, all.Into.Count);
        Assert.Equal(3, all.Left!.Count);
        Assert.Null(all.EmptiedId);

        var rest = inventory.Combine(new StorageSlotState("bank", 4), new StorageSlotState("bag", 0), 30)!;
        Assert.Equal(23, rest.Into.Count);
        Assert.Equal("b", rest.EmptiedId);
        Assert.Single(inventory.Items);
    }

    [Fact]
    public void Combine_LeavesDifferentItemsAndFullStacksToAnOrdinaryMove()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("a", 300, 20, 0));
        inventory.Seed(new InventoryItemState("b", 300, 5, 1));
        inventory.Seed(new InventoryItemState("c", 400, 5, 2));

        Assert.Null(inventory.Combine(new StorageSlotState("bag", 1), new StorageSlotState("bag", 0), 20));
        Assert.Null(inventory.Combine(new StorageSlotState("bag", 1), new StorageSlotState("bag", 2), 20));
        Assert.Null(inventory.Combine(new StorageSlotState("bag", 1), new StorageSlotState("bag", 2), 1));
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
