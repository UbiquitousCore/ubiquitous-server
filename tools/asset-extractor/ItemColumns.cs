namespace Ubiquitous.AssetExtractor;

public static class ItemColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/items.tbl",
        "uuukuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuffuffuuuufuuuuuuuuuuuuukuuuuuuuufuuuuuuuuuuuuuuuusuuuusuuuuuuuuuu",
        [
            LayoutColumn.Must(nameof(ItemTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(ItemTableColumns.Class), 1, "uc"),
            LayoutColumn.Must(nameof(ItemTableColumns.SubClass), 2, "uc"),
            LayoutColumn.Must(nameof(ItemTableColumns.NameTextId), 3, "uk"),
            LayoutColumn.Must(nameof(ItemTableColumns.DisplayInfoId), 4, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.Quality), 5, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.BuyPrice), 8, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.SellPrice), 9, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.InventoryType), 11, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.AllowableClassMask), 13, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.AllowableRaceMask), 14, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.ItemLevel), 15, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.RequiredLevel), 16, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.RequiredSpellId), 19, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.MaxStack), 25, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.StatCount), 27, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.FirstStat), 28, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.MinDamage), 48, "uf"),
            LayoutColumn.May(nameof(ItemTableColumns.MaxDamage), 49, "uf"),
            LayoutColumn.May(nameof(ItemTableColumns.Armor), 54, "uf"),
            LayoutColumn.May(nameof(ItemTableColumns.SpeedMs), 55, "uf"),
            LayoutColumn.May(nameof(ItemTableColumns.OnUseSpellId), 58, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.OnUseSpellTrigger), 59, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.OnUseCooldownSeconds), 61, "u"),
            LayoutColumn.May(nameof(ItemTableColumns.QuestId), 75, "u"),
        ],
        // Before 1.1.4 one of the always-empty columns 20 to 22 is not there;
        // the rows cannot say which, and nothing reads any of the three.
        // 1.0.0 and 1.0.1
        new LayoutRevision("uuukuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuffuffuuuufuuuuuuuuuuuuukuuuuuuuufuuuuuuuuuuuuuuuusuuuusuu", new LayoutEdit.Lacks(22), new LayoutEdit.Lacks(105, 8)),
        // 1.0.3: one column past 104 that lines up with none of the reference's
        new LayoutRevision(
            "uuukuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuffuffuuuufuuuuuuuuuuuuukuuuuuuuufuuuuuuuuuuuuuuuusuuuusuuu",
            new LayoutEdit.Lacks(22), new LayoutEdit.Lacks(105, 8), new LayoutEdit.Adds(113)),
        // 1.1.0 and 1.1.1: likewise one unplaced column at the end
        new LayoutRevision(
            "uuukuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuffuffuuuufuuuuuuuuuuuuukuuuuuuuufuuuuuuuuuuuuuuuusuuuusuuuuuuu",
            new LayoutEdit.Lacks(22), new LayoutEdit.Lacks(109, 4), new LayoutEdit.Adds(113)),
        // 2.2.0
        new LayoutRevision("uuukuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuffuffuuuufuuuuuuuuuuuuukuuuuuuuufuuuuuuuuuuuuuuuusuuuusuuuuuuuuuus", new LayoutEdit.Adds(113)),
        // 2.5.0
        new LayoutRevision("uuukuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuffuffuuuufuuuuuuuuuuuuukuuuuuuuufuuuuuuuuuuuuuuuusuuuusuuuuuuuuuusuuuu", new LayoutEdit.Adds(113, 5)),
        // 4.2.21
        new LayoutRevision("uuukuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuffuffuuuufuuuuuuuuuuuuukuuuuuuuufuuuuuuuuuuuuuuuusuuuusuuuuuuuuuusuuuuuufsusssuuuuuuuuuu", new LayoutEdit.Adds(113, 23)));

    public static ItemTableColumns Reference => Layout.Reference.As<ItemTableColumns>();
}
