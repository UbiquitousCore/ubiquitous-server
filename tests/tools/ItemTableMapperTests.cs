using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class ItemTableMapperTests
{
    private static readonly ItemTableColumns Columns = new(0, 1, 2, 3, 4);

    [Fact]
    public void MapItems_ConvertsRowsToCanonicalDefinitions()
    {
        var pool = new byte[] { (byte)'s', (byte)'w', (byte)'o', (byte)'r', (byte)'d', 0 };
        var data = BuildTable(rows: 1, pool);
        WriteItem(data, rows: 1, row: 0, id: 100, nameOffset: 69, nameLength: 6,
            @class: 2, subClass: 6, display: 700);

        var items = ItemTableMapper.MapItems(FTableDocument.Load(data), Columns);

        Assert.Equal(new Ubiquitous.Data.ItemDefinition(100, "sword", 2, 6, 700), Assert.Single(items));
    }

    private static byte[] WeaponRow(uint whole, float fractional)
    {
        var pool = new byte[] { (byte)'s', 0 };
        var data = BuildTable(rows: 1, pool, columnFormat: "ksccuf", entrySizeOverride: 20);
        int at = 44 + 4 + 6;
        int poolAt = at + 20;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), 100u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), (uint)poolAt);
        BitConverter.GetBytes((short)2).CopyTo(data, at + 8);
        data[at + 10] = 2;
        data[at + 11] = 6;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 12, 4), whole);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 16, 4), fractional);
        return data;
    }

    [Fact]
    public void MapItems_ReadsWeaponDamageWhetherItIsWrittenWholeOrFractional()
    {
        var data = WeaponRow(whole: 3700u, fractional: 251.6f);
        var columns = new ItemTableColumns(0, 1, 2, 3, 4, SpeedMs: 4, MinDamage: 5);

        var item = Assert.Single(ItemTableMapper.MapItems(FTableDocument.Load(data), columns));

        Assert.Equal(3700u, item.SpeedMs);
        Assert.Equal(252u, item.MinDamage);
    }

    [Fact]
    public void MapItems_LeavesDamageAndArmourUnsaidWhereTheRowCarriesNone()
    {
        var data = WeaponRow(whole: 0u, fractional: 0f);
        var columns = new ItemTableColumns(0, 1, 2, 3, 4, SpeedMs: 4, MinDamage: 5, Armor: 4);

        var item = Assert.Single(ItemTableMapper.MapItems(FTableDocument.Load(data), columns));

        Assert.Null(item.MinDamage);
        Assert.Null(item.Armor);
        Assert.Null(item.SpeedMs);
    }

    [Fact]
    public void MapItems_RejectsNegativeByteColumn()
    {
        var pool = new byte[] { (byte)'s', 0 };
        var data = BuildTable(rows: 1, pool);
        WriteItem(data, rows: 1, row: 0, id: 100, nameOffset: 69, nameLength: 2,
            @class: 2, subClass: 6, display: 700);
        data[53 + 11] = 0xFF;

        Assert.Throws<FTableFormatException>(
            () => ItemTableMapper.MapItems(FTableDocument.Load(data), Columns));
    }

    [Fact]
    public void MapItems_RejectsDuplicateItemId()
    {
        var pool = new byte[] { (byte)'s', 0 };
        var data = BuildTable(rows: 2, pool);
        WriteItem(data, rows: 2, row: 0, id: 100, nameOffset: 89, nameLength: 2,
            @class: 2, subClass: 6, display: 700);
        WriteItem(data, rows: 2, row: 1, id: 100, nameOffset: 89, nameLength: 2,
            @class: 2, subClass: 6, display: 701);

        var error = Assert.Throws<FTableFormatException>(
            () => ItemTableMapper.MapItems(FTableDocument.Load(data), Columns));

        Assert.Contains("Duplicate item id 100", error.Message);
    }

    [Theory]
    [InlineData(10u)]
    [InlineData(2u)]
    [InlineData(0u)]
    public void MapItems_ReadsTheAllowableClassColumn(uint mask)
    {
        var pool = new byte[] { (byte)'b', (byte)'o', (byte)'w', 0 };
        var data = BuildTable(rows: 1, pool, extraUIntColumn: true);
        WriteItem(data, rows: 1, row: 0, id: 4149, nameOffset: 73, nameLength: 4,
            @class: 2, subClass: 16, display: 0, allowedClasses: mask);

        var items = ItemTableMapper.MapItems(
            FTableDocument.Load(data), Columns with { AllowableClassMask = 5 });

        Assert.Equal(mask, Assert.Single(items).AllowableClassMask);
    }

    [Fact]
    public void MapItems_LeavesTheAllowableClassMaskUnsetWhenTheColumnIsUnpinned()
    {
        var pool = new byte[] { (byte)'b', (byte)'o', (byte)'w', 0 };
        var data = BuildTable(rows: 1, pool, extraUIntColumn: true);
        WriteItem(data, rows: 1, row: 0, id: 4149, nameOffset: 73, nameLength: 4,
            @class: 2, subClass: 16, display: 0, allowedClasses: 10);

        var items = ItemTableMapper.MapItems(FTableDocument.Load(data), Columns);

        Assert.Null(Assert.Single(items).AllowableClassMask);
    }

    private static void WriteItem(
        byte[] data, int rows, int row, uint id, int nameOffset, short nameLength,
        byte @class, byte subClass, uint display, uint? allowedClasses = null)
    {
        string format = allowedClasses is null ? "ksccu" : "ksccuu";
        int entrySize = allowedClasses is null ? 16 : 20;
        int at = 44 + rows * 4 + format.Length + row * entrySize;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), (uint)nameOffset);
        BitConverter.GetBytes(nameLength).CopyTo(data, at + 8);
        data[at + 10] = @class;
        data[at + 11] = subClass;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 12, 4), display);
        if (allowedClasses is not null)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 16, 4), allowedClasses.Value);
        }
    }

    private static byte[] BuildTable(
        int rows, byte[]? pool = null, bool extraUIntColumn = false,
        string? columnFormat = null, int? entrySizeOverride = null)
    {
        string format = columnFormat ?? (extraUIntColumn ? "ksccuu" : "ksccu");
        int entrySize = entrySizeOverride ?? (extraUIntColumn ? 20 : 16);
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
