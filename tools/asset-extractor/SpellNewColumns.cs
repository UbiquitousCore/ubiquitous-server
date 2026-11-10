namespace Ubiquitous.AssetExtractor;

public static class SpellNewColumns
{
    public static readonly ColumnLayout Layout = new(
        "tables/spell_new.tbl",
        "uuusuuuuuusuiuuuuuffffuuuuuuuuuuuuuuuuufuuuuuuuuuuuuu",
        [
            LayoutColumn.Must(nameof(SpellTableColumns.Id), 0, "uk"),
            LayoutColumn.Must(nameof(SpellTableColumns.NameTextId), 5, "u"),
            LayoutColumn.Must(nameof(SpellTableColumns.Rank), 6, "u"),
            LayoutColumn.Must(nameof(SpellTableColumns.WornSlotNeeded), 12, "iu"),
            LayoutColumn.Must(nameof(SpellTableColumns.RequiredItemClass), 13, "u"),
            LayoutColumn.Must(nameof(SpellTableColumns.ItemSubclassesAllowed), 14, "u"),
            LayoutColumn.Must(nameof(SpellTableColumns.CastTimeMs), 16, "iu"),
            LayoutColumn.Must(nameof(SpellTableColumns.CooldownMs), 17, "iu"),
            LayoutColumn.Must(nameof(SpellTableColumns.MaxRange), 21, "f"),
            LayoutColumn.Must(nameof(SpellTableColumns.DurationMs), 31, "iu"),
            LayoutColumn.MustRun(nameof(SpellTableColumns.EffectSlots), "u", 33, 34, 35, 36, 37),
            LayoutColumn.May(nameof(SpellTableColumns.Speed), 39, "f"),
            LayoutColumn.May(nameof(SpellTableColumns.PowerCost), 41, "u"),
            LayoutColumn.MayRun(nameof(SpellTableColumns.GroupMaskColumns), "u", 44, 45, 46),
        ],
        // 1.0.0 to 1.1.1: no mount column
        new LayoutRevision("uuusuuuuuusuiuuuuuffffuuuuuuuuuuuuuuuufuuuuuuuuuuuuu", new LayoutEdit.Lacks(25)),
        // 2.2.0
        new LayoutRevision("uuusuuuuuusuiuuuuuffffuuuuuuuuuuuuuuuuufuuuuuuuuuuuuuuu", new LayoutEdit.Adds(47, 2)),
        // 2.5.0
        new LayoutRevision("uuusuuuuuusuiuuuuuffffuuuuuuuuuuuuuuuuufuuuuuuuuuuuuuuuu", new LayoutEdit.Adds(47, 2), new LayoutEdit.Adds(53)),
        // 4.2.21
        new LayoutRevision("uuusuuuuuusuiuuuuuffffuuuuuuuuuuuuuuuuufuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuu", new LayoutEdit.Adds(47, 17), new LayoutEdit.Adds(53, 4)));

    public static SpellTableColumns Reference => Layout.Reference.As<SpellTableColumns>();
}
