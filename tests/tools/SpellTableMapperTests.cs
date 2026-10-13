using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class SpellTableMapperTests
{
    private static readonly SpellTableColumns Columns = new(0, 1, 2, 3, 4);

    [Fact]
    public void MapSpells_ConvertsRowsToCanonicalDefinitions()
    {
        var pool = new byte[] { (byte)'b', (byte)'o', (byte)'l', (byte)'t', 0 };
        var data = BuildTable(rows: 1, pool);
        WriteSpell(data, rows: 1, row: 0, id: 1, nameOffset: 75, nameLength: 5,
            castTimeMs: 1000, cooldownMs: 2000, maxRange: 30f);

        var spells = SpellTableMapper.MapSpells(FTableDocument.Load(data), Columns);

        Assert.Equal(new Ubiquitous.Data.SpellDefinition(1, "bolt", 1000, 2000, 30f), Assert.Single(spells));
    }

    [Fact]
    public void MapSpells_RejectsNegativeCastTime()
    {
        var pool = new byte[] { (byte)'b', 0 };
        var data = BuildTable(rows: 1, pool);
        WriteSpell(data, rows: 1, row: 0, id: 1, nameOffset: 75, nameLength: 2,
            castTimeMs: -1, cooldownMs: 2000, maxRange: 30f);

        Assert.Throws<FTableFormatException>(
            () => SpellTableMapper.MapSpells(FTableDocument.Load(data), Columns));
    }

    [Fact]
    public void MapSpells_RejectsDuplicateSpellId()
    {
        var pool = new byte[] { (byte)'b', 0 };
        var data = BuildTable(rows: 2, pool);
        WriteSpell(data, rows: 2, row: 0, id: 1, nameOffset: 101, nameLength: 2,
            castTimeMs: 1000, cooldownMs: 2000, maxRange: 30f);
        WriteSpell(data, rows: 2, row: 1, id: 1, nameOffset: 101, nameLength: 2,
            castTimeMs: 500, cooldownMs: 1000, maxRange: 20f);

        var error = Assert.Throws<FTableFormatException>(
            () => SpellTableMapper.MapSpells(FTableDocument.Load(data), Columns));

        Assert.Contains("Duplicate spell id 1", error.Message);
    }

    private static void WriteSpell(
        byte[] data, int rows, int row, uint id, int nameOffset, short nameLength,
        int castTimeMs, int cooldownMs, float maxRange)
    {
        int at = 44 + rows * 4 + "ksiif".Length + row * 22;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), (uint)nameOffset);
        BitConverter.GetBytes(nameLength).CopyTo(data, at + 8);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at + 10, 4), castTimeMs);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at + 14, 4), cooldownMs);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 18, 4), maxRange);
    }

    private static byte[] BuildTable(int rows, byte[]? pool = null)
    {
        const string format = "ksiif";
        const int entrySize = 22;
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
