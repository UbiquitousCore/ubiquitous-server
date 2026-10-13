using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed record VendorStockEvent(string NetworkId, IReadOnlyList<VendorTab> Tabs);
public sealed record VendorBuyResult(string Code, int TotalPrice, int Entry, int Count);
public sealed record VendorSellResult(string Code, int Price, int Entry);

public sealed class VendorService
{
    public const string ResultOk = ItemErrors.Ok;
    public const string ResultBagsFull = ItemErrors.InventoryFull;
    public const string ResultNotSold = ItemErrors.VendorDoesNotSell;
    public const string ResultNotVendor = ItemErrors.NotAVendor;
    public const string ResultCantAfford = ItemErrors.NotEnoughMoney;
    public const string ResultCantSell = ItemErrors.CantSell;

    public VendorStockEvent? Open(
        string networkId,
        uint entryId,
        IReadOnlyDictionary<uint, NpcDefinition> definitions,
        IReadOnlyList<VendorTab> tabs)
    {
        if (!definitions.TryGetValue(entryId, out var npc) || !npc.Flags.HasFlag(NpcFlags.Vendor)) return null;
        return new VendorStockEvent(networkId, tabs);
    }

    public VendorBuyResult Buy(
        string networkId,
        uint entryId,
        IReadOnlyDictionary<uint, NpcDefinition> definitions,
        IReadOnlyList<VendorTab> tabs,
        int slot,
        int entry,
        int count,
        int currency)
    {
        if (!definitions.TryGetValue(entryId, out var npc) || !npc.Flags.HasFlag(NpcFlags.Vendor))
            return new VendorBuyResult(ResultNotVendor, 0, entry, count);
        var item = tabs.SelectMany(tab => tab.Items)
            .FirstOrDefault(candidate => candidate.Slot == slot && candidate.Entry == entry);
        if (item is null || count <= 0)
            return new VendorBuyResult(ResultNotSold, 0, entry, count);
        var totalPrice = checked(item.BuyPrice * count);
        if (totalPrice > currency)
            return new VendorBuyResult(ResultCantAfford, 0, entry, count);
        return new VendorBuyResult(ResultOk, totalPrice, entry, count);
    }

    private const int SellBackNumerator = 1;
    private const int SellBackDenominator = 2;

    public VendorSellResult Sell(
        string networkId,
        uint entryId,
        IReadOnlyDictionary<uint, NpcDefinition> definitions,
        IReadOnlyList<VendorTab> tabs,
        IReadOnlyDictionary<uint, ItemDefinition> items,
        int itemEntry,
        int count)
    {
        if (!definitions.TryGetValue(entryId, out var npc) || !npc.Flags.HasFlag(NpcFlags.Vendor))
            return new VendorSellResult(ResultNotVendor, 0, itemEntry);
        items.TryGetValue((uint)itemEntry, out var item);
        var unit = item?.SellPrice is uint sellPrice
            ? (long)sellPrice
            : FallbackUnitPrice(item, tabs, itemEntry);
        if (unit <= 0 || count <= 0) return new VendorSellResult(ResultCantSell, 0, itemEntry);
        var total = unit * count;
        return total > int.MaxValue
            ? new VendorSellResult(ResultCantSell, 0, itemEntry)
            : new VendorSellResult(ResultOk, (int)total, itemEntry);
    }

    private static long FallbackUnitPrice(ItemDefinition? item, IReadOnlyList<VendorTab> tabs, int itemEntry)
    {
        long buy = item?.BuyPrice is uint own
            ? own
            : tabs.SelectMany(tab => tab.Items).FirstOrDefault(row => row.Entry == itemEntry)?.BuyPrice ?? 0;
        return buy * SellBackNumerator / SellBackDenominator;
    }
}
