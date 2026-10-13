using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class TravelRouteColumnsTests
{
    private const string Build114Format = "uuuuu";

    [Fact]
    public void Build114_MapsARowShapedLikeTheTable()
    {
        var data = BuildRow(new Dictionary<int, uint> { [0] = 1u, [1] = 1u, [2] = 7u, [3] = 45u, [4] = 7559u });

        var route = Assert.Single(TravelRouteTableMapper.MapTravelRoutes(
            FTableDocument.Load(data), TravelRouteColumns.Build114, stopIds: new HashSet<uint> { 1u, 7u }));

        Assert.Equal(1u, route.Id);
        Assert.Equal(1u, route.FirstStop);
        Assert.Equal(7u, route.SecondStop);
        Assert.Equal(45, route.Price);
    }

    private static byte[] BuildRow(IReadOnlyDictionary<int, uint> uints)
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
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), uints.GetValueOrDefault(i));
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
