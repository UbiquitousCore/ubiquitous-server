namespace Ubiquitous.AssetExtractor;

public static class StorageUpgradeColumns
{
    private const string Table = "tables/storage_upgrade.tbl";

    public static StorageUpgradeTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "1.1.4")) return Build114;
        TableShape.Expect(Table, format,
            (Build114.Id, "uk"),
            (Build114.AreaColumn, "u"),
            (Build114.SlotCount, "u"),
            (Build114.PriceCurrency, "u"),
            (Build114.PriceAmount, "u"),
            (Build114.FromSlotCount, "u"));
        return Build114;
    }

    public static readonly StorageUpgradeTableColumns Build114 = new(
        Id: 0,
        AreaColumn: 1,
        SlotCount: 2,
        PriceCurrency: 3,
        PriceAmount: 4,
        FromSlotCount: 6);
}
