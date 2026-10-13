using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class QuestRewardColumnsTests
{
    private const string Build114Format =
        "uuuuuuuuuuuuuuuuuuuuuuuukkkkkkkkkkkuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuiiiiiiuiuuuuuuuuuuuusuuuusuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuusuuuuuu";

    [Fact]
    public void Build114_MapsAFixedReward()
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 17,
            [87] = 300,
            [91] = 10,
            [108] = 104,
        });

        var reward = Assert.Single(QuestRewardTableMapper.MapRewards(
            FTableDocument.Load(data), QuestRewardColumns.Build114,
            questIds: new HashSet<uint> { 17 },
            resolveItem: entry => entry == 300 ? (5001u, 2) : null));

        Assert.Equal(17u, reward.QuestId);
        Assert.Equal(104, reward.Money);
        var item = Assert.Single(reward.Fixed);
        Assert.Equal(300u, item.Entry);
        Assert.Equal(10, item.Count);
        Assert.Equal(5001u, item.NameTextId);
        Assert.Equal(2, item.Quality);
        Assert.Empty(reward.Choices);
    }

    [Fact]
    public void Build114_SkipsAnEmptySlotAndAnUnresolvableItem()
    {
        var data = BuildRow(new Dictionary<int, uint> { [0] = 18, [88] = 999, [92] = 1 });

        var reward = Assert.Single(QuestRewardTableMapper.MapRewards(
            FTableDocument.Load(data), QuestRewardColumns.Build114,
            questIds: new HashSet<uint> { 18 }, resolveItem: _ => null));

        Assert.Empty(reward.Fixed);
        Assert.Empty(reward.Choices);
    }

    private static byte[] BuildRow(IReadOnlyDictionary<int, uint> values)
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
                    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at, 4), 0f);
                    break;
                case 'i':
                    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at, 4), unchecked((int)values.GetValueOrDefault(i)));
                    break;
                default:
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), values.GetValueOrDefault(i));
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
