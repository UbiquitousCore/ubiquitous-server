using System.Buffers.Binary;
using System.Text;
using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

public class TeleportStopColumnsTests
{
    private const string Build114Format = "usuffffu";

    [Fact]
    public void Build114_MapsARowShapedLikeTheTable()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 1u, [2] = 1u, [7] = 0u },
            floats: new Dictionary<int, float> { [3] = -507f, [4] = 5.8f, [5] = 147.4f, [6] = 1.57f },
            strings: new Dictionary<int, string> { [1] = "test-stop" });

        var stop = Assert.Single(TeleportStopTableMapper.MapTeleportStops(
            FTableDocument.Load(data), TeleportStopColumns.Build114, mapIds: new HashSet<ushort> { 1 }));

        Assert.Equal(1u, stop.Id);
        Assert.Equal((ushort)1, stop.MapId);
        Assert.Equal(-507f, stop.X);
        Assert.Equal(5.8f, stop.Y);
        Assert.Equal(147.4f, stop.Z);
        Assert.Equal(1.57f, stop.Yaw);
        Assert.Equal(TeleportStopKind.BusStop, stop.Kind);
    }

    [Fact]
    public void Build114_MapsTheDungeonGateType()
    {
        var data = BuildRow(
            uints: new Dictionary<int, uint> { [0] = 3u, [2] = 9u, [7] = 1u },
            floats: new Dictionary<int, float> { [3] = 1127f, [4] = 0f, [5] = 514f, [6] = 0f },
            strings: new Dictionary<int, string> { [1] = "sand box" });

        var stop = Assert.Single(TeleportStopTableMapper.MapTeleportStops(
            FTableDocument.Load(data), TeleportStopColumns.Build114, mapIds: new HashSet<ushort> { 9 }));

        Assert.Equal(TeleportStopKind.DungeonGate, stop.Kind);
    }

    private static byte[] BuildRow(
        IReadOnlyDictionary<int, uint> uints, IReadOnlyDictionary<int, float> floats, IReadOnlyDictionary<int, string> strings)
    {
        int cols = Build114Format.Length;
        var widths = new int[cols];
        for (int i = 0; i < cols; i++)
        {
            widths[i] = Build114Format[i] == 's' ? 6 : 4;
        }

        int entrySize = widths.Sum();
        const int headerSize = 44;
        const int rows = 1;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + cols;
        int offsetStrings = offsetEntry + entrySize;

        var pool = new List<byte>();
        var stringOffsets = new Dictionary<int, (int offset, int length)>();
        for (int c = 0; c < cols; c++)
        {
            if (Build114Format[c] == 's' && strings.TryGetValue(c, out var s))
            {
                var bytes = Encoding.UTF8.GetBytes(s + "\0");
                stringOffsets[c] = (offsetStrings + pool.Count, bytes.Length);
                pool.AddRange(bytes);
            }
        }

        var data = new byte[offsetStrings + pool.Count];
        pool.CopyTo(data, offsetStrings);
        WriteAscii(data, 0, "FTABLE");
        WriteU32(data, 8, 0x100000);
        WriteU32(data, 12, rows);
        WriteU32(data, 16, cols);
        WriteU32(data, 20, offsetIndex);
        WriteU32(data, 24, offsetFormat);
        WriteU32(data, 28, offsetEntry);
        WriteU32(data, 32, entrySize);
        WriteU32(data, 36, offsetStrings);
        WriteU32(data, 40, pool.Count);
        WriteAscii(data, offsetFormat, Build114Format);

        int at = offsetEntry;
        for (int i = 0; i < cols; i++)
        {
            if (Build114Format[i] == 's')
            {
                var (offset, length) = stringOffsets.TryGetValue(i, out var v) ? v : (0, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), (uint)offset);
                BitConverter.GetBytes((short)length).CopyTo(data, at + 4);
            }
            else if (Build114Format[i] == 'f')
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
