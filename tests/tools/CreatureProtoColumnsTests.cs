using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

public class CreatureProtoColumnsTests
{
    private const string TableFormat =
        "uuuuuuuuuufuuuuuuuuuuuuuuuuuuuuuuuffsuuuuuuuuuuuuuuuuuusuuuuufuuuuufuuuuuu";

    [Fact]
    public void Build114_MapsARowShapedLikeTheTable()
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 42,
            [4] = 12,
            [8] = 500,
            [12] = 2000,
            [14] = 10,
            [15] = 20,
            [19] = 60,
            [20] = 120,
            [53] = 5,
            [54] = 15,
        });

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(
            FTableDocument.Load(data), CreatureProtoColumns.Build114, resolveName: _ => "Test Creature"));

        Assert.Equal(42u, creature.EntryId);
        Assert.Equal("Test Creature", creature.NameKey);
        Assert.Equal(12, creature.Level);
        Assert.Equal(500, creature.MaxHealth);
        Assert.Equal(10, creature.MinDamage);
        Assert.Equal(20, creature.MaxDamage);
        Assert.Equal(60, creature.RespawnTimeMinSeconds);
        Assert.Equal(120, creature.RespawnTimeMaxSeconds);
        Assert.Equal(5, creature.MoneyDropMin);
        Assert.Equal(15, creature.MoneyDropMax);
        Assert.Equal(2000, creature.MeleeAttackIntervalMs);
    }

    [Fact]
    public void Build114_MapsSightRangeAndLeashRange()
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 42,
            [4] = 12,
            [8] = 500,
            [68] = 20,
            [67] = BitConverter.SingleToUInt32Bits(25.5f),
        });

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(
            FTableDocument.Load(data), CreatureProtoColumns.Build114, resolveName: _ => "Test Creature"));

        Assert.Equal(20, creature.NoticeDistance);
        Assert.Equal(25.5f, creature.LeashRange);
    }

    [Fact]
    public void Build114_MapsNpcFlags()
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 42,
            [4] = 12,
            [8] = 500,
            [11] = 0xD,
        });

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(
            FTableDocument.Load(data), CreatureProtoColumns.Build114, resolveName: _ => "Test Creature"));

        Assert.Equal(0xDu, creature.RawNpcFlags);
    }

    [Fact]
    public void Build114_MapsExtraFlags()
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 42,
            [4] = 12,
            [8] = 500,
            [44] = 128,
        });

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(
            FTableDocument.Load(data), CreatureProtoColumns.Build114, resolveName: _ => "Test Creature"));

        Assert.Equal(128u, creature.RawExtraFlags);
    }

    [Theory]
    [InlineData(0u, CreatureRank.Ordinary)]
    [InlineData(1u, CreatureRank.Rare)]
    [InlineData(2u, CreatureRank.Elite)]
    [InlineData(3u, CreatureRank.Ordinary)]
    [InlineData(4u, CreatureRank.Boss)]
    [InlineData(5u, CreatureRank.WorldBoss)]
    [InlineData(99u, CreatureRank.Ordinary)]
    public void Build114_MapsRank(uint column, CreatureRank rank)
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 42,
            [3] = column,
            [4] = 12,
            [8] = 500,
        });

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(
            FTableDocument.Load(data), CreatureProtoColumns.Build114, resolveName: _ => "Test Creature"));

        Assert.Equal(rank, creature.Rank);
    }

    [Fact]
    public void Build114_MapsTheRangedSwingSpeed()
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 42,
            [4] = 12,
            [8] = 500,
            [16] = 2000,
        });

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(
            FTableDocument.Load(data), CreatureProtoColumns.Build114, resolveName: _ => "Test Creature"));

        Assert.Equal(2000, creature.RangedAttackIntervalMs);
        Assert.True(creature.FightsAtRange);
    }

    [Theory]
    [InlineData(FestivalIds.None, false)]
    [InlineData(FestivalIds.WinterSolstice, true)]
    [InlineData(FestivalIds.Anniversary, true)]
    [InlineData(FestivalIds.Expansion1, false)]
    public void Build114_MapsFestival(uint festival, bool seasonal)
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 3014,
            [4] = 1,
            [8] = 100,
            [64] = festival,
        });

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(
            FTableDocument.Load(data), CreatureProtoColumns.Build114, resolveName: _ => "Test Creature"));

        Assert.Equal(festival, creature.FestivalId);
        Assert.Equal(seasonal, FestivalIds.IsSeasonal(creature.FestivalId));
    }

    private static byte[] BuildRow(IReadOnlyDictionary<int, uint> values)
    {
        int cols = TableFormat.Length;
        var widths = new int[cols];
        for (int i = 0; i < cols; i++)
        {
            widths[i] = TableFormat[i] switch
            {
                's' => 6,
                'h' => 2,
                'c' => 1,
                _ => 4,
            };
        }

        int entrySize = widths.Sum();
        const int headerSize = 44;
        const int rows = 1;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + cols;
        int offsetStrings = offsetEntry + entrySize;
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
        WriteAscii(data, offsetFormat, TableFormat);

        int at = offsetEntry;
        for (int i = 0; i < cols; i++)
        {
            if (TableFormat[i] == 's')
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), 0);
                BitConverter.GetBytes((short)0).CopyTo(data, at + 4);
            }
            else
            {
                uint value = values.TryGetValue(i, out var v) ? v : 0;
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), value);
            }

            at += widths[i];
        }

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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Build114_MapsFaction(uint faction)
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 42,
            [4] = 12,
            [8] = 500,
            [6] = faction,
        });

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(
            FTableDocument.Load(data), CreatureProtoColumns.Build114, resolveName: _ => "Test Creature"));

        Assert.Equal((int)faction, creature.FactionType);
    }

    [Fact]
    public void Build114_MapsFamily()
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 42,
            [1] = CreatureFamilies.Critter,
            [4] = 1,
            [8] = 1,
        });

        var creature = Assert.Single(CreatureTableMapper.MapCreatures(
            FTableDocument.Load(data), CreatureProtoColumns.Build114, resolveName: _ => "Test Creature"));

        Assert.Equal(CreatureFamilies.Critter, creature.Family);
    }
}
