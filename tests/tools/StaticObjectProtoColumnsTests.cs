using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

public class StaticObjectProtoColumnsTests
{
    private const string Build114Format = "uuuuuuufuuuuuuuuuuuu";

    [Fact]
    public void Build114_MapsAStorageBoxEntry()
    {
        var data = BuildRow(new Dictionary<int, uint> { [0] = 145, [1] = 9 });

        var type = Assert.Single(StaticObjectProtoTableMapper.MapTypes(FTableDocument.Load(data), StaticObjectProtoColumns.Build114));

        Assert.Equal(145u, type.Entry);
        Assert.Equal(StaticObjectTypes.StorageBox, type.Type);
    }

    [Fact]
    public void Build114_ReadsTheFestivalAPropBelongsTo()
    {
        var winter = BuildRow(new Dictionary<int, uint> { [0] = 223, [19] = FestivalIds.WinterSolstice });
        var anniversary = BuildRow(new Dictionary<int, uint> { [0] = 288, [19] = FestivalIds.Anniversary });

        var first = Assert.Single(StaticObjectProtoTableMapper.MapTypes(
            FTableDocument.Load(winter), StaticObjectProtoColumns.Build114));
        var second = Assert.Single(StaticObjectProtoTableMapper.MapTypes(
            FTableDocument.Load(anniversary), StaticObjectProtoColumns.Build114));

        Assert.Equal(FestivalIds.WinterSolstice, first.Festival);
        Assert.Equal(FestivalIds.Anniversary, second.Festival);
        Assert.True(FestivalIds.IsSeasonal(first.Festival));
        Assert.True(FestivalIds.IsSeasonal(second.Festival));
    }

    private static byte[] BuildRow(IReadOnlyDictionary<int, uint> values)
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
