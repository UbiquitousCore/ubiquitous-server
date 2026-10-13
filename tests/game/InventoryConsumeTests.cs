namespace Ubiquitous.Game.Tests;

public sealed class InventoryConsumeTests
{
    [Fact]
    public void Consume_RemovesAStackEntirelyWhenItExactlyCoversTheCount()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("pelt", 200, 3, 0));

        var result = inventory.Consume(200, 3);

        Assert.True(result.Accepted);
        Assert.Equal("pelt", Assert.Single(result.RemovedItemIds));
        Assert.Empty(result.ReducedItems);
        Assert.Empty(inventory.Items);
    }

    [Fact]
    public void ConsumeOneOf_TakesFromTheStackNamed_NotTheFirstOfItsKind()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("bond-a", 7485, 1, 0));
        inventory.Seed(new InventoryItemState("bond-b", 7485, 1, 1));

        var result = inventory.ConsumeOneOf("bond-b");

        Assert.Equal("bond-b", Assert.Single(result.RemovedItemIds));
        Assert.Equal("bond-a", Assert.Single(inventory.Items).Id);
    }

    [Fact]
    public void ConsumeOneOf_LeavesTheRestOfAStack()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("potion", 20, 3, 0));

        var result = inventory.ConsumeOneOf("potion");

        Assert.Equal(2, Assert.Single(result.ReducedItems).Count);
        Assert.Equal(2, Assert.Single(inventory.Items).Count);
    }

    [Fact]
    public void Consume_ReducesAStackRatherThanRemovingItWhenSomeIsLeftOver()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("pelt", 200, 5, 0));

        var result = inventory.Consume(200, 3);

        Assert.True(result.Accepted);
        Assert.Empty(result.RemovedItemIds);
        var reduced = Assert.Single(result.ReducedItems);
        Assert.Equal(2, reduced.Count);
        Assert.Equal(2, Assert.Single(inventory.Items).Count);
    }

    [Fact]
    public void Consume_SpansMultipleStacksSmallestSlotFirst()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("pelt-a", 200, 2, 0));
        inventory.Seed(new InventoryItemState("pelt-b", 200, 2, 1));

        var result = inventory.Consume(200, 3);

        Assert.True(result.Accepted);
        Assert.Equal("pelt-a", Assert.Single(result.RemovedItemIds));
        var reduced = Assert.Single(result.ReducedItems);
        Assert.Equal("pelt-b", reduced.Id);
        Assert.Equal(1, reduced.Count);
    }

    [Fact]
    public void Consume_RefusesTheWholeRequestWhenNotEnoughAreHeld()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("pelt", 200, 2, 0));

        var result = inventory.Consume(200, 3);

        Assert.False(result.Accepted);
        Assert.Single(inventory.Items);
        Assert.Equal(2, inventory.Items[0].Count);
    }

    [Fact]
    public void Consume_DoesNothingForACountOfZero()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("pelt", 200, 2, 0));

        var result = inventory.Consume(200, 0);

        Assert.True(result.Accepted);
        Assert.Empty(result.RemovedItemIds);
        Assert.Empty(result.ReducedItems);
        Assert.Single(inventory.Items);
    }

    [Fact]
    public void Consume_IgnoresItemsOfADifferentEntry()
    {
        var inventory = new InventoryService();
        inventory.Seed(new InventoryItemState("pelt", 200, 3, 0));
        inventory.Seed(new InventoryItemState("other", 201, 5, 1));

        inventory.Consume(200, 3);

        var remaining = Assert.Single(inventory.Items);
        Assert.Equal("other", remaining.Id);
    }
}
