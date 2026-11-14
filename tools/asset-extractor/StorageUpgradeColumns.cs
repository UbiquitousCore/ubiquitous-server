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

    public const int Build114Cols = 7;

    // 4.2.2 adds column 2, which sorts the rows into ladders 0 to 3; only ladder 0 is read.
    public static readonly StorageUpgradeTableColumns Build422 = new(
        Id: 0,
        AreaColumn: 1,
        SlotCount: 3,
        PriceCurrency: 4,
        PriceAmount: 5,
        FromSlotCount: 7,
        LadderColumn: 2);

    public const int Build422Cols = 8;

    public static StorageUpgradeTableColumns? For(TableLayout layout) => layout.Cols switch
    {
        Build114Cols => Build114,
        Build422Cols => Build422,
        _ => null,
    };
}
