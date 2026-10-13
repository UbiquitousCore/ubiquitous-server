using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class TableLayoutTests
{
    [Fact]
    public void Read_AcceptsSyntheticHeaderWithFittingSections()
    {
        var header = TableLayoutReader.Read(BuildTable(rows: 2, cols: 3, entrySize: 12));

        Assert.Equal(2, header.Rows);
        Assert.Equal(3, header.Cols);
        Assert.Equal("uif", header.ColumnTypes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Read_RejectsTruncatedInput(int length)
    {
        Assert.Throws<FTableFormatException>(
            () => TableLayoutReader.Read(new byte[length]));
    }

    [Fact]
    public void Read_RejectsBadMagic()
    {
        var data = BuildTable(rows: 1, cols: 1, entrySize: 4);
        data[0] = (byte)'X';

        Assert.Throws<FTableFormatException>(() => TableLayoutReader.Read(data));
    }

    [Fact]
    public void Read_RejectsIndexPastEndOfFile()
    {
        var data = BuildTable(rows: 2, cols: 1, entrySize: 4);
        WriteU32(data, 20, 10_000);

        Assert.Throws<FTableFormatException>(() => TableLayoutReader.Read(data));
    }

    [Fact]
    public void Read_RejectsEntryRecordsPastEndOfFile()
    {
        var data = BuildTable(rows: 2, cols: 1, entrySize: 4);
        WriteU32(data, 28, 10_000);

        Assert.Throws<FTableFormatException>(() => TableLayoutReader.Read(data));
    }

    private static byte[] BuildTable(int rows, int cols, int entrySize)
    {
        const int headerSize = 44;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + cols;
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
        WriteAscii(data, offsetFormat, "uif".Substring(0, cols));
        return data;
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
