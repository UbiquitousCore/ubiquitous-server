using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

// The 1.1.0 overrides, pinned by row-anchored evidence: items present in
// both builds show columns 0..19 aligned and the tail shifted by exactly
// one (the dropped uniform column sits in golden 20..25), and spell power
// costs at 1.1.0 col 40 distribute exactly like 1.1.4 col 41.
public class Build110LayoutTests
{
    private static readonly string Items110 = TableSchemas.FormatFor("tables/items.tbl", "1.1.0")!;
    private static readonly string Spells110 = TableSchemas.FormatFor("tables/spell_new.tbl", "1.1.0")!;
    private static readonly string Effects114 = TableSchemas.FormatFor("tables/spell_effect.tbl", "1.1.4")!;

    [Fact]
    public void ItemColumns_ResolveToTheShifted110Layout()
    {
        var document = Table("tables/items.tbl", Items110, rows: 0);
        Assert.Same(ItemColumns.Build110, ItemColumns.Resolve(document));
        Assert.Equal(24, ItemColumns.Build110.MaxStack);
        Assert.Equal(26, ItemColumns.Build110.StatCount);
        Assert.Equal(27, ItemColumns.Build110.FirstStat);
        Assert.Equal(74, ItemColumns.Build110.QuestId);
    }

    [Fact]
    public void ItemStats_ReadFromTheShiftedColumns()
    {
        var document = Table("tables/items.tbl", Items110,
        [
            new Dictionary<int, object>
            {
                [0] = 6530u, [1] = 2u, [2] = 9u, [3] = 4638u, [5] = 5u, [8] = 294548u,
                [15] = 90u, [16] = 60u, [24] = 100u, [26] = 2u,
                [27] = 11u, [28] = 30u, [29] = 13u, [30] = 36u,
            },
        ]);
        var item = Assert.Single(ItemTableMapper.MapItems(
            document, ItemColumns.Resolve(document), resolveName: _ => "axe"));
        Assert.Equal(5u, item.Quality);
        Assert.Equal(294548u, item.BuyPrice);
        Assert.Equal(60u, item.RequiredLevel);
        Assert.Equal(100u, item.MaxStack);
        Assert.Equal(
            [new ItemAttributeBonus(AttributeIds.Strength, 30), new ItemAttributeBonus(AttributeIds.Stamina, 36)],
            item.Stats);
    }

    [Fact]
    public void SpellColumns_ResolveToThe110Layout_WithDiscoveredSlots()
    {
        var document = Table("tables/spell_new.tbl", Spells110, rows: 0);
        var (columns, notes) = SpellNewColumns.Resolve(document, new HashSet<uint> { 500u });
        Assert.Same(SpellNewColumns.Build110, columns);
        Assert.Empty(notes);
        Assert.Equal([32, 33, 34, 35, 36], columns.EffectSlots);
        Assert.Equal(40, columns.PowerCost);
        Assert.Equal([43, 44, 45], columns.GroupMaskColumns);
    }

    [Fact]
    public void SpellPowerAndEffects_ReadFromTheShiftedColumns()
    {
        var effects = Table("tables/spell_effect.tbl", Effects114,
        [
            new Dictionary<int, object> { [0] = 500u, [6] = 0u, [7] = 1u, [8] = 1, [9] = 50, [10] = 5 },
        ]);
        var effectsById = SpellEffectTableMapper.MapEffects(
            effects, SpellEffectColumns.Resolve(effects)).ToDictionary(e => e.Id);
        var spells = Table("tables/spell_new.tbl", Spells110,
        [
            new Dictionary<int, object>
            {
                [0] = 85u, [5] = 700u, [16] = 1000u, [17] = 2000u, [21] = 30f,
                [32] = 500u, [40] = 15u, [43] = 4u, [44] = 0u, [45] = 0u,
            },
        ]);
        var (columns, _) = SpellNewColumns.Resolve(spells, new HashSet<uint>(effectsById.Keys));
        var spell = Assert.Single(SpellTableMapper.MapSpells(
            spells, columns, resolveName: _ => "bolt"));
        var row = spells.Rows.Single(r => (uint)r[0] == 85u);
        var slots = columns.EffectSlots!.Select(column =>
            (column, row.Count > column && row[column] is uint id && id != 0
                && effectsById.TryGetValue(id, out var found) ? found : null)).ToList();
        spell = SpellEffectClassifier.ApplySlots(
            spell with { PowerCost = 15, SpellGroupMask = [4u, 0u, 0u] }, slots, columns.EffectSlots!);
        Assert.Equal(50, spell.EffectDamage);
        Assert.Equal(15, spell.PowerCost);
    }

    private static FTableDocument Table(string name, string format, IReadOnlyList<Dictionary<int, object>> rows)
    {
        int cols = format.Length;
        var widths = format.Select(c => c switch { 's' => 6, 'h' => 2, 'c' => 1, _ => 4 }).ToArray();
        int stride = widths.Sum();
        const int header = 44;
        int formatAt = header + rows.Count * 4;
        int recordsAt = formatAt + cols;
        var data = new byte[recordsAt + stride * rows.Count];
        void Ascii(int at, string text)
        {
            for (int i = 0; i < text.Length; i++) data[at + i] = (byte)text[i];
        }

        void U32(int at, int value) =>
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), (uint)value);
        Ascii(0, "FTABLE");
        U32(12, rows.Count); U32(16, cols); U32(20, header); U32(24, formatAt);
        U32(28, recordsAt); U32(32, stride); U32(36, recordsAt + stride * rows.Count); U32(40, 0);
        Ascii(formatAt, format);
        int at = recordsAt;
        foreach (var row in rows)
        {
            for (int c = 0; c < cols; c++)
            {
                if (format[c] == 'f')
                {
                    float value = row.TryGetValue(c, out var v) ? Convert.ToSingle(v) : 0f;
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at, 4), value);
                }
                else
                {
                    uint value = row.TryGetValue(c, out var v) ? Convert.ToUInt32(v) : 0u;
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), value);
                }

                at += widths[c];
            }
        }

        return FTableDocument.Load(data);
    }

    private static FTableDocument Table(string name, string format, int rows) =>
        Table(name, format, Enumerable.Range(0, rows).Select(_ => new Dictionary<int, object>()).ToList());
}
