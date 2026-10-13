using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class CreatureTableMapperTests
{
    private static readonly CreatureTableColumns Columns = new(Entry: 0, Name: 1, Level: 2, Health: 3);

    [Fact]
    public void MapCreatures_ConvertsRowsToCanonicalTemplates()
    {
        var pool = new byte[] { (byte)'w', (byte)'o', (byte)'l', (byte)'f', 0, (byte)'o', (byte)'r', (byte)'c', 0 };
        var data = BuildTable("kshh", rows: 2, entrySize: 14, pool);
        WriteRow(data, rows: 2, row: 0, entry: 12, nameOffset: 84, nameLength: 5, level: 3, health: 80);
        WriteRow(data, rows: 2, row: 1, entry: 13, nameOffset: 89, nameLength: 4, level: 4, health: 90);

        var creatures = CreatureTableMapper.MapCreatures(FTableDocument.Load(data), Columns);

        Assert.Equal(2, creatures.Count);
        Assert.Equal(new Ubiquitous.Data.CreatureDefinition(12, "wolf", 3, 80), creatures[0]);
        Assert.Equal(new Ubiquitous.Data.CreatureDefinition(13, "orc", 4, 90), creatures[1]);
    }

    [Fact]
    public void MapCreatures_FirstDuplicateEntryWins()
    {
        var pool = new byte[] { (byte)'w', 0, (byte)'o', 0 };
        var data = BuildTable("kshh", rows: 2, entrySize: 14, pool);
        WriteRow(data, rows: 2, row: 0, entry: 12, nameOffset: 84, nameLength: 2, level: 3, health: 80);
        WriteRow(data, rows: 2, row: 1, entry: 12, nameOffset: 86, nameLength: 2, level: 9, health: 99);

        var creatures = CreatureTableMapper.MapCreatures(FTableDocument.Load(data), Columns);

        Assert.Single(creatures);
        Assert.Equal(3, creatures[0].Level);
    }

    [Fact]
    public void MapCreatures_ReadsOptionalCombatColumnsWhenTheyAreMapped()
    {
        var columns = new CreatureTableColumns(Entry: 0, Name: 1, Level: 2, Health: 3,
            MinDamage: 4, MaxDamage: 5);
        var pool = new byte[] { (byte)'w', (byte)'o', (byte)'l', (byte)'f' };
        var data = BuildTable("kshhhh", rows: 1, entrySize: 18, pool);
        WriteRowWithDamage(data, rows: 1, row: 0, entry: 12, nameOffset: 72, nameLength: 4,
            level: 3, health: 80, minDamage: 8, maxDamage: 16);

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(FTableDocument.Load(data), columns));

        Assert.Equal(8, creature.MinDamage);
        Assert.Equal(16, creature.MaxDamage);
        Assert.Null(creature.RespawnTimeMinSeconds);
        Assert.Null(creature.MoneyDropMin);
    }

    [Fact]
    public void MapCreatures_LeavesUnmappedCombatColumnsNull()
    {
        var data = BuildTable("kshh", rows: 1, entrySize: 14, [(byte)'w']);
        WriteRow(data, rows: 1, row: 0, entry: 12, nameOffset: 66, nameLength: 1, level: 3, health: 80);

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(FTableDocument.Load(data), Columns));

        Assert.Null(creature.MinDamage);
        Assert.Null(creature.MaxDamage);
        Assert.Null(creature.RespawnTimeMinSeconds);
        Assert.Null(creature.RespawnTimeMaxSeconds);
        Assert.Null(creature.MoneyDropMin);
        Assert.Null(creature.MoneyDropMax);
        Assert.Null(creature.MeleeAttackIntervalMs);
    }

    [Fact]
    public void MapCreatures_ResolvesNameThroughADelegateWhenTheTableHasNoNameColumn()
    {
        var columns = new CreatureTableColumns(Entry: 0, Name: -1, Level: 1, Health: 2);
        var data = BuildTable("uhh", rows: 1, entrySize: 8);
        WriteRowNoName(data, rows: 1, row: 0, entry: 12, level: 3, health: 80);

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(
            FTableDocument.Load(data), columns, resolveName: entry => entry == 12 ? "wolf" : "?"));

        Assert.Equal("wolf", creature.NameKey);
    }

    [Fact]
    public void MapCreatures_ThrowsWhenTheTableHasNoNameColumnAndNoResolverIsGiven()
    {
        var columns = new CreatureTableColumns(Entry: 0, Name: -1, Level: 1, Health: 2);
        var data = BuildTable("uhh", rows: 1, entrySize: 8);
        WriteRowNoName(data, rows: 1, row: 0, entry: 12, level: 3, health: 80);

        Assert.Throws<FTableFormatException>(
            () => CreatureTableMapper.MapCreatures(FTableDocument.Load(data), columns));
    }

    [Fact]
    public void MapCreatures_RejectsNegativeLevel()
    {
        var pool = new byte[] { (byte)'w', 0 };
        var data = BuildTable("kshh", rows: 1, entrySize: 14, pool);
        WriteRow(data, rows: 1, row: 0, entry: 12, nameOffset: 66, nameLength: 2, level: -1, health: 80);

        Assert.Throws<FTableFormatException>(
            () => CreatureTableMapper.MapCreatures(FTableDocument.Load(data), Columns));
    }

    [Fact]
    public void MapCreatures_DropsABackwardsRangeToNullRatherThanKeepingIt()
    {
        var columns = new CreatureTableColumns(Entry: 0, Name: 1, Level: 2, Health: 3,
            RespawnMinSeconds: 4, RespawnMaxSeconds: 5);
        var pool = new byte[] { (byte)'w', (byte)'o', (byte)'l', (byte)'f' };
        var data = BuildTable("kshhhh", rows: 1, entrySize: 18, pool);
        WriteRowWithDamage(data, rows: 1, row: 0, entry: 12, nameOffset: 72, nameLength: 4,
            level: 3, health: 80, minDamage: 300, maxDamage: 180);

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(FTableDocument.Load(data), columns));

        Assert.Null(creature.RespawnTimeMinSeconds);
        Assert.Null(creature.RespawnTimeMaxSeconds);
    }

    [Fact]
    public void MapCreatures_KeepsAForwardRangeUnchanged()
    {
        var columns = new CreatureTableColumns(Entry: 0, Name: 1, Level: 2, Health: 3,
            RespawnMinSeconds: 4, RespawnMaxSeconds: 5);
        var pool = new byte[] { (byte)'w', (byte)'o', (byte)'l', (byte)'f' };
        var data = BuildTable("kshhhh", rows: 1, entrySize: 18, pool);
        WriteRowWithDamage(data, rows: 1, row: 0, entry: 12, nameOffset: 72, nameLength: 4,
            level: 3, health: 80, minDamage: 180, maxDamage: 300);

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(FTableDocument.Load(data), columns));

        Assert.Equal(180, creature.RespawnTimeMinSeconds);
        Assert.Equal(300, creature.RespawnTimeMaxSeconds);
    }

    [Fact]
    public void MapCreatures_RejectsWrongCellType()
    {
        var pool = new byte[] { (byte)'w', 0 };
        var data = BuildTable("fshh", rows: 1, entrySize: 14, pool);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(52, 4), 1.5f);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(56, 4), 66u);
        BitConverter.GetBytes((short)2).CopyTo(data, 60);

        Assert.Throws<FTableFormatException>(
            () => CreatureTableMapper.MapCreatures(FTableDocument.Load(data), Columns));
    }

    private static void WriteRow(
        byte[] data, int rows, int row, uint entry, int nameOffset, short nameLength, short level, short health)
    {
        int at = 44 + rows * 4 + "kshh".Length + row * 14;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), entry);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), (uint)nameOffset);
        BitConverter.GetBytes(nameLength).CopyTo(data, at + 8);
        BitConverter.GetBytes(level).CopyTo(data, at + 10);
        BitConverter.GetBytes(health).CopyTo(data, at + 12);
    }

    private static void WriteRowNoName(byte[] data, int rows, int row, uint entry, short level, short health)
    {
        int at = 44 + rows * 4 + "uhh".Length + row * 8;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), entry);
        BitConverter.GetBytes(level).CopyTo(data, at + 4);
        BitConverter.GetBytes(health).CopyTo(data, at + 6);
    }

    private static void WriteRowWithDamage(
        byte[] data, int rows, int row, uint entry, int nameOffset, short nameLength, short level, short health,
        short minDamage, short maxDamage)
    {
        int at = 44 + rows * 4 + "kshhhh".Length + row * 18;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), entry);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), (uint)nameOffset);
        BitConverter.GetBytes(nameLength).CopyTo(data, at + 8);
        BitConverter.GetBytes(level).CopyTo(data, at + 10);
        BitConverter.GetBytes(health).CopyTo(data, at + 12);
        BitConverter.GetBytes(minDamage).CopyTo(data, at + 14);
        BitConverter.GetBytes(maxDamage).CopyTo(data, at + 16);
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
