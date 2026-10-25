namespace Ubiquitous.AssetExtractor;

public static class CreatureProtoColumns
{
    private const string Table = "tables/creature_proto.tbl";

    // The vendor list (a string) and greeting menu sit beside the mapped
    // columns; on a layout where either is retyped they resolve to -1 and
    // the importer skips that wiring instead of misreading it.
    public static CreatureTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "1.1.4"))
        {
            return Build114 with { SellList = 55, GreetingMenu = 56 };
        }

        // Only the identity and combat core are required: every other
        // column feeds a mapper that already treats -1 as absent, so a
        // retyped or truncated column degrades to skipped wiring.
        TableShape.Expect(Table, format,
            (Build114.Entry, "uk"),
            (Build114.Level, "iu"),
            (Build114.Health, "iu"));
        return Build114 with
        {
            MinDamage = TableShape.Optional(Table, format, Build114.MinDamage, "iuh"),
            MaxDamage = TableShape.Optional(Table, format, Build114.MaxDamage, "iuh"),
            RespawnMinSeconds = TableShape.Optional(Table, format, Build114.RespawnMinSeconds, "iuh"),
            RespawnMaxSeconds = TableShape.Optional(Table, format, Build114.RespawnMaxSeconds, "iuh"),
            MoneyDropMin = TableShape.Optional(Table, format, Build114.MoneyDropMin, "iuh"),
            MoneyDropMax = TableShape.Optional(Table, format, Build114.MoneyDropMax, "iuh"),
            MeleeAttackIntervalMs = TableShape.Optional(Table, format, Build114.MeleeAttackIntervalMs, "iuh"),
            NoticeDistance = TableShape.Optional(Table, format, Build114.NoticeDistance, "iuh"),
            LeashRange = TableShape.Optional(Table, format, Build114.LeashRange, "f"),
            NpcFlags = TableShape.Optional(Table, format, Build114.NpcFlags, "u"),
            ExtraFlags = TableShape.Optional(Table, format, Build114.ExtraFlags, "u"),
            Faction = TableShape.Optional(Table, format, Build114.Faction, "iuh"),
            Festival = TableShape.Optional(Table, format, Build114.Festival, "u"),
            Rank = TableShape.Optional(Table, format, Build114.Rank, "u"),
            RangedAttackIntervalMs = TableShape.Optional(Table, format, Build114.RangedAttackIntervalMs, "iuh"),
            Family = TableShape.Optional(Table, format, Build114.Family, "iuh"),
            SellList = TableShape.Optional(Table, format, 55, "s"),
            GreetingMenu = TableShape.Optional(Table, format, 56, "u"),
        };
    }

    public static readonly CreatureTableColumns Build114 = new(
        Entry: 0,
        Name: -1,
        Level: 4,
        Health: 8,
        MinDamage: 14,
        MaxDamage: 15,
        RespawnMinSeconds: 19,
        RespawnMaxSeconds: 20,
        MoneyDropMin: 53,
        MoneyDropMax: 54,
        MeleeAttackIntervalMs: 12,
        NoticeDistance: 68,
        LeashRange: 67,
        NpcFlags: 11,
        ExtraFlags: 44,
        Faction: 6,
        Festival: 64,
        Rank: 3,
        RangedAttackIntervalMs: 16,
        Family: 1);
}
