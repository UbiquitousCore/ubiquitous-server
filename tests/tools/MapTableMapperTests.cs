using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class MapTableMapperTests
{
    private static readonly MapTableColumns Columns = new(0, 1);

    [Fact]
    public void MapMaps_ConvertsRowsToCanonicalDefinitions()
    {
        var pool = new byte[]
        {
            (byte)'t', (byte)'o', (byte)'w', (byte)'n', 0,
            (byte)'d', (byte)'u', (byte)'n', (byte)'g', (byte)'e', (byte)'o', (byte)'n', 0,
        };
        var data = BuildTable(rows: 2, pool);
        WriteMap(data, rows: 2, row: 0, id: 7, nameOffset: 74, nameLength: 5);
        WriteMap(data, rows: 2, row: 1, id: 13, nameOffset: 79, nameLength: 8);

        var maps = MapTableMapper.MapMaps(FTableDocument.Load(data), Columns);

        Assert.Equal(2, maps.Count);
        Assert.Equal(new Ubiquitous.Data.MapDefinition(7, "town"), maps[0]);
        Assert.Equal(new Ubiquitous.Data.MapDefinition(13, "dungeon"), maps[1]);
    }

    [Fact]
    public void MapMaps_RejectsBlankName()
    {
        var pool = new byte[] { 0 };
        var data = BuildTable(rows: 1, pool);
        WriteMap(data, rows: 1, row: 0, id: 7, nameOffset: 60, nameLength: 1);

        Assert.Throws<FTableFormatException>(
            () => MapTableMapper.MapMaps(FTableDocument.Load(data), Columns));
    }

    [Fact]
    public void MapMaps_RejectsDuplicateMapId()
    {
        var pool = new byte[] { (byte)'t', 0 };
        var data = BuildTable(rows: 2, pool);
        WriteMap(data, rows: 2, row: 0, id: 7, nameOffset: 74, nameLength: 2);
        WriteMap(data, rows: 2, row: 1, id: 7, nameOffset: 74, nameLength: 2);

        var error = Assert.Throws<FTableFormatException>(
            () => MapTableMapper.MapMaps(FTableDocument.Load(data), Columns));

        Assert.Contains("Duplicate map id 7", error.Message);
    }

    [Fact]
    public void MapMaps_ReadsFileWhenTheColumnIsPinned()
    {
        var pool = new byte[]
        {
            (byte)'t', (byte)'o', (byte)'w', (byte)'n', 0,
            (byte)'w', (byte)'o', (byte)'r', (byte)'l', (byte)'d', (byte)'/', (byte)'t', (byte)'.', (byte)'w',
            (byte)'l', (byte)'d', 0,
        };
        var data = BuildTableWithFile(pool);

        var map = Assert.Single(MapTableMapper.MapMaps(FTableDocument.Load(data), new MapTableColumns(0, 1, File: 2)));

        Assert.Equal("world/t.wld", map.File);
    }

    private static byte[] BuildTableWithFile(byte[] pool)
    {
        const string format = "kss";
        const int entrySize = 16;
        const int headerSize = 44;
        const int rows = 1;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + format.Length;
        int offsetStrings = offsetEntry + rows * entrySize;
        var data = new byte[offsetStrings + pool.Length];
        pool.CopyTo(data, offsetStrings);
        WriteAscii(data, 0, "FTABLE");
        WriteU32(data, 8, 0x100000);
        WriteU32(data, 12, rows);
        WriteU32(data, 16, format.Length);
        WriteU32(data, 20, offsetIndex);
        WriteU32(data, 24, offsetFormat);
        WriteU32(data, 28, offsetEntry);
        WriteU32(data, 32, entrySize);
        WriteU32(data, 36, offsetStrings);
        WriteU32(data, 40, pool.Length);
        WriteAscii(data, offsetFormat, format);

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offsetEntry, 4), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offsetEntry + 4, 4), (uint)offsetStrings);
        BitConverter.GetBytes((short)5).CopyTo(data, offsetEntry + 8);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offsetEntry + 10, 4), (uint)(offsetStrings + 5));
        BitConverter.GetBytes((short)12).CopyTo(data, offsetEntry + 14);
        return data;
    }

    [Fact]
    public void MapMaps_RejectsWrongCellType()
    {
        var pool = new byte[] { (byte)'t', 0 };
        var data = BuildTable(rows: 1, pool);
        WriteMap(data, rows: 1, row: 0, id: 7, nameOffset: 60, nameLength: 2);

        var shifted = Columns with { Id = 1 };

        Assert.Throws<FTableFormatException>(
            () => MapTableMapper.MapMaps(FTableDocument.Load(data), shifted));
    }

    private static void WriteMap(
        byte[] data, int rows, int row, uint id, int nameOffset, short nameLength)
    {
        int at = 44 + rows * 4 + "ks".Length + row * 10;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), (uint)nameOffset);
        BitConverter.GetBytes(nameLength).CopyTo(data, at + 8);
    }

    private static byte[] BuildTable(int rows, byte[]? pool = null)
    {
        const string format = "ks";
        const int entrySize = 10;
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
        WriteU32(data, 40, pool?.Length ?? 0);
        WriteAscii(data, offsetFormat, format);
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
