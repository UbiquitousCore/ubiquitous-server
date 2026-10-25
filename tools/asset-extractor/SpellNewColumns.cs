namespace Ubiquitous.AssetExtractor;

public static class SpellNewColumns
{
    public static readonly SpellTableColumns Build114 = new(
        Id: 0,
        Name: -1,
        CastTimeMs: 16,
        CooldownMs: 17,
        MaxRange: 21,
        NameTextId: 5,
        Rank: 6,
        DurationMs: 31,
        WornSlotNeeded: 12,
        RequiredItemClass: 13,
        ItemSubclassesAllowed: 14,
        Speed: 39,
        PowerCost: 41,
        GroupMaskColumns: [44, 45, 46],
        EffectSlots: [33, 34, 35, 36, 37]);

    // 1.1.0 drops the float at golden 38, so speed, power and the group
    // mask each move down one; the effect slots sit one earlier too
    // (value-discovered at 32..36 on the real tables, matching this).
    public static readonly SpellTableColumns Build110 = new(
        Id: 0,
        Name: -1,
        CastTimeMs: 16,
        CooldownMs: 17,
        MaxRange: 21,
        NameTextId: 5,
        Rank: 6,
        DurationMs: 31,
        WornSlotNeeded: 12,
        RequiredItemClass: 13,
        ItemSubclassesAllowed: 14,
        Speed: 38,
        PowerCost: 40,
        GroupMaskColumns: [43, 44, 45],
        EffectSlots: [32, 33, 34, 35, 36]);

    private const string Table = "tables/spell_new.tbl";

    // Resolve the spell_new layout. An exact or append-only (prefix) match
    // takes the pinned columns; anything else validates the combat core
    // where it sits, discovers the effect slots by value, and leaves power
    // cost and group mask unknown rather than reading shifted columns.
    // Notes describe every degradation for the import log.
    public static (SpellTableColumns Columns, IReadOnlyList<string> Notes) Resolve(
        FTableDocument document, IReadOnlySet<uint> effectIds)
    {
        string format = document.Header.ColumnTypes;
        string? known = TableSchemas.FormatFor(Table, "1.1.4");
        if (format == known)
        {
            return (Build114, []);
        }

        if (known is not null && format.StartsWith(known, StringComparison.Ordinal))
        {
            return (Build114,
                [$"{Table}: {format.Length - known.Length} trailing column(s) past 1.1.4; pinned columns kept."]);
        }

        if (TableSchemas.Matches(Table, format, "1.1.0"))
        {
            return (Build110, []);
        }

        TableShape.Expect(Table, format,
            (Build114.Id, "uk"),
            (Build114.NameTextId, "u"),
            (Build114.Rank, "u"),
            (Build114.WornSlotNeeded, "iu"),
            (Build114.RequiredItemClass, "u"),
            (Build114.ItemSubclassesAllowed, "u"),
            (Build114.CastTimeMs, "iu"),
            (Build114.CooldownMs, "iu"),
            (Build114.MaxRange, "f"),
            (Build114.DurationMs, "iu"));
        int speed = TableShape.Optional(Table, format, Build114.Speed, "f");

        int[] slots = TableShape.DiscoverEffectSlots(Table, document, effectIds);
        if (slots.Length == 0)
        {
            throw new FTableFormatException(
                $"{Table}: no effect-slot columns discovered (format \"{format}\") — " +
                $"no layout in {nameof(TableSchemas)} matches; fingerprint the build and add one.");
        }

        var notes = new List<string>
        {
            $"{Table}: unknown layout (format \"{format}\"); effect slots discovered at " +
            $"{string.Join(",", slots)}, power cost and group mask left unknown" +
            (speed < 0 ? " and speed left at 0" : "") + ".",
        };
        return (Build114 with { PowerCost = -1, GroupMaskColumns = null, EffectSlots = slots, Speed = speed }, notes);
    }
}
