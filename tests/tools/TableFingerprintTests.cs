using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class TableFingerprintTests
{
    [Fact]
    public void Capture_RoundTripsThroughJson()
    {
        var entries = new Dictionary<string, byte[]>
        {
            ["tables/spell_new.tbl"] = Build("uuus", [new Dictionary<int, object> { [0] = 1u, [1] = 2u, [2] = 3u, [3] = "bolt" }]),
            ["tables/zones.tbl"] = Build("iii", [new Dictionary<int, object> { [0] = 1, [1] = 2, [2] = 3 }]),
            ["other/readme.txt"] = new byte[] { 1, 2, 3 },
        };

        var captured = TableFingerprint.Capture(entries, sourceHash: "abc");
        Assert.Equal("abc", captured.SourceHash);
        Assert.Equal(
            ["tables/spell_new.tbl", "tables/zones.tbl"],
            captured.Tables.Select(t => t.Name));

        var path = Path.Combine(Path.GetTempPath(), "fingerprint-test-" + Guid.NewGuid() + ".json");
        try
        {
            captured.Save(path);
            var loaded = TableFingerprint.Load(path);
            Assert.Equal(captured.SourceHash, loaded.SourceHash);
            Assert.Equal(captured.Tables, loaded.Tables);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Compare_PassesOnIdenticalShapesAndFailsOnFormatDrift()
    {
        var golden = new TableFingerprint("hash", new[]
        {
            new TableFingerprintEntry("tables/spell_new.tbl", 10, 4, "uuus"),
            new TableFingerprintEntry("tables/zones.tbl", 5, 3, "iii"),
        }, DateTimeOffset.UtcNow);

        var sameShape = new TableFingerprint("other-hash", new[]
        {
            new TableFingerprintEntry("tables/spell_new.tbl", 12, 4, "uuus"),
            new TableFingerprintEntry("tables/zones.tbl", 5, 3, "iii"),
        }, DateTimeOffset.UtcNow);
        var notes = TableFingerprint.Compare(golden, sameShape);
        Assert.DoesNotContain(notes, p => !TableFingerprint.IsNote(p));

        var drifted = new TableFingerprint("other-hash", new[]
        {
            new TableFingerprintEntry("tables/spell_new.tbl", 10, 5, "uuusu"),
            new TableFingerprintEntry("tables/quests.tbl", 1, 2, "uu"),
        }, DateTimeOffset.UtcNow);
        var problems = TableFingerprint.Compare(golden, drifted);
        var failures = problems.Where(p => !TableFingerprint.IsNote(p)).ToList();
        Assert.Contains(failures, p => p.Contains("spell_new") && p.Contains("uuusu"));
        Assert.Contains(failures, p => p.Contains("zones") && p.Contains("missing"));
        // A build's extra tables are informational, never failures.
        Assert.Contains(problems, p => p.Contains("quests") && p.Contains("new table"));
        Assert.DoesNotContain(failures, p => p.Contains("quests"));
    }

    [Fact]
    public void IdentifyBuild_Names114_FromItsPinnedShapes()
    {
        var tables = TableSchemas.Known.Single(b => b.Build == "1.1.4");
        var fingerprint = new TableFingerprint("hash",
            tables.Formats.Select(kv => new TableFingerprintEntry(kv.Key, 3, kv.Value.Length, kv.Value)).ToList(),
            DateTimeOffset.UtcNow);
        Assert.Equal("1.1.4", TableSchemas.IdentifyBuild(fingerprint));
        Assert.Empty(TableSchemas.DescribeDrift(fingerprint, "1.1.4"));
    }

    [Fact]
    public void IdentifyBuild_Tells110From111_ByThePortugueseStrings()
    {
        var formats110 = TableSchemas.Known.Single(b => b.Build == "1.1.0").Formats;
        var tables110 = formats110
            .Select(kv => new TableFingerprintEntry(kv.Key, 3, kv.Value.Length, kv.Value))
            .ToList();
        Assert.Equal("1.1.0", TableSchemas.IdentifyBuild(new TableFingerprint("h", tables110, DateTimeOffset.UtcNow)));
        tables110.Add(new TableFingerprintEntry("tables/String_Menu_pt.tbl", 538, 2, "us"));
        Assert.Equal("1.1.1", TableSchemas.IdentifyBuild(new TableFingerprint("h", tables110, DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void IdentifyBuild_SaysUnknown_WhenALoadBearingFormatMoves()
    {
        var entries = TableSchemas.Known.Single(b => b.Build == "1.1.4").Formats
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        entries["tables/spell_new.tbl"] += "u";
        var fingerprint = new TableFingerprint("hash",
            entries.Select(kv => new TableFingerprintEntry(kv.Key, 3, kv.Value.Length, kv.Value)).ToList(),
            DateTimeOffset.UtcNow);
        Assert.Equal("unknown", TableSchemas.IdentifyBuild(fingerprint));
        var drift = TableSchemas.DescribeDrift(fingerprint, "1.1.4");
        Assert.Single(drift);
        Assert.Contains("spell_new", drift[0]);
    }

    private static byte[] Build(string format, IReadOnlyList<Dictionary<int, object>> rows)
    {
        int cols = format.Length;
        var widths = new int[cols];
        for (int i = 0; i < cols; i++)
            widths[i] = format[i] switch { 's' => 6, 'h' => 2, 'c' => 1, _ => 4 };
        int entrySize = widths.Sum();
        const int headerSize = 44;
        int offsetFormat = headerSize + rows.Count * 4;
        int offsetEntry = offsetFormat + cols;
        var pool = new List<byte>();
        var strings = new Dictionary<(int, int), (int, int)>();
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < cols; c++)
                if (format[c] == 's' && rows[r].TryGetValue(c, out var v) && v is string s)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(s + "\0");
                    strings[(r, c)] = (offsetEntry + entrySize * rows.Count + pool.Count, bytes.Length);
                    pool.AddRange(bytes);
                }
        int offsetStrings = offsetEntry + entrySize * rows.Count;
        var data = new byte[offsetStrings + pool.Count];
        pool.CopyTo(data, offsetStrings);
        void Ascii(int at, string text)
        {
            for (int i = 0; i < text.Length; i++) data[at + i] = (byte)text[i];
        }
        void U32(int at, int value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), (uint)value);
        Ascii(0, "FTABLE");
        U32(12, rows.Count); U32(16, cols); U32(20, headerSize); U32(24, offsetFormat);
        U32(28, offsetEntry); U32(32, entrySize); U32(36, offsetStrings); U32(40, pool.Count);
        Ascii(offsetFormat, format);
        int at = offsetEntry;
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < cols; c++)
            {
                if (format[c] == 's')
                {
                    var (off, len) = strings.TryGetValue((r, c), out var v) ? v : (0, 0);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), (uint)off);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(at + 4, 2), (short)len);
                }
                else if (format[c] == 'f')
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at, 4), rows[r].TryGetValue(c, out var f) ? Convert.ToSingle(f) : 0f);
                else if (format[c] == 'i')
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at, 4), rows[r].TryGetValue(c, out var i) ? Convert.ToInt32(i) : 0);
                else
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), rows[r].TryGetValue(c, out var u) ? Convert.ToUInt32(u) : 0u);
                at += widths[c];
            }
        return data;
    }
}
