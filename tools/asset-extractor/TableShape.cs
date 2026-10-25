namespace Ubiquitous.AssetExtractor;

// Shape helpers ported from the client's tables.ts (adjacentPair,
// longestRun, runLength, expect): columns that move between builds keep
// their neighbours, so resolvers locate them by shape and validate every
// pinned column against the table's own format string.
//
// A mismatch throws a loud FTableFormatException naming the table, the
// column and both formats — never a bare "no usable X" from deep in a
// mapper, and never a silent misread of a shifted column that happens to
// still hold a uint.
public static class TableShape
{
    // How many consecutive `ch` start at `at`.
    public static int RunLength(string format, int at, char ch)
    {
        int n = 0;
        while (at + n < format.Length && format[at + n] == ch) n++;
        return n;
    }

    // Start and length of the longest run of `ch`, or (-1, 0).
    public static (int At, int Length) LongestRun(string format, char ch)
    {
        var best = (-1, 0);
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != ch) continue;
            int length = RunLength(format, i, ch);
            if (length > best.Item2) best = (i, length);
            i += length - 1;
        }

        return best;
    }

    // The first place two `ch` sit side by side, for columns that move but
    // keep their neighbours, or -1.
    public static int AdjacentPair(string format, char ch)
    {
        for (int i = 0; i < format.Length - 1; i++)
        {
            if (format[i] == ch && format[i + 1] == ch) return i;
        }

        return -1;
    }

    // Guard required columns against the table's own format string.
    // `types` lists the accepted format letters (`k` reads as a uint, so a
    // uint-expected column accepts "uk").
    public static void Expect(string table, string format, params (int Column, string Types)[] checks)
    {
        foreach (var (column, types) in checks)
        {
            if (column < 0 || column >= format.Length)
            {
                throw new FTableFormatException(
                    $"{table}: column {column} is past the end (format \"{format}\") — " +
                    $"no layout in {nameof(TableSchemas)} matches; fingerprint the build and add one.");
            }

            char got = format[column];
            if (!types.Contains(got))
            {
                throw new FTableFormatException(
                    $"{table}: column {column} is '{got}', expected one of \"{types}\" " +
                    $"(format \"{format}\") — no layout in {nameof(TableSchemas)} matches; " +
                    "fingerprint the build and add one.");
            }
        }
    }

    // A column that may be absent or retyped: its index when it is there
    // with an accepted type, else -1. Callers treat -1 as "wiring skipped".
    public static int Optional(string table, string format, int column, string types)
    {
        if (column < 0 || column >= format.Length || !types.Contains(format[column])) return -1;
        return column;
    }

    // Effect-slot discovery for spell_new on an unknown layout: a slot
    // column holds one spell_effect id per spell, so nearly every nonzero
    // value names a real effect, the values span the effect id space (which
    // rules out boolean and small-enum columns whose small ints collide
    // with low effect ids), and enough spells use the column to judge.
    // Own-choice thresholds, validated against 1.1.4 (exactly columns
    // 33-37) and 1.1.0 (exactly columns 32-36, where two slot columns each
    // carry a few stale references) and pinned by SpellSlotDiscoveryTests.
    // Returns the longest contiguous run of qualifying columns; empty when
    // nothing qualifies.
    public static int[] DiscoverEffectSlots(string table, FTableDocument spellNew, IReadOnlySet<uint> effectIds)
    {
        const double MinHitRate = 0.99;
        const int MaxMisses = 5;
        const int MinSample = 3;
        const uint MinSpan = 100;

        string format = spellNew.Header.ColumnTypes;
        var qualified = new List<int>();
        for (int column = 0; column < format.Length; column++)
        {
            if (format[column] != 'u') continue;
            int nonzero = 0;
            int misses = 0;
            uint maxRef = 0;
            foreach (var row in spellNew.Rows)
            {
                if (column >= row.Count || row[column] is not uint value || value == 0) continue;
                nonzero++;
                if (value > maxRef) maxRef = value;
                if (!effectIds.Contains(value))
                {
                    misses++;
                    if (misses > MaxMisses) break;
                }
            }

            if (nonzero >= MinSample && misses <= MaxMisses
                && (double)(nonzero - misses) / nonzero >= MinHitRate && maxRef >= MinSpan)
            {
                qualified.Add(column);
            }
        }

        // Longest contiguous run; ties go to the first. A build that splits
        // its slots degrades to the longest run, and the import log names
        // which columns were used.
        int[] best = [];
        var run = new List<int>();
        foreach (int column in qualified)
        {
            if (run.Count > 0 && column != run[^1] + 1)
            {
                if (run.Count > best.Length) best = [.. run];
                run.Clear();
            }

            run.Add(column);
        }

        if (run.Count > best.Length) best = [.. run];
        return best;
    }
}
