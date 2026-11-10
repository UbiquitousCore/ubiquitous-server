namespace Ubiquitous.AssetExtractor;

public static class CreatureAppearanceColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/creature_appearance.tbl",
        "ukkkuuu",
        [
            LayoutColumn.Must(nameof(CreatureAppearanceTableColumns.Entry), 0, "uk"),
            LayoutColumn.Must(nameof(CreatureAppearanceTableColumns.NameTextId), 1, "uk"),
        ]);

    public static CreatureAppearanceTableColumns Reference => Layout.Reference.As<CreatureAppearanceTableColumns>();
}
