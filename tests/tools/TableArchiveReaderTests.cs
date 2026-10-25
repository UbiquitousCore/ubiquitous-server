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

    [Fact]
    public void ReadEntries_DecodesMethod14LzmaEntries()
    {
        // Payload built with LZMA properties 5d00008000 (lc0/lp0/pb2, 8 MB dictionary),
        // header layout copied from a real newer-build tables.bin entry.
        byte[] payload = Convert.FromBase64String(
            "BEEFAF0AAIAAADYeid19SVLhBgLa8goeRA1s4UfokwD8XinSjKEKpPwXlONyTWnX//9KcAAA");
        var archive = BuildMethod14Archive("tables/achievement.tbl", payload, uncompressedSize: 35);

        var entries = TableArchiveReader.ReadEntries(archive);

        Assert.Equal("lzma method fourteen decodes tables"u8.ToArray(), entries["tables/achievement.tbl"]);
    }

    [Fact]
    public void ReadEntries_RejectsALzmaEntryWithANonStandardPropertiesBlock()
    {
        byte[] payload = Convert.FromBase64String(
            "BEEFAF0AAIAAADYeid19SVLhBgLa8goeRA1s4UfokwD8XinSjKEKpPwXlONyTWnX//9KcAAA");
        payload[2] = 7; // properties size is 5 in every pack seen so far
        var archive = BuildMethod14Archive("tables/achievement.tbl", payload, uncompressedSize: 35);

        Assert.Throws<TableArchiveFormatException>(() => TableArchiveReader.ReadEntries(archive));
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

    private static byte[] BuildMethod14Archive(string name, byte[] payload, int uncompressedSize)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        using var stream = new MemoryStream();
        uint localOffset = (uint)stream.Position;
        stream.Write("GBMP"u8); // substituted local header magic
        WriteU16(stream, 20); // version needed
        WriteU16(stream, 0); // flags
        WriteU16(stream, 14); // LZMA
        WriteU16(stream, 0); // time
        WriteU16(stream, 0); // date
        WriteU32(stream, 0); // crc (unchecked by the reader)
        WriteU32(stream, (uint)payload.Length);
        WriteU32(stream, (uint)uncompressedSize);
        WriteU16(stream, (ushort)nameBytes.Length);
        WriteU16(stream, 0); // extra
        stream.Write(nameBytes);
        stream.Write(payload);
        uint centralOffset = (uint)stream.Position;
        WriteU32(stream, 0x02014B50); // central directory signature
        WriteU16(stream, 20); // version made by
        WriteU16(stream, 20); // version needed
        WriteU16(stream, 0); // flags
        WriteU16(stream, 14); // LZMA
        WriteU16(stream, 0); // time
        WriteU16(stream, 0); // date
        WriteU32(stream, 0); // crc
        WriteU32(stream, (uint)payload.Length);
        WriteU32(stream, (uint)uncompressedSize);
        WriteU16(stream, (ushort)nameBytes.Length);
        WriteU16(stream, 0); // extra
        WriteU16(stream, 0); // comment
        WriteU16(stream, 0); // disk
        WriteU16(stream, 0); // internal attributes
        WriteU32(stream, 0); // external attributes
        WriteU32(stream, localOffset);
        stream.Write(nameBytes);
        uint centralSize = (uint)stream.Position - centralOffset;
        WriteU32(stream, 0x06054B50); // end of central directory
        WriteU16(stream, 0); // disk
        WriteU16(stream, 0); // central directory disk
        WriteU16(stream, 1); // entries on disk
        WriteU16(stream, 1); // entries total
        WriteU32(stream, centralSize);
        WriteU32(stream, centralOffset);
        WriteU16(stream, 0); // comment
        return stream.ToArray();
    }

    private static void WriteU16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
    }

    private static void WriteU32(Stream stream, uint value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 24));
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
