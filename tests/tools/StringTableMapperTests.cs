using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class StringTableMapperTests
{
    private const int EntrySize = 10;

    [Fact]
    public void MapStrings_ResolvesTextById()
    {
        var pool = new byte[] { (byte)'w', (byte)'o', (byte)'l', (byte)'f', 0, (byte)'o', (byte)'r', (byte)'c', 0 };
        var data = BuildTable("us", rows: 2, pool);
        WriteRow(data, "us", rows: 2, row: 0, id: 1001, poolOffset: 0, textLength: 5);
        WriteRow(data, "us", rows: 2, row: 1, id: 1002, poolOffset: 5, textLength: 4);

        var strings = StringTableMapper.MapStrings(FTableDocument.Load(data));

        Assert.Equal(2, strings.Count);
        Assert.Equal("wolf", strings[1001]);
        Assert.Equal("orc", strings[1002]);
    }

    [Fact]
    public void MapStrings_FirstDuplicateIdWins()
    {
        var pool = new byte[] { (byte)'a', 0, (byte)'b', 0 };
        var data = BuildTable("us", rows: 2, pool);
        WriteRow(data, "us", rows: 2, row: 0, id: 5, poolOffset: 0, textLength: 2);
        WriteRow(data, "us", rows: 2, row: 1, id: 5, poolOffset: 2, textLength: 2);

        var strings = StringTableMapper.MapStrings(FTableDocument.Load(data));

        Assert.Single(strings);
        Assert.Equal("a", strings[5]);
    }

    [Fact]
    public void MapStrings_RejectsAWrongIdCellType()
    {
        var pool = new byte[] { (byte)'x', 0 };
        var data = BuildTable("fs", rows: 1, pool);
        WriteFloatRow(data, rows: 1, row: 0, id: 1.5f, poolOffset: 0, textLength: 1);

        Assert.Throws<FTableFormatException>(() => StringTableMapper.MapStrings(FTableDocument.Load(data)));
    }

    private static int OffsetEntry(string format, int rows) => 44 + rows * 4 + format.Length;

    private static int OffsetStrings(string format, int rows) => OffsetEntry(format, rows) + rows * EntrySize;

    private static byte[] BuildTable(string format, int rows, byte[] pool)
    {
        const int headerSize = 44;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = OffsetEntry(format, rows);
        int offsetStrings = OffsetStrings(format, rows);
        var data = new byte[offsetStrings + pool.Length];
        pool.CopyTo(data, offsetStrings);
        WriteAscii(data, 0, "FTABLE");
        WriteU32(data, 8, 0x100000);
        WriteU32(data, 12, rows);
        WriteU32(data, 16, format.Length);
        WriteU32(data, 20, offsetIndex);
        WriteU32(data, 24, offsetFormat);
        WriteU32(data, 28, offsetEntry);
        WriteU32(data, 32, EntrySize);
        WriteU32(data, 36, offsetStrings);
        WriteU32(data, 40, pool.Length);
        WriteAscii(data, offsetFormat, format);
        return data;
    }

    private static void WriteRow(
        byte[] data, string format, int rows, int row, uint id, int poolOffset, short textLength)
    {
        int at = OffsetEntry(format, rows) + row * EntrySize;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), (uint)(OffsetStrings(format, rows) + poolOffset));
        BitConverter.GetBytes(textLength).CopyTo(data, at + 8);
    }

    private static void WriteFloatRow(byte[] data, int rows, int row, float id, int poolOffset, short textLength)
    {
        int at = OffsetEntry("fs", rows) + row * EntrySize;
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at, 4), id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), (uint)(OffsetStrings("fs", rows) + poolOffset));
        BitConverter.GetBytes(textLength).CopyTo(data, at + 8);
    }

    private static void WriteU32(byte[] data, int at, int value)
    {
        data[at] = (byte)value;
        data[at + 1] = (byte)(value >> 8);
        data[at + 2] = (byte)(value >> 16);
        data[at + 3] = (byte)(value >> 24);
    }

    private static void WriteAscii(byte[] data, int at, string text)
    {
        for (int i = 0; i < text.Length && at + i < data.Length; i++)
        {
            data[at + i] = (byte)text[i];
        }
    }
}
