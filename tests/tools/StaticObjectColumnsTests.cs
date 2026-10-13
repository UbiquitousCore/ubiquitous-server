using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class StaticObjectColumnsTests
{
    private const string Build114Format = "uuuffffffuufffuf";

    [Fact]
    public void Build114_MapsARowShapedLikeTheTable()
    {
        var data = BuildRow(new Dictionary<int, float>
        {
            [3] = -453.096f,
            [4] = 40.6191f,
            [5] = 203.481f,
            [6] = 0f,
            [7] = 3.14159f,
            [8] = 0f,
        }, new Dictionary<int, uint> { [0] = 22u, [1] = 1u, [2] = 1u });

        var spawn = Assert.Single(StaticObjectTableMapper.MapStaticObjects(
            FTableDocument.Load(data), StaticObjectColumns.Build114, mapIds: new HashSet<ushort> { 1 }));

        Assert.Equal(22u, spawn.Id);
        Assert.Equal((ushort)1, spawn.MapId);
        Assert.Equal(1u, spawn.EntryId);
        Assert.Equal(-453.096f, spawn.X);
        Assert.Equal(40.6191f, spawn.Y);
        Assert.Equal(203.481f, spawn.Z);
        Assert.Equal(3.14159f, spawn.Yaw);
    }

    private static byte[] BuildRow(IReadOnlyDictionary<int, float> floats, IReadOnlyDictionary<int, uint> uints)
    {
        int cols = Build114Format.Length;
        var widths = new int[cols];
        for (int i = 0; i < cols; i++)
        {
            widths[i] = 4;
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
            if (Build114Format[i] == 'f')
            {
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at, 4), floats.GetValueOrDefault(i));
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), uints.GetValueOrDefault(i));
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
