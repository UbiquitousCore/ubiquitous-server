namespace Ubiquitous.AssetExtractor;

public static class StorageUpgradeColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/storage_upgrade.tbl",
        "uuuuuuu",
        [
            LayoutColumn.Must(nameof(StorageUpgradeTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(StorageUpgradeTableColumns.AreaColumn), 1, "u"),
            LayoutColumn.Must(nameof(StorageUpgradeTableColumns.SlotCount), 2, "u"),
            LayoutColumn.Must(nameof(StorageUpgradeTableColumns.PriceCurrency), 3, "u"),
            LayoutColumn.Must(nameof(StorageUpgradeTableColumns.PriceAmount), 4, "u"),
            LayoutColumn.Must(nameof(StorageUpgradeTableColumns.FromSlotCount), 6, "u"),
        ],
        // 2.5.0 on
        new LayoutRevision("uuuuuuuu", new LayoutEdit.Adds(2)));

    public static StorageUpgradeTableColumns Reference => Layout.Reference.As<StorageUpgradeTableColumns>();
}
