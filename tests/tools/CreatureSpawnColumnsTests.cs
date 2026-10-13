using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class CreatureSpawnColumnsTests
{
    private const string TableFormat = "uuuffffcuuuuuuuuhuuhuuuuuuufuuuuuuuuuuuuuu";

    [Fact]
    public void Build114_MapsARowShapedLikeTheTable()
    {
        var data = BuildRow(uints: new Dictionary<int, uint> { [0] = 20168, [1] = 9001, [2] = 13 },
            floats: new Dictionary<int, float> { [3] = 100f, [4] = 2f, [5] = 3f, [6] = 1.5f });

        var spawns = CreatureSpawnTableMapper.MapSpawns(
            FTableDocument.Load(data), CreatureSpawnColumns.Build114,
            mapIds: new HashSet<ushort> { 13 }, creatureEntries: new HashSet<uint> { 9001 });

        var spawn = Assert.Single(spawns);
        Assert.Equal(20168u, spawn.Id);
        Assert.Equal((ushort)13, spawn.MapId);
        Assert.Equal(9001u, spawn.CreatureEntryId);
        Assert.Equal(100f, spawn.X);
        Assert.Equal(2f, spawn.Y);
        Assert.Equal(3f, spawn.Z);
        Assert.Equal(1.5f, spawn.Yaw);
    }

    [Fact]
    public void Build114_ReadsTheLeaderColumnAtIndex34AndNotItsNeighbours()
    {
        var follower = new Dictionary<int, uint>
        {
            [0] = 18099,
            [1] = 2458,
            [2] = 13,
            [32] = 18097,
            [33] = 18097,
            [34] = 18097,
            [35] = 18097,
        };
        var leader = new Dictionary<int, uint> { [0] = 18097, [1] = 2456, [2] = 13 };
        var noFloats = new Dictionary<int, float>();
        var data = BuildRows((follower, noFloats), (leader, noFloats));

        var spawns = CreatureSpawnTableMapper.MapSpawns(
            FTableDocument.Load(data), CreatureSpawnColumns.Build114,
            mapIds: new HashSet<ushort> { 13 }, creatureEntries: new HashSet<uint> { 2458, 2456 });

        Assert.Equal(18097u, spawns[0].LeaderSpawnId);

        follower[34] = 0;
        var cleared = CreatureSpawnTableMapper.MapSpawns(
            FTableDocument.Load(BuildRows((follower, noFloats), (leader, noFloats))),
            CreatureSpawnColumns.Build114,
            mapIds: new HashSet<ushort> { 13 }, creatureEntries: new HashSet<uint> { 2458, 2456 });

        Assert.Equal(0u, cleared[0].LeaderSpawnId);
    }

    [Fact]
    public void Build114_ReadsTheStandingAnimationFromColumn14()
    {
        var data = BuildRow(uints: new Dictionary<int, uint>
        {
            [0] = 20168,
            [1] = 9001,
            [2] = 13,
            [13] = 7,
            [14] = 4321,
            [15] = 9,
        },
            floats: new Dictionary<int, float>());

        var spawn = Assert.Single(CreatureSpawnTableMapper.MapSpawns(
            FTableDocument.Load(data), CreatureSpawnColumns.Build114,
            mapIds: new HashSet<ushort> { 13 }, creatureEntries: new HashSet<uint> { 9001 }));

        Assert.Equal(4321u, spawn.AnimationId);
    }

    private static byte[] BuildRow(IReadOnlyDictionary<int, uint> uints, IReadOnlyDictionary<int, float> floats) =>
        BuildRows((uints, floats));

    private static byte[] BuildRows(
        params (IReadOnlyDictionary<int, uint> Uints, IReadOnlyDictionary<int, float> Floats)[] rowValues)
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
        int rows = rowValues.Length;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + cols;
        int offsetStrings = offsetEntry + rows * entrySize;
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

        for (int row = 0; row < rows; row++)
        {
            var (uints, floats) = rowValues[row];
            WriteU32(data, offsetIndex + row * 4, (int)uints.GetValueOrDefault(0));
            int at = offsetEntry + row * entrySize;
            for (int i = 0; i < cols; i++)
            {
                switch (TableFormat[i])
                {
                    case 's':
                        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), 0);
                        BitConverter.GetBytes((short)0).CopyTo(data, at + 4);
                        break;
                    case 'f':
                        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at, 4), floats.GetValueOrDefault(i));
                        break;
                    default:
                        uint value = uints.TryGetValue(i, out var v) ? v : 0;
                        for (int b = 0; b < widths[i]; b++)
                        {
                            data[at + b] = (byte)(value >> (8 * b));
                        }

                        break;
                }

                at += widths[i];
            }
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
