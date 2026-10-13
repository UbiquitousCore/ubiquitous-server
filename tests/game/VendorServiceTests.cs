using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class VendorServiceTests
{
    private static readonly Dictionary<uint, NpcDefinition> Vendors = new()
    {
        [9001] = new(9001, NpcFlags.Vendor, OffersQuests: true),
    };

    private static readonly IReadOnlyList<VendorTab> Tabs =
    [
        new(0, 0, 1, true, [new VendorItem(0, 100, 5001, 700, 50, 10, 1)]),
    ];

    private static readonly IReadOnlyDictionary<uint, ItemDefinition> NoRealItems =
        new Dictionary<uint, ItemDefinition>();

    [Fact]
    public void Hello_ReturnsTabsForVendorFlaggedNpc()
    {
        var result = new VendorService().Open("Creature:1", 9001, Vendors, Tabs);

        Assert.Equal("Creature:1", result?.NetworkId);
        Assert.Equal(100, result?.Tabs[0].Items[0].Entry);
    }

    [Fact]
    public void Hello_RejectsNpcWithoutVendorFlag()
    {
        var questOnly = new Dictionary<uint, NpcDefinition>
        {
            [9001] = new(9001, NpcFlags.None, OffersQuests: true),
        };

        Assert.Null(new VendorService().Open("Creature:1", 9001, questOnly, Tabs));
    }

    [Fact]
    public void Hello_RejectsUnknownEntry()
    {
        Assert.Null(new VendorService().Open("Creature:9", 9999, Vendors, Tabs));
    }

    [Fact]
    public void Buy_AcceptsMatchingRowWithinStockAndPurse()
    {
        var result = new VendorService().Buy("Creature:1", 9001, Vendors, Tabs, 0, 100, 2, 5000);

        Assert.Equal(VendorService.ResultOk, result.Code);
        Assert.Equal(100, result.TotalPrice);
    }

    [Fact]
    public void Buy_RefusesUnknownRow()
    {
        var result = new VendorService().Buy("Creature:1", 9001, Vendors, Tabs, 0, 999, 1, 5000);

        Assert.Equal(VendorService.ResultNotSold, result.Code);
    }

    [Fact]
    public void Buy_IgnoresStock()
    {
        var result = new VendorService().Buy("Creature:1", 9001, Vendors, Tabs, 0, 100, 11, 5000);

        Assert.Equal(VendorService.ResultOk, result.Code);
        Assert.Equal(550, result.TotalPrice);
    }

    [Fact]
    public void Buy_RefusesUnaffordableTotal()
    {
        var result = new VendorService().Buy("Creature:1", 9001, Vendors, Tabs, 0, 100, 2, 50);

        Assert.Equal(VendorService.ResultCantAfford, result.Code);
    }

    [Fact]
    public void Buy_RefusesNonVendor()
    {
        var questOnly = new Dictionary<uint, NpcDefinition>
        {
            [9001] = new(9001, NpcFlags.None, OffersQuests: true),
        };

        var result = new VendorService().Buy("Creature:1", 9001, questOnly, Tabs, 0, 100, 1, 5000);

        Assert.Equal(VendorService.ResultNotVendor, result.Code);
    }

    [Fact]
    public void Sell_FallsBackToHalfTheVendorsOwnBuyPriceWhenNoSellPriceExists()
    {
        var result = new VendorService().Sell("Creature:1", 9001, Vendors, Tabs, NoRealItems, itemEntry: 100, count: 1);

        Assert.Equal(VendorService.ResultOk, result.Code);
        Assert.Equal(25, result.Price);
    }

    [Fact]
    public void Sell_UsesTheItemsOwnSellPriceWhenOneIsSet()
    {
        var realItems = new Dictionary<uint, ItemDefinition>
        {
            [100] = new(100, "Real Item", 0, 0, 0, SellPrice: 17),
        };

        var result = new VendorService().Sell("Creature:1", 9001, Vendors, Tabs, realItems, itemEntry: 100, count: 1);

        Assert.Equal(VendorService.ResultOk, result.Code);
        Assert.Equal(17, result.Price);
    }

    [Fact]
    public void Sell_BuysAnItemTheVendorDoesNotCarry()
    {
        var realItems = new Dictionary<uint, ItemDefinition>
        {
            [4200] = new(4200, "Pelt", 15, 0, 0, BuyPrice: 40, SellPrice: 10),
        };

        var result = new VendorService().Sell("Creature:1", 9001, Vendors, Tabs, realItems, itemEntry: 4200, count: 1);

        Assert.Equal(VendorService.ResultOk, result.Code);
        Assert.Equal(10, result.Price);
    }

    [Fact]
    public void Sell_PricesTheWholeStack()
    {
        var realItems = new Dictionary<uint, ItemDefinition>
        {
            [4200] = new(4200, "Pelt", 15, 0, 0, BuyPrice: 40, SellPrice: 10),
        };

        var result = new VendorService().Sell("Creature:1", 9001, Vendors, Tabs, realItems, itemEntry: 4200, count: 5);

        Assert.Equal(VendorService.ResultOk, result.Code);
        Assert.Equal(50, result.Price);
    }

    [Fact]
    public void Sell_RefusesAnItemWhoseSellPriceIsZero()
    {
        var realItems = new Dictionary<uint, ItemDefinition>
        {
            [100] = new(100, "Quest Thing", 12, 0, 0, BuyPrice: 50, SellPrice: 0),
        };

        var result = new VendorService().Sell("Creature:1", 9001, Vendors, Tabs, realItems, itemEntry: 100, count: 1);

        Assert.Equal(VendorService.ResultCantSell, result.Code);
        Assert.Equal(0, result.Price);
    }

    [Fact]
    public void Sell_RefusesAnItemWithNothingToPriceItFrom()
    {
        var realItems = new Dictionary<uint, ItemDefinition>
        {
            [4300] = new(4300, "Unpriced", 0, 0, 0),
        };

        var result = new VendorService().Sell("Creature:1", 9001, Vendors, Tabs, realItems, itemEntry: 4300, count: 1);

        Assert.Equal(VendorService.ResultCantSell, result.Code);
    }

    [Fact]
    public void Sell_RefusesAnEntryWithNoItemRowAndNoVendorRow()
    {
        var result = new VendorService().Sell("Creature:1", 9001, Vendors, Tabs, NoRealItems, itemEntry: 999, count: 1);

        Assert.Equal(VendorService.ResultCantSell, result.Code);
    }

    [Fact]
    public void Sell_RefusesNonVendor()
    {
        var questOnly = new Dictionary<uint, NpcDefinition>
        {
            [9001] = new(9001, NpcFlags.None, OffersQuests: true),
        };

        var result = new VendorService().Sell("Creature:1", 9001, questOnly, Tabs, NoRealItems, itemEntry: 100, count: 1);

        Assert.Equal(VendorService.ResultNotVendor, result.Code);
    }
}
