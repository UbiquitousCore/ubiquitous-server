namespace Ubiquitous.AssetExtractor;

public static class StorageUpgradeColumns
{
    public static readonly StorageUpgradeTableColumns Build114 = new(
        Id: 0,
        AreaColumn: 1,
        SlotCount: 2,
        PriceCurrency: 3,
        PriceAmount: 4,
        FromSlotCount: 6);
}
