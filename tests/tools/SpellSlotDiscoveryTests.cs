using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

// Task 2: the effect slots a spell_new row carries are discovered, not
// pinned. A layout that moves them (but keeps the combat core where it
// sits) imports with full effects; power cost and group mask degrade to
// unknown rather than reading shifted columns.
public class SpellSlotDiscoveryTests
{
    // Core columns sit at their 1.1.4 indices with 1.1.4 types, but the
    // effect references moved to columns 24-26. Everything else holds
    // values outside the effect id set.
    // 40 columns: the combat core at its 1.1.4 indices, 'f' at 21 and 39.
    private static readonly string MovedSlotFormat =
        new string('u', 21) + "f" + new string('u', 17) + "f";

    private static readonly HashSet<uint> EffectIds = new() { 500u, 501u, 502u, 503u, 504u, 505u };

    private static FTableDocument MovedSlotTable() => Build(MovedSlotFormat,
    [
        Row(80, s24: 500u, s25: 0u, s26: 504u),
        Row(81, s24: 501u, s25: 503u, s26: 0u),
        Row(82, s24: 0u, s25: 504u, s26: 505u),
        Row(83, s24: 502u, s25: 0u, s26: 500u),
        Row(84, s24: 500u, s25: 503u, s26: 0u),
    ]);

    private static Dictionary<int, object> Row(uint id, uint s24, uint s25, uint s26) =>
        new()
        {
            [0] = id, [1] = 3u, [2] = 1u, [5] = 700u, [6] = 1u, [12] = 13u, [13] = 4u, [14] = 0u,
            [16] = 1500u, [17] = 8000u, [21] = 30f, [31] = 6000u, [39] = 0f,
            [24] = s24, [25] = s25, [26] = s26,
        };

    [Fact]
    public void DiscoversSlots_ThatMoved()
    {
        var (columns, notes) = SpellNewColumns.Resolve(MovedSlotTable(), EffectIds);
        Assert.Equal([24, 25, 26], columns.EffectSlots);
        Assert.Equal(-1, columns.PowerCost);
        Assert.Null(columns.GroupMaskColumns);
        Assert.Equal(16, columns.CastTimeMs);
        var note = Assert.Single(notes);
        Assert.Contains("24,25,26", note);
    }

    [Fact]
    public void DiscoveredSlots_DriveClassification()
    {
        var (columns, _) = SpellNewColumns.Resolve(MovedSlotTable(), EffectIds);
        var effects = new Dictionary<uint, SpellEffectDefinition>
        {
            [500u] = new(500u, 0, SpellEffectColumns.DirectDamageSubType, 0, 50, 0, 0u, 2, 0u, 0u),
        };
        var spells = SpellTableMapper.MapSpells(
            MovedSlotTable(), columns, resolveName: _ => "moved-bolt");
        var rowsById = MovedSlotTable().Rows.ToDictionary(row => (uint)row[0]);
        var spell = spells.Single(s => s.Id == 80u);
        var row = rowsById[80u];
        var slots = columns.EffectSlots!.Select(column =>
            (column, row.Count > column && row[column] is uint id && id != 0
                && effects.TryGetValue(id, out var found) ? found : null)).ToList();
        spell = SpellEffectClassifier.ApplySlots(spell, slots, columns.EffectSlots!);
        Assert.Equal(50, spell.EffectDamage);
        Assert.Null(spell.PowerCost);
        Assert.Equal(1500, spell.CastTimeMs);
    }

    [Fact]
    public void ThrowsLoudly_WhenNoColumnLooksLikeSlots()
    {
        var table = Build(MovedSlotFormat,
        [
            Row(80, s24: 99991u, s25: 99992u, s26: 99993u),
            Row(81, s24: 99994u, s25: 99995u, s26: 99996u),
            Row(82, s24: 99997u, s25: 99998u, s26: 99999u),
        ]);
        var ex = Assert.Throws<FTableFormatException>(() => SpellNewColumns.Resolve(table, EffectIds));
        Assert.Contains("tables/spell_new.tbl", ex.Message);
        Assert.Contains("no effect-slot columns", ex.Message);
    }

    [Fact]
    public void SmallIntColumns_DoNotQualify_AsSlots()
    {
        // Columns of small ints collide with low effect ids; the span guard
        // (values must reach 100) keeps them out even at full hit rate.
        var table = Build("uuuu",
        [
            new Dictionary<int, object> { [0] = 1u, [1] = 1u, [2] = 2u, [3] = 3u },
            new Dictionary<int, object> { [0] = 2u, [1] = 1u, [2] = 2u, [3] = 3u },
            new Dictionary<int, object> { [0] = 3u, [1] = 1u, [2] = 2u, [3] = 3u },
        ]);
        var ids = new HashSet<uint> { 1u, 2u, 3u };
        Assert.Empty(TableShape.DiscoverEffectSlots("t", table, ids));
    }

    private static FTableDocument Build(string format, IReadOnlyList<Dictionary<int, object>> rows)
    {
        int cols = format.Length;
        var widths = format.Select(c => c switch { 's' => 6, 'h' => 2, 'c' => 1, _ => 4 }).ToArray();
        int stride = widths.Sum();
        const int header = 44;
        int formatAt = header + rows.Count * 4;
        int recordsAt = formatAt + cols;
        int stringsAt = recordsAt + stride * rows.Count;
        var data = new byte[stringsAt];
        void Ascii(int at, string text)
        {
            for (int i = 0; i < text.Length; i++) data[at + i] = (byte)text[i];
        }

        void U32(int at, int value) =>
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), (uint)value);
        Ascii(0, "FTABLE");
        U32(12, rows.Count); U32(16, cols); U32(20, header); U32(24, formatAt);
        U32(28, recordsAt); U32(32, stride); U32(36, stringsAt); U32(40, 0);
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
}
