using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class ZoneColumnsTests
{
    private const string Build114Format = "iiiuffffffffuuffuuuussssssfffffusiii";

    [Fact]
    public void Build114_MapsARowShapedLikeTheTable()
    {
        var data = BuildRow(
            ints: new Dictionary<int, int> { [0] = 71, [2] = -1 },
            uints: new Dictionary<int, uint> { [3] = 1 },
            floats: new Dictionary<int, float> { [4] = -2944f, [6] = -1152f, [8] = -1152f, [10] = 768f });

        var zone = Assert.Single(ZoneTableMapper.MapZones(
            FTableDocument.Load(data), ZoneColumns.Build114,
            mapIds: new HashSet<ushort> { 1 }, resolveName: id => id == 71 ? "test-coast" : "?"));

        Assert.Equal(71u, zone.Id);
        Assert.Equal((ushort)1, zone.MapId);
        Assert.Equal(0u, zone.ParentZoneId);
        Assert.Equal("test-coast", zone.NameKey);
        Assert.Equal(-2944f, zone.MinX);
        Assert.Equal(-1152f, zone.MinZ);
        Assert.Equal(-1152f, zone.MaxX);
        Assert.Equal(768f, zone.MaxZ);
    }

    private static byte[] BuildRow(
        IReadOnlyDictionary<int, int> ints, IReadOnlyDictionary<int, uint> uints, IReadOnlyDictionary<int, float> floats)
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
                    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at, 4), ints.GetValueOrDefault(i));
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
