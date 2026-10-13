using System.IO.Compression;
using System.Text;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class TableArchiveReaderTests
{
    [Fact]
    public void ReadEntries_RecoversEveryEntryByName()
    {
        var archive = BuildPackedArchive(("tables/achievement.tbl", "hello"u8.ToArray()),
            ("tables/quests.tbl", "world of quests"u8.ToArray()));

        var entries = TableArchiveReader.ReadEntries(archive);

        Assert.Equal(2, entries.Count);
        Assert.Equal("hello"u8.ToArray(), entries["tables/achievement.tbl"]);
        Assert.Equal("world of quests"u8.ToArray(), entries["tables/quests.tbl"]);
    }

    [Fact]
    public void ReadEntries_RoundTripsAnFTableDocumentThroughTheArchive()
    {
        var table = FTableFixture.Build(rows: 1, cols: 1, entrySize: 4, format: "u");
        var archive = BuildPackedArchive(("tables/creature_proto.tbl", table));

        var entries = TableArchiveReader.ReadEntries(archive);
        var document = FTableDocument.Load(entries["tables/creature_proto.tbl"]);

        Assert.Equal(1, document.Header.Rows);
    }

    [Fact]
    public void ReadEntries_RejectsAnEntryWhoseLocalHeaderLostTheSubstitutedMagic()
    {
        var archive = BuildPackedArchive(("tables/achievement.tbl", "hello"u8.ToArray()));
        int magicAt = IndexOf(archive, "GBMP"u8.ToArray());
        archive[magicAt] = (byte)'X';

        Assert.Throws<TableArchiveFormatException>(() => TableArchiveReader.ReadEntries(archive));
    }

    [Fact]
    public void ReadEntries_RejectsDataWithNoEndOfCentralDirectoryRecord()
    {
        Assert.Throws<TableArchiveFormatException>(() => TableArchiveReader.ReadEntries(new byte[10]));
    }

    private static byte[] BuildPackedArchive(params (string Name, byte[] Contents)[] files)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, contents) in files)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(contents);
            }
        }

        byte[] bytes = stream.ToArray();
        byte[] standardMagic = [0x50, 0x4B, 0x03, 0x04];
        byte[] packedMagic = "GBMP"u8.ToArray();
        for (int i = 0; i + 4 <= bytes.Length; i++)
        {
            if (bytes.AsSpan(i, 4).SequenceEqual(standardMagic))
            {
                packedMagic.CopyTo(bytes, i);
            }
        }

        return bytes;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        throw new InvalidOperationException("Needle not found in fixture.");
    }
}

internal static class FTableFixture
{
    public static byte[] Build(int rows, int cols, int entrySize, string format)
    {
        const int headerSize = 44;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + cols;
        int offsetStrings = offsetEntry + rows * entrySize;
        var data = new byte[offsetStrings];
        Encoding.ASCII.GetBytes("FTABLE").CopyTo(data, 0);
        WriteU32(data, 8, 0x100000);
        WriteU32(data, 12, rows);
        WriteU32(data, 16, cols);
        WriteU32(data, 20, offsetIndex);
        WriteU32(data, 24, offsetFormat);
        WriteU32(data, 28, offsetEntry);
        WriteU32(data, 32, entrySize);
        WriteU32(data, 36, offsetStrings);
        WriteU32(data, 40, 0);
        Encoding.ASCII.GetBytes(format).CopyTo(data, offsetFormat);
        return data;
    }

    private static void WriteU32(byte[] data, int at, int value)
    {
        data[at] = (byte)value;
        data[at + 1] = (byte)(value >> 8);
        data[at + 2] = (byte)(value >> 16);
        data[at + 3] = (byte)(value >> 24);
    }
}
