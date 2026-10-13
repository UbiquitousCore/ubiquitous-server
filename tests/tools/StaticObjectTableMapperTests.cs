using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class StaticObjectTableMapperTests
{
    private static readonly StaticObjectTableColumns Columns = new(0, 1, 2, 3, 4, 5, 6);
    private static readonly HashSet<ushort> Maps = [7];

    [Fact]
    public void MapStaticObjects_ConvertsRowsWithResolvedMap()
    {
        var data = BuildTable(rows: 2);
        WriteStaticObject(data, rows: 2, row: 0, id: 201, map: 7, entry: 500, x: 4, y: 5, z: 6, yaw: 1);
        WriteStaticObject(data, rows: 2, row: 1, id: 202, map: 7, entry: 501, x: 7, y: 8, z: 9, yaw: 2);

        var spawns = StaticObjectTableMapper.MapStaticObjects(FTableDocument.Load(data), Columns, Maps);

        Assert.Equal(2, spawns.Count);
        Assert.Equal(new Ubiquitous.Data.StaticObjectSpawnDefinition(201, 7, 500, 4, 5, 6, 1), spawns[0]);
    }

    [Fact]
    public void MapStaticObjects_RejectsMissingMapReference()
    {
        var data = BuildTable(rows: 1);
        WriteStaticObject(data, rows: 1, row: 0, id: 201, map: 99, entry: 500, x: 4, y: 5, z: 6, yaw: 1);

        var error = Assert.Throws<FTableFormatException>(
            () => StaticObjectTableMapper.MapStaticObjects(FTableDocument.Load(data), Columns, Maps));

        Assert.Contains("missing map 99", error.Message);
    }

    [Fact]
    public void MapStaticObjects_RejectsNonFiniteCoordinate()
    {
        var data = BuildTable(rows: 1);
        WriteStaticObject(data, rows: 1, row: 0, id: 201, map: 7, entry: 500, x: float.PositiveInfinity, y: 5, z: 6, yaw: 1);

        Assert.Throws<FTableFormatException>(
            () => StaticObjectTableMapper.MapStaticObjects(FTableDocument.Load(data), Columns, Maps));
    }

    [Fact]
    public void MapStaticObjects_RejectsDuplicateId()
    {
        var data = BuildTable(rows: 2);
        WriteStaticObject(data, rows: 2, row: 0, id: 201, map: 7, entry: 500, x: 4, y: 5, z: 6, yaw: 1);
        WriteStaticObject(data, rows: 2, row: 1, id: 201, map: 7, entry: 501, x: 7, y: 8, z: 9, yaw: 2);

        var error = Assert.Throws<FTableFormatException>(
            () => StaticObjectTableMapper.MapStaticObjects(FTableDocument.Load(data), Columns, Maps));

        Assert.Contains("Duplicate static-object id 201", error.Message);
    }

    [Fact]
    public void MapStaticObjects_RejectsWrongCellType()
    {
        var data = BuildTable(rows: 1);
        WriteStaticObject(data, rows: 1, row: 0, id: 201, map: 7, entry: 500, x: 4, y: 5, z: 6, yaw: 1);

        var shifted = Columns with { Id = 3 };

        Assert.Throws<FTableFormatException>(
            () => StaticObjectTableMapper.MapStaticObjects(FTableDocument.Load(data), shifted, Maps));
    }

    private static void WriteStaticObject(
        byte[] data, int rows, int row, uint id, uint map, uint entry,
        float x, float y, float z, float yaw)
    {
        int at = 44 + rows * 4 + "uuuffff".Length + row * 28;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), map);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 8, 4), entry);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 12, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 16, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 20, 4), z);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 24, 4), yaw);
    }

    private static byte[] BuildTable(int rows)
    {
        const string format = "uuuffff";
        const int entrySize = 28;
        const int headerSize = 44;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + format.Length;
        int offsetStrings = offsetEntry + rows * entrySize;
        var data = new byte[offsetStrings];
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
