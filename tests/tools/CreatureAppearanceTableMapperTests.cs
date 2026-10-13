using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class CreatureAppearanceTableMapperTests
{
    private static readonly CreatureAppearanceTableColumns Columns = new(Entry: 0, NameTextId: 1);

    [Fact]
    public void MapNameStringIds_ResolvesNameStringIdByEntry()
    {
        var data = BuildTable(rows: 2, cols: 2);
        WriteRow(data, rows: 2, cols: 2, row: 0, values: [9001, 5001]);
        WriteRow(data, rows: 2, cols: 2, row: 1, values: [9002, 5002]);

        var byEntry = CreatureAppearanceTableMapper.MapNameStringIds(FTableDocument.Load(data), Columns);

        Assert.Equal(2, byEntry.Count);
        Assert.Equal(5001u, byEntry[9001]);
        Assert.Equal(5002u, byEntry[9002]);
    }

    [Fact]
    public void MapNameStringIds_FirstDuplicateEntryWins()
    {
        var data = BuildTable(rows: 2, cols: 2);
        WriteRow(data, rows: 2, cols: 2, row: 0, values: [9001, 5001]);
        WriteRow(data, rows: 2, cols: 2, row: 1, values: [9001, 5099]);

        var byEntry = CreatureAppearanceTableMapper.MapNameStringIds(FTableDocument.Load(data), Columns);

        Assert.Single(byEntry);
        Assert.Equal(5001u, byEntry[9001]);
    }

    [Fact]
    public void MapNameStringIds_RejectsAWrongCellType()
    {
        var data = BuildTable(rows: 1, cols: 2, format: "fu");
        int offsetEntry = 44 + 1 * 4 + 2;
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offsetEntry, 4), 1.5f);

        Assert.Throws<FTableFormatException>(
            () => CreatureAppearanceTableMapper.MapNameStringIds(FTableDocument.Load(data), Columns));
    }

    private static byte[] BuildTable(int rows, int cols, string? format = null)
    {
        format ??= new string('u', cols);
        const int headerSize = 44;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + cols;
        int entrySize = cols * 4;
        int offsetStrings = offsetEntry + rows * entrySize;
        var data = new byte[offsetStrings];
        WriteAscii(data, 0, "FTABLE");
        WriteU32(data, 8, 0x100000);
        WriteU32(data, 12, rows);
        WriteU32(data, 16, cols);
        WriteU32(data, 20, offsetIndex);
        WriteU32(data, 24, offsetFormat);
        WriteU32(data, 28, offsetEntry);
        WriteU32(data, 32, entrySize);
        WriteU32(data, 36, offsetStrings);
        WriteU32(data, 40, 0);
        WriteAscii(data, offsetFormat, format);
        return data;
    }

    private static void WriteRow(byte[] data, int rows, int cols, int row, uint[] values)
    {
        int offsetEntry = 44 + rows * 4 + cols;
        int entrySize = cols * 4;
        int at = offsetEntry + row * entrySize;
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + i * 4, 4), values[i]);
        }
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
