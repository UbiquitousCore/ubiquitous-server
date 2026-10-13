using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class ZoneTableMapperTests
{
    private static readonly ZoneTableColumns Columns = new(0, 1, 2, 3, 4, 5, 6, 7);
    private static readonly HashSet<ushort> Maps = [7];

    [Fact]
    public void MapZones_ConvertsRowsWithResolvedMap()
    {
        var pool = new byte[]
        {
            (byte)'m', (byte)'e', (byte)'a', (byte)'d', (byte)'o', (byte)'w', 0,
            (byte)'f', (byte)'i', (byte)'e', (byte)'l', (byte)'d', 0,
        };
        var data = BuildTable(rows: 2, pool);
        WriteZone(data, rows: 2, row: 0, id: 3, map: 7, parent: 0,
            nameOffset: 128, nameLength: 7, minX: -10, minZ: -20, maxX: 10, maxZ: 20);
        WriteZone(data, rows: 2, row: 1, id: 4, map: 7, parent: 3,
            nameOffset: 135, nameLength: 6, minX: 0, minZ: 0, maxX: 1, maxZ: 1);

        var zones = ZoneTableMapper.MapZones(FTableDocument.Load(data), Columns, Maps);

        Assert.Equal(2, zones.Count);
        Assert.Equal(new Ubiquitous.Data.ZoneDefinition(3, 7, 0, "meadow", -10, -20, 10, 20), zones[0]);
        Assert.Equal((ushort)7, zones[1].MapId);
    }

    [Fact]
    public void MapZones_RejectsMissingMapReference()
    {
        var pool = new byte[] { (byte)'m', 0 };
        var data = BuildTable(rows: 1, pool);
        WriteZone(data, rows: 1, row: 0, id: 3, map: 99, parent: 0,
            nameOffset: 90, nameLength: 2, minX: 0, minZ: 0, maxX: 1, maxZ: 1);

        var error = Assert.Throws<FTableFormatException>(
            () => ZoneTableMapper.MapZones(FTableDocument.Load(data), Columns, Maps));

        Assert.Contains("missing map 99", error.Message);
    }

    [Fact]
    public void MapZones_RejectsInvertedBounds()
    {
        var pool = new byte[] { (byte)'m', 0 };
        var data = BuildTable(rows: 1, pool);
        WriteZone(data, rows: 1, row: 0, id: 3, map: 7, parent: 0,
            nameOffset: 90, nameLength: 2, minX: 10, minZ: 0, maxX: -10, maxZ: 1);

        var error = Assert.Throws<FTableFormatException>(
            () => ZoneTableMapper.MapZones(FTableDocument.Load(data), Columns, Maps));

        Assert.Contains("inverted bounds", error.Message);
    }

    [Fact]
    public void MapZones_RejectsBlankName()
    {
        var pool = new byte[] { 0 };
        var data = BuildTable(rows: 1, pool);
        WriteZone(data, rows: 1, row: 0, id: 3, map: 7, parent: 0,
            nameOffset: 90, nameLength: 1, minX: 0, minZ: 0, maxX: 1, maxZ: 1);

        Assert.Throws<FTableFormatException>(
            () => ZoneTableMapper.MapZones(FTableDocument.Load(data), Columns, Maps));
    }

    [Fact]
    public void MapZones_RejectsDuplicateZoneId()
    {
        var pool = new byte[] { (byte)'m', 0 };
        var data = BuildTable(rows: 2, pool);
        WriteZone(data, rows: 2, row: 0, id: 3, map: 7, parent: 0,
            nameOffset: 128, nameLength: 2, minX: 0, minZ: 0, maxX: 1, maxZ: 1);
        WriteZone(data, rows: 2, row: 1, id: 3, map: 7, parent: 0,
            nameOffset: 128, nameLength: 2, minX: 0, minZ: 0, maxX: 1, maxZ: 1);

        var error = Assert.Throws<FTableFormatException>(
            () => ZoneTableMapper.MapZones(FTableDocument.Load(data), Columns, Maps));

        Assert.Contains("Duplicate zone id 3", error.Message);
    }

    [Fact]
    public void MapZones_RejectsWrongCellType()
    {
        var pool = new byte[] { (byte)'m', 0 };
        var data = BuildTable(rows: 1, pool);
        WriteZone(data, rows: 1, row: 0, id: 3, map: 7, parent: 0,
            nameOffset: 90, nameLength: 2, minX: 0, minZ: 0, maxX: 1, maxZ: 1);

        var shifted = Columns with { Id = 4 };

        Assert.Throws<FTableFormatException>(
            () => ZoneTableMapper.MapZones(FTableDocument.Load(data), shifted, Maps));
    }

    private static void WriteZone(
        byte[] data, int rows, int row, uint id, uint map, uint parent,
        int nameOffset, short nameLength, float minX, float minZ, float maxX, float maxZ)
    {
        int at = 44 + rows * 4 + "uuusffff".Length + row * 34;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), map);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 8, 4), parent);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 12, 4), (uint)nameOffset);
        BitConverter.GetBytes(nameLength).CopyTo(data, at + 16);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 18, 4), minX);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 22, 4), minZ);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 26, 4), maxX);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 30, 4), maxZ);
    }

    private static byte[] BuildTable(int rows, byte[]? pool = null)
    {
        const string format = "uuusffff";
        const int entrySize = 34;
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
