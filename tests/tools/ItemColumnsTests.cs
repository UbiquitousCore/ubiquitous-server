using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class ItemColumnsTests
{
    private const string TableFormat =
        "uuukuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuffuffuuuufuuuuuuuuuuuuukuuuuuuuufuuuuuuuuuuuuuuuusuuuusuuuuuuuuuu";

    [Fact]
    public void Build114_MapsARowShapedLikeTheTable()
    {
        var data = BuildRow(new Dictionary<int, uint> { [0] = 7, [1] = 4, [2] = 5, [3] = 900, [4] = 7 });

        var item = Assert.Single(ItemTableMapper.MapItems(
            FTableDocument.Load(data), ItemColumns.Build114, resolveName: _ => "Test Shield"));

        Assert.Equal(7u, item.Id);
        Assert.Equal("Test Shield", item.NameKey);
        Assert.Equal((byte)4, item.Class);
        Assert.Equal((byte)5, item.SubClass);
        Assert.Equal(7u, item.DisplayInfoId);
    }

    [Fact]
    public void Build114_MapsAQuestStarterItemsQuestId()
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 300,
            [1] = 10,
            [2] = 0,
            [3] = 950,
            [4] = 300,
            [75] = 5001,
        });

        var item = Assert.Single(ItemTableMapper.MapItems(
            FTableDocument.Load(data), ItemColumns.Build114, resolveName: _ => "Test Scroll"));

        Assert.Equal(5001u, item.QuestId);
        Assert.Null(item.OnUseSpellId);
    }

    [Fact]
    public void Build114_MapsAnOnUseSpellShapedLikeAHealingPotion()
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 1168,
            [1] = 0,
            [2] = 1,
            [3] = 950,
            [4] = 1168,
            [58] = 20,
            [59] = 1,
            [61] = 60,
        });

        var item = Assert.Single(ItemTableMapper.MapItems(
            FTableDocument.Load(data), ItemColumns.Build114, resolveName: _ => "test-potion"));

        Assert.Equal(20u, item.OnUseSpellId);
        Assert.Equal(60u, item.OnUseCooldownSeconds);
    }

    [Fact]
    public void Build114_ReadsTheSpellABondNeedsItsUserToKnow()
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 7485,
            [1] = 14,
            [2] = 0,
            [3] = 5788,
            [4] = 0,
            [19] = 6952,
            [58] = 6979,
            [59] = 1,
        });

        var item = Assert.Single(ItemTableMapper.MapItems(
            FTableDocument.Load(data), ItemColumns.Build114, resolveName: _ => "test-bond"));

        Assert.Equal(6952u, item.RequiredSpellId);
        Assert.Equal(6979u, item.OnUseSpellId);
    }

    [Fact]
    public void Build114_IgnoresASpellSlotWhoseTriggerIsNotOnUse()
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 42,
            [3] = 950,
            [58] = 999,
            [59] = 2,
            [61] = 0,
        });

        var item = Assert.Single(ItemTableMapper.MapItems(
            FTableDocument.Load(data), ItemColumns.Build114, resolveName: _ => "Trinket"));

        Assert.Null(item.OnUseSpellId);
        Assert.Null(item.OnUseCooldownSeconds);
    }

    [Fact]
    public void Build114_ReadsEveryStatPairTheRowHasRoomFor()
    {
        var values = new Dictionary<int, uint> { [0] = 400, [27] = 10 };
        for (int pair = 0; pair < 10; pair++)
        {
            values[28 + (pair * 2)] = 13;
            values[29 + (pair * 2)] = (uint)(pair + 1);
        }

        var item = Assert.Single(ItemTableMapper.MapItems(
            FTableDocument.Load(BuildRow(values)), ItemColumns.Build114, resolveName: _ => "Test Charm"));

        Assert.Equal(Enumerable.Range(1, 10), item.Stats!.Select(stat => stat.Amount));
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
            else if (TableFormat[i] == 'f')
            {
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at, 4), 0f);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), values.GetValueOrDefault(i));
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
