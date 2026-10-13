using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class FTableRowTests
{

    [Fact]
    public void ReadRow_DecodesEveryScalarWidth()
    {
        const string format = "kuifhc";
        const int entrySize = 4 + 4 + 4 + 4 + 2 + 1;
        var data = BuildTable(format, rows: 1, entrySize);
        int at = EntryOffset(format.Length, rows: 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), 4000000000u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), 7u);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at + 8, 4), -1500000);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 12, 4), 1.5f);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(at + 16, 2), -32000);
        data[at + 18] = 251;

        var header = TableLayoutReader.Read(data);
        var cells = FTableRowReader.ReadRow(data, header, 0);

        Assert.Equal(4000000000u, Assert.IsType<uint>(cells[0]));
        Assert.Equal(7u, Assert.IsType<uint>(cells[1]));
        Assert.Equal(-1500000, Assert.IsType<int>(cells[2]));
        Assert.Equal(1.5f, Assert.IsType<float>(cells[3]));
        Assert.Equal((short)-32000, Assert.IsType<short>(cells[4]));
        Assert.Equal((sbyte)-5, Assert.IsType<sbyte>(cells[5]));
    }

    [Fact]
    public void ReadRow_UnknownColumnDecodesAsUInt32()
    {
        var data = BuildTable("x", rows: 1, entrySize: 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(EntryOffset(1, rows: 1), 4), 0x12345678u);

        var header = TableLayoutReader.Read(data);
        var cells = FTableRowReader.ReadRow(data, header, 0);

        Assert.Equal(0x12345678u, Assert.Single(cells));
    }

    [Fact]
    public void ReadRow_DecodesPoolStringWithMixedColumns()
    {
        var data = BuildTable("us", rows: 1, entrySize: 10, pool: new byte[] { (byte)'h', (byte)'i', 0 });
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(EntryOffset(2, rows: 1), 4), 5u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(EntryOffset(2, rows: 1) + 4, 4), (uint)(EntryOffset(2, rows: 1) + 10));
        BitConverter.GetBytes((short)3).CopyTo(data, EntryOffset(2, rows: 1) + 8);

        var header = TableLayoutReader.Read(data);
        var cells = FTableRowReader.ReadRow(data, header, 0);

        Assert.Equal(5u, Assert.IsType<uint>(cells[0]));
        Assert.Equal("hi", Assert.IsType<string>(cells[1]));
    }

    [Fact]
    public void ReadRow_FallsBackToSingleByteTextForNonUtf8()
    {
        var data = BuildTable("s", rows: 1, entrySize: 6, pool: new byte[] { 0x92, 0 });
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(EntryOffset(1, rows: 1), 4), (uint)EntryOffset(1, rows: 1) + 6);
        BitConverter.GetBytes((short)2).CopyTo(data, EntryOffset(1, rows: 1) + 4);

        var header = TableLayoutReader.Read(data);
        var cells = FTableRowReader.ReadRow(data, header, 0);

        Assert.Equal("’", Assert.IsType<string>(cells[0]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ReadRow_InvalidPoolReferenceReadsEmpty(int length)
    {
        var data = BuildTable("s", rows: 1, entrySize: 6);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(EntryOffset(1, rows: 1), 4), 10_000u);
        BitConverter.GetBytes((short)length).CopyTo(data, EntryOffset(1, rows: 1) + 4);

        var header = TableLayoutReader.Read(data);
        var cells = FTableRowReader.ReadRow(data, header, 0);

        Assert.Equal(string.Empty, Assert.IsType<string>(cells[0]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void ReadRow_RejectsIndexOutsideRows(int index)
    {
        var data = BuildTable("u", rows: 1, entrySize: 4);
        var header = TableLayoutReader.Read(data);

        Assert.Throws<FTableFormatException>(() => FTableRowReader.ReadRow(data, header, index));
    }

    [Fact]
    public void ReadByKey_IndexesRowsByIdInRowOrder()
    {
        var data = BuildTable("u", rows: 2, entrySize: 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(EntryOffset(1, rows: 2), 4), 11u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(EntryOffset(1, rows: 2) + 4, 4), 22u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(44, 4), 100u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(48, 4), 200u);

        var header = TableLayoutReader.Read(data);
        var rows = FTableRowReader.ReadByKey(data, header);

        Assert.Equal(2, rows.Count);
        Assert.Equal(11u, Assert.IsType<uint>(rows[100u][0]));
        Assert.Equal(22u, Assert.IsType<uint>(rows[200u][0]));
    }

    [Fact]
    public void ReadByKey_LaterDuplicateKeysLose()
    {
        var data = BuildTable("u", rows: 2, entrySize: 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(EntryOffset(1, rows: 2), 4), 11u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(EntryOffset(1, rows: 2) + 4, 4), 22u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(44, 4), 100u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(48, 4), 100u);

        var header = TableLayoutReader.Read(data);
        var rows = FTableRowReader.ReadByKey(data, header);

        Assert.Single(rows);
        Assert.Equal(11u, Assert.IsType<uint>(rows[100u][0]));
    }

    [Fact]
    public void Load_ReturnsRowsInOrderWithKeyedIndex()
    {
        var data = BuildTable("uk", rows: 2, entrySize: 8);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(54, 4), 11u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(54 + 4, 4), 101u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(62, 4), 22u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(62 + 4, 4), 202u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(44, 4), 7u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(48, 4), 8u);

        var document = FTableDocument.Load(data);

        Assert.Equal(2, document.Rows.Count);
        Assert.Equal(11u, Assert.IsType<uint>(document.Rows[0][0]));
        Assert.Equal(22u, Assert.IsType<uint>(document.Rows[1][0]));
        Assert.Equal(2, document.ByKey.Count);
        Assert.Same(document.Rows[0], document.ByKey[7u]);
    }

    [Fact]
    public void Load_RejectsTruncatedTable()
    {
        var data = BuildTable("uk", rows: 2, entrySize: 8);

        Assert.Throws<FTableFormatException>(
            () => FTableDocument.Load(data.AsSpan(0, data.Length - 1)));
    }

    private static byte[] BuildTable(string format, int rows, int entrySize, byte[]? pool = null)
    {
        const int headerSize = 44;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + format.Length;
        int offsetStrings = offsetEntry + rows * entrySize;
        var data = new byte[offsetStrings + (pool?.Length ?? 0)];
        pool?.CopyTo(data, offsetStrings);
        WriteAscii(data, 0, "FTABLE");
        WriteU32(data, 8, 0x100000);
        WriteU32(data, 12, rows);
        WriteU32(data, 16, format.Length);
        WriteU32(data, 20, offsetIndex);
        WriteU32(data, 24, offsetFormat);
        WriteU32(data, 28, offsetEntry);
        WriteU32(data, 32, entrySize);
        WriteU32(data, 36, offsetStrings);
        WriteU32(data, 40, 0);
        WriteAscii(data, offsetFormat, format);
        return data;
    }

    private static int EntryOffset(int cols, int rows) => 44 + rows * 4 + cols;

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
