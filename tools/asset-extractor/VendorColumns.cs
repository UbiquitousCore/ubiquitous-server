namespace Ubiquitous.AssetExtractor;

public sealed record VendorListTableColumns(int Id, int Currency);

public sealed record VendorListItemTableColumns(int List, int Item, int StackCount, int MaxCount);

public static class VendorColumns
{
    public static readonly ColumnLayout ListLayout = new(
        "tables/creature_machant_sell_program.tbl",
        "usu",
        [
            LayoutColumn.Must(nameof(VendorListTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(VendorListTableColumns.Currency), 2, "u"),
        ]);

    public static readonly ColumnLayout ItemLayout = new(
        "tables/creature_machant_sell_program_item.tbl",
        "uuuuuuuuuuu",
        [
            LayoutColumn.Must(nameof(VendorListItemTableColumns.List), 1, "u"),
            LayoutColumn.Must(nameof(VendorListItemTableColumns.Item), 2, "u"),
            LayoutColumn.Must(nameof(VendorListItemTableColumns.StackCount), 3, "u"),
            LayoutColumn.Must(nameof(VendorListItemTableColumns.MaxCount), 4, "u"),
        ]);
}
