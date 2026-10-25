namespace Ubiquitous.AssetExtractor;

public static class ItemColumns
{
    private const string Table = "tables/items.tbl";

    // 1.1.0 drops one column in the 21..25 uniform run (row-anchored on
    // items present in both builds: 0..19 align, the tail aligns shifted by
    // one from golden 54 on). Every pin past the gap moves down one; the
    // pinned letters are unchanged.
    public static readonly ItemTableColumns Build110 = new(
        Id: 0,
        Name: -1,
        Class: 1,
        SubClass: 2,
        DisplayInfoId: 4,
        NameTextId: 3,
        OnUseSpellId: 57,
        OnUseSpellTrigger: 58,
        OnUseCooldownSeconds: 60,
        BuyPrice: 8,
        SellPrice: 9,
        QuestId: 74,
        AllowableClassMask: 13,
        InventoryType: 11,
        ItemLevel: 15,
        RequiredLevel: 16,
        RequiredSpellId: 19,
        AllowableRaceMask: 14,
        Quality: 5,
        MinDamage: 47,
        MaxDamage: 48,
        SpeedMs: 54,
        Armor: 53,
        StatCount: 26,
        FirstStat: 27,
        MaxStats: 10,
        MaxStack: 24);

    public static ItemTableColumns Resolve(FTableDocument document)
    {
        string format = document.Header.ColumnTypes;
        if (TableSchemas.Matches(Table, format, "1.1.4")) return Build114;
        if (TableSchemas.Matches(Table, format, "1.1.0")) return Build110;
        // Only the identity is required: every other column feeds a mapper
        // branch that already treats -1 as absent, so a retyped or truncated
        // column degrades to skipped wiring.
        TableShape.Expect(Table, format,
            (Build114.Id, "uk"),
            (Build114.Class, "uc"),
            (Build114.SubClass, "uc"),
            (Build114.DisplayInfoId, "u"),
            (Build114.NameTextId, "uk"));
        return Build114 with
        {
            OnUseSpellId = TableShape.Optional(Table, format, Build114.OnUseSpellId, "u"),
            OnUseSpellTrigger = TableShape.Optional(Table, format, Build114.OnUseSpellTrigger, "u"),
            OnUseCooldownSeconds = TableShape.Optional(Table, format, Build114.OnUseCooldownSeconds, "u"),
            BuyPrice = TableShape.Optional(Table, format, Build114.BuyPrice, "u"),
            SellPrice = TableShape.Optional(Table, format, Build114.SellPrice, "u"),
            QuestId = TableShape.Optional(Table, format, Build114.QuestId, "u"),
            AllowableClassMask = TableShape.Optional(Table, format, Build114.AllowableClassMask, "u"),
            InventoryType = TableShape.Optional(Table, format, Build114.InventoryType, "u"),
            ItemLevel = TableShape.Optional(Table, format, Build114.ItemLevel, "u"),
            RequiredLevel = TableShape.Optional(Table, format, Build114.RequiredLevel, "u"),
            RequiredSpellId = TableShape.Optional(Table, format, Build114.RequiredSpellId, "u"),
            AllowableRaceMask = TableShape.Optional(Table, format, Build114.AllowableRaceMask, "u"),
            Quality = TableShape.Optional(Table, format, Build114.Quality, "u"),
            MinDamage = TableShape.Optional(Table, format, Build114.MinDamage, "uf"),
            MaxDamage = TableShape.Optional(Table, format, Build114.MaxDamage, "uf"),
            SpeedMs = TableShape.Optional(Table, format, Build114.SpeedMs, "uf"),
            Armor = TableShape.Optional(Table, format, Build114.Armor, "uf"),
            StatCount = TableShape.Optional(Table, format, Build114.StatCount, "u"),
            FirstStat = TableShape.Optional(Table, format, Build114.FirstStat, "u"),
            MaxStack = TableShape.Optional(Table, format, Build114.MaxStack, "u"),
        };
    }

    public static readonly ItemTableColumns Build114 = new(
        Id: 0,
        Name: -1,
        Class: 1,
        SubClass: 2,
        DisplayInfoId: 4,
        NameTextId: 3,
        OnUseSpellId: 58,
        OnUseSpellTrigger: 59,
        OnUseCooldownSeconds: 61,
        BuyPrice: 8,
        SellPrice: 9,
        QuestId: 75,
        AllowableClassMask: 13,
        InventoryType: 11,
        ItemLevel: 15,
        RequiredLevel: 16,
        RequiredSpellId: 19,
        AllowableRaceMask: 14,
        Quality: 5,
        MinDamage: 48,
        MaxDamage: 49,
        SpeedMs: 55,
        Armor: 54,
        StatCount: 27,
        FirstStat: 28,
        MaxStats: 10,
        MaxStack: 25);
}
