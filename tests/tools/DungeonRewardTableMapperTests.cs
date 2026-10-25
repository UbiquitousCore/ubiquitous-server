using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

// The 2.2.0 loot replacement: dungeon completion rewards. Row-anchored on
// the real table (dungeon 14 normal mode grants item 9545 plus 10000 and
// 7400 of the two unsettled amounts).
public class DungeonRewardTableMapperTests
{
    private static readonly string Format220 =
        TableSchemas.FormatFor("tables/dungeon_reward.tbl", "2.2.0")!;

    [Fact]
    public void Resolve_ReturnsBuild220_OnThe220Shape()
    {
        var document = Table(
        [
            new Dictionary<int, object>
            {
                [0] = 0u, [1] = 14u, [2] = 0u, [3] = 9545u, [4] = 10000u, [5] = 7400u,
            },
        ]);
        Assert.Same(DungeonRewardColumns.Build220, DungeonRewardColumns.Resolve(document));
    }

    [Fact]
    public void MapsARewardRow()
    {
        var document = Table(
        [
            new Dictionary<int, object>
            {
                [0] = 0u, [1] = 14u, [2] = 0u, [3] = 9545u, [4] = 10000u, [5] = 7400u,
            },
            new Dictionary<int, object>
            {
                [0] = 1u, [1] = 14u, [2] = 1u, [3] = 9551u, [4] = 10000u, [5] = 99200u,
            },
        ]);
        var rewards = DungeonRewardTableMapper.MapRewards(
            document, DungeonRewardColumns.Resolve(document),
            mapIds: new HashSet<ushort> { 14 }, itemIds: new HashSet<uint> { 9545u, 9551u });
        Assert.Equal(
            [
                new DungeonRewardDefinition(0u, 14, 0u, 9545u, 10000u, 7400u),
                new DungeonRewardDefinition(1u, 14, 1u, 9551u, 10000u, 99200u),
            ],
            rewards);
    }

    [Fact]
    public void ThrowsLoudly_WhenTheDungeonIsUnknown()
    {
        var document = Table(
        [
            new Dictionary<int, object>
            {
                [0] = 0u, [1] = 471u, [2] = 0u, [3] = 9545u, [4] = 10000u, [5] = 7400u,
            },
        ]);
        var ex = Assert.Throws<FTableFormatException>(() =>
            DungeonRewardTableMapper.MapRewards(
                document, DungeonRewardColumns.Resolve(document),
                mapIds: new HashSet<ushort> { 14 }, itemIds: new HashSet<uint> { 9545u }));
        Assert.Contains("471", ex.Message);
    }

    [Fact]
    public void ThrowsLoudly_WhenTheItemIsUnknown()
    {
        var document = Table(
        [
            new Dictionary<int, object>
            {
                [0] = 0u, [1] = 14u, [2] = 0u, [3] = 424242u, [4] = 10000u, [5] = 7400u,
            },
        ]);
        var ex = Assert.Throws<FTableFormatException>(() =>
            DungeonRewardTableMapper.MapRewards(
                document, DungeonRewardColumns.Resolve(document),
                mapIds: new HashSet<ushort> { 14 }, itemIds: new HashSet<uint> { 9545u }));
        Assert.Contains("424242", ex.Message);
    }

    private static FTableDocument Table(IReadOnlyList<Dictionary<int, object>> rows)
    {
        string format = Format220;
        int cols = format.Length;
        var widths = format.Select(c => c switch { 's' => 6, 'h' => 2, 'c' => 1, _ => 4 }).ToArray();
        int stride = widths.Sum();
        const int header = 44;
        int formatAt = header + rows.Count * 4;
        int recordsAt = formatAt + cols;
        var data = new byte[recordsAt + stride * rows.Count];
        void Ascii(int at, string text)
        {
            for (int i = 0; i < text.Length; i++) data[at + i] = (byte)text[i];
        }

        void U32(int at, int value) =>
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), (uint)value);
        Ascii(0, "FTABLE");
        U32(12, rows.Count); U32(16, cols); U32(20, header); U32(24, formatAt);
        U32(28, recordsAt); U32(32, stride); U32(36, recordsAt + stride * rows.Count); U32(40, 0);
        Ascii(formatAt, format);
        int at = recordsAt;
        foreach (var row in rows)
        {
            for (int c = 0; c < cols; c++)
            {
                if (format[c] == 's')
                {
                    U32(at, 0);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(at + 4, 2), 0);
                }
                else
                {
                    uint value = row.TryGetValue(c, out var v) ? Convert.ToUInt32(v) : 0u;
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), value);
                }

                at += widths[c];
            }
        }

        return FTableDocument.Load(data);
    }
}
