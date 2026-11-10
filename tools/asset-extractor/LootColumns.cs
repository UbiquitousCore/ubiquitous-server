namespace Ubiquitous.AssetExtractor;

public sealed record LootTableColumns(int Target, int TargetKind, int Group);

public sealed record LootGroupTableColumns(int Id, int MinLevel, int MaxLevel, int GuaranteedDrops, int MaxDrops);

public sealed record LootItemTableColumns(int Group, int Item, int Chance, int Fewest, int Most);

public static class LootColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/loot.tbl",
        "uuuu",
        [
            LayoutColumn.Must(nameof(LootTableColumns.Target), 1, "u"),
            LayoutColumn.Must(nameof(LootTableColumns.TargetKind), 2, "u"),
            LayoutColumn.Must(nameof(LootTableColumns.Group), 3, "u"),
        ]);

    public static readonly ColumnLayout GroupLayout = new(
        "tables/loot_feature.tbl",
        "uuuuuuuu",
        [
            LayoutColumn.Must(nameof(LootGroupTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(LootGroupTableColumns.MinLevel), 2, "u"),
            LayoutColumn.Must(nameof(LootGroupTableColumns.MaxLevel), 3, "u"),
            LayoutColumn.Must(nameof(LootGroupTableColumns.GuaranteedDrops), 6, "u"),
            LayoutColumn.Must(nameof(LootGroupTableColumns.MaxDrops), 7, "u"),
        ]);

    public static readonly ColumnLayout ItemLayout = new(
        "tables/loot_feature_items.tbl",
        "uuufuu",
        [
            LayoutColumn.Must(nameof(LootItemTableColumns.Group), 1, "u"),
            LayoutColumn.Must(nameof(LootItemTableColumns.Item), 2, "u"),
            LayoutColumn.Must(nameof(LootItemTableColumns.Chance), 3, "f"),
            LayoutColumn.Must(nameof(LootItemTableColumns.Fewest), 4, "u"),
            LayoutColumn.Must(nameof(LootItemTableColumns.Most), 5, "u"),
        ]);
}
