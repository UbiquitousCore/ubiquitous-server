using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class SpellNewColumnsTests
{
    private const string Build114Format =
        "uuusuuuuuusuiuuuuuffffuuuuuuuuuuuuuuuuufuuuuuuuuuuuuu";

    [Fact]
    public void Build114_MapsARowShapedLikeTheShippedTable()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 85, [16] = 1000, [17] = 2000 },
            floats: new Dictionary<int, float> { [21] = 30f });

        var spell = Assert.Single(SpellTableMapper.MapSpells(
            FTableDocument.Load(data), SpellNewColumns.Build114, resolveName: _ => "test-bolt"));

        Assert.Equal(85u, spell.Id);
        Assert.Equal("test-bolt", spell.NameKey);
        Assert.Equal(1000, spell.CastTimeMs);
        Assert.Equal(2000, spell.CooldownMs);
        Assert.Equal(30f, spell.MaxRange);
    }

    [Fact]
    public void Build114_ReadsTheDurationColumn()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 61, [31] = 2000 },
            floats: new Dictionary<int, float>());

        var spell = Assert.Single(SpellTableMapper.MapSpells(
            FTableDocument.Load(data), SpellNewColumns.Build114, resolveName: _ => "Stun Bash"));

        Assert.Equal(2000, spell.DurationMs);
    }

    [Fact]
    public void Build114_ReadsTheRankColumn()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 5006, [6] = 2 },
            floats: new Dictionary<int, float>());

        var spell = Assert.Single(SpellTableMapper.MapSpells(
            FTableDocument.Load(data), SpellNewColumns.Build114, resolveName: _ => "Fireball"));

        Assert.Equal(2, spell.Rank);
    }

    [Fact]
    public void Build114_ReadsTheProjectileSpeedColumn()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 70 },
            floats: new Dictionary<int, float> { [39] = 30f });

        var spell = Assert.Single(SpellTableMapper.MapSpells(
            FTableDocument.Load(data), SpellNewColumns.Build114, resolveName: _ => "Rapid Shot"));

        Assert.Equal(30f, spell.Speed);
    }

    [Fact]
    public void Build114_LeavesDurationAtZeroWhenTheColumnIsUnmapped()
    {
        var columns = SpellNewColumns.Build114 with { DurationMs = -1 };
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 61, [31] = 2000 },
            floats: new Dictionary<int, float>());

        var spell = Assert.Single(SpellTableMapper.MapSpells(
            FTableDocument.Load(data), columns, resolveName: _ => "Stun Bash"));

        Assert.Equal(0, spell.DurationMs);
    }

    [Fact]
    public void Build114_ReadsTheEquipmentGateColumns()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 5692, [12] = 15, [13] = 2, [14] = 196608 },
            floats: new Dictionary<int, float>());

        var spell = Assert.Single(SpellTableMapper.MapSpells(
            FTableDocument.Load(data), SpellNewColumns.Build114, resolveName: _ => "test-ranged-attack"));

        Assert.Equal(15, spell.WornSlotNeeded);
        Assert.Equal(2u, spell.RequiredItemClass);
        Assert.Equal(196608u, spell.ItemSubclassesAllowed);
    }

    [Fact]
    public void Build114_ReadsNoSlotAsNoEquipmentRequirementAtAll()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 100, [12] = unchecked((uint)-1), [13] = 2, [14] = 196608 },
            floats: new Dictionary<int, float>());

        var spell = Assert.Single(SpellTableMapper.MapSpells(
            FTableDocument.Load(data), SpellNewColumns.Build114, resolveName: _ => "test-bolt"));

        Assert.Equal(-1, spell.WornSlotNeeded);
        Assert.Equal(0u, spell.RequiredItemClass);
        Assert.Equal(0u, spell.ItemSubclassesAllowed);
    }

    private static byte[] BuildRow(IReadOnlyDictionary<int, uint> uints, IReadOnlyDictionary<int, float> floats)
    {
        int cols = Build114Format.Length;
        var widths = new int[cols];
        for (int i = 0; i < cols; i++)
        {
            widths[i] = Build114Format[i] switch
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
        WriteAscii(data, offsetFormat, Build114Format);

        int at = offsetEntry;
        for (int i = 0; i < cols; i++)
        {
            switch (Build114Format[i])
            {
                case 's':
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), 0);
                    BitConverter.GetBytes((short)0).CopyTo(data, at + 4);
                    break;
                case 'f':
                    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at, 4), floats.GetValueOrDefault(i));
                    break;
                case 'i':
                    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at, 4), (int)uints.GetValueOrDefault(i));
                    break;
                default:
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), uints.GetValueOrDefault(i));
                    break;
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
}
