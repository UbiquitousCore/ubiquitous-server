namespace Ubiquitous.AssetExtractor;

public static class CreatureProtoColumns
{
    private static readonly LayoutColumn OneSellList = LayoutColumn.May(nameof(CreatureTableColumns.SellList), 55, "u");

    public static readonly ColumnLayout Layout = new(
        "tables/creature_proto.tbl",
        "uuuuuuuuuufuuuuuuuuuuuuuuuuuuuuuuuffsuuuuuuuuuuuuuuuuuusuuuuufuuuuufuuuuuu",
        [
            LayoutColumn.Must(nameof(CreatureTableColumns.Entry), 0, "uk"),
            LayoutColumn.May(nameof(CreatureTableColumns.Family), 1, "iuh"),
            LayoutColumn.May(nameof(CreatureTableColumns.Rank), 3, "u"),
            LayoutColumn.Must(nameof(CreatureTableColumns.Level), 4, "iu"),
            LayoutColumn.May(nameof(CreatureTableColumns.Faction), 6, "iuh"),
            LayoutColumn.Must(nameof(CreatureTableColumns.Health), 8, "iu"),
            LayoutColumn.May(nameof(CreatureTableColumns.NpcFlags), 11, "u"),
            LayoutColumn.May(nameof(CreatureTableColumns.MeleeAttackIntervalMs), 12, "iuh"),
            LayoutColumn.May(nameof(CreatureTableColumns.MinDamage), 14, "iuh"),
            LayoutColumn.May(nameof(CreatureTableColumns.MaxDamage), 15, "iuh"),
            LayoutColumn.May(nameof(CreatureTableColumns.RangedAttackIntervalMs), 16, "iuh"),
            LayoutColumn.May(nameof(CreatureTableColumns.RespawnMinSeconds), 19, "iuh"),
            LayoutColumn.May(nameof(CreatureTableColumns.RespawnMaxSeconds), 20, "iuh"),
            LayoutColumn.May(nameof(CreatureTableColumns.ExtraFlags), 44, "u"),
            LayoutColumn.May(nameof(CreatureTableColumns.MoneyDropMin), 53, "iuh"),
            LayoutColumn.May(nameof(CreatureTableColumns.MoneyDropMax), 54, "iuh"),
            LayoutColumn.May(nameof(CreatureTableColumns.SellList), 55, "s"),
            LayoutColumn.May(nameof(CreatureTableColumns.GreetingMenu), 56, "u"),
            LayoutColumn.May(nameof(CreatureTableColumns.Festival), 64, "u"),
            LayoutColumn.May(nameof(CreatureTableColumns.LeashRange), 67, "f"),
            LayoutColumn.May(nameof(CreatureTableColumns.NoticeDistance), 68, "iuh"),
        ],
        // 1.0.0 to 1.0.3: one vendor list, as a number, where later builds write several
        new LayoutRevision(
            "uuuuuuuuuufuuuuuuuuuuuuuuuuuuuuuuuffsuuuuuuuuuuuuuuuuuuuuuuuufuu",
            new LayoutEdit.Lacks(55), new LayoutEdit.Adds(56), new LayoutEdit.Lacks(64, 10))
        {
            Own = [OneSellList],
        },
        // 1.1.0 and 1.1.1
        new LayoutRevision(
            "uuuuuuuuuufuuuuuuuuuuuuuuuuuuuuuuuffsuuuuuuuuuuuuuuuuuuuuuuuufuuuuuf",
            new LayoutEdit.Lacks(55), new LayoutEdit.Adds(56), new LayoutEdit.Lacks(68, 6))
        {
            Own = [OneSellList],
        },
        // 2.5.0
        new LayoutRevision("uuuuuuuuuufuuuuuuuuuuuuuuuuuuuuuuuffsuuuuuuuuuuuuuuuuuusuuuuufuuuuufuuuuuuu", new LayoutEdit.Adds(74)),
        // 4.2.21
        new LayoutRevision("uuuuuuuuuufuuuuuuuuuuuuuuuuuuuuuuuffsuuuuuuuuuuuuuuuuuusuuuuufuuuuufuuuuuuuuuuf", new LayoutEdit.Adds(74, 5)));

    public static CreatureTableColumns Reference => Layout.Reference.As<CreatureTableColumns>();
}
