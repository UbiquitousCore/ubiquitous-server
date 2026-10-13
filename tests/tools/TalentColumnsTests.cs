using System.Buffers.Binary;
using System.Text;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public sealed class TalentColumnsTests
{
    private const string TalentFormat = "uuuuuuuuuuuuuusu";
    private const string ClassFormat = "uuuuuuuu";

    private static readonly byte[] Classes = BuildTable(ClassFormat,
        [new Dictionary<int, uint> { [0] = 1, [3] = 114, [4] = 115, [5] = 116 }]);

    [Fact]
    public void Build114_ReadsPlaceGateRanksAndBothPrerequisiteColumns()
    {
        var talents = BuildTable(TalentFormat,
        [
            new() { [0] = 222, [1] = 1, [2] = 1, [3] = 114, [8] = 1, [9] = 5292, [15] = 9766 },
            new() { [0] = 223, [1] = 1, [2] = 2, [3] = 114, [8] = 1, [9] = 5289, [15] = 9760 },
            new() { [0] = 227, [1] = 2, [2] = 3, [3] = 114, [4] = 5, [5] = 222, [6] = 223, [8] = 3, [9] = 5297, [10] = 5298, [11] = 5299, [15] = 9776 },
        ]);

        var tree = Assert.Single(Map(talents));

        Assert.Equal(114u, tree.Id);
        Assert.Equal(1, tree.ClassId);
        var deep = tree.Talents.Single(talent => talent.Id == 227);
        Assert.Equal(2, deep.Tier);
        Assert.Equal(3, deep.Column);
        Assert.Equal(5, deep.PointsInTreeRequired);
        Assert.Equal([222u, 223u], deep.Prerequisites);
        Assert.Equal([5297u, 5298u, 5299u], deep.RankSpellIds);
        Assert.Equal(3, deep.MaxRank);
    }

    [Fact]
    public void Build114_LeavesOutATreeNoClassOwns()
    {
        var talents = BuildTable(TalentFormat,
        [
            new() { [0] = 22, [1] = 1, [2] = 1, [3] = 14, [8] = 1, [9] = 212 },
            new() { [0] = 1000, [1] = 1, [2] = 1, [3] = 102, [8] = 1, [9] = 6995 },
        ]);

        Assert.Empty(Map(talents));
    }

    [Fact]
    public void Build114_RejectsARankCountThatDisagreesWithTheRankSpells()
    {
        var talents = BuildTable(TalentFormat,
            [new() { [0] = 222, [1] = 1, [2] = 1, [3] = 114, [8] = 5, [9] = 5292, [10] = 5293 }]);

        Assert.Throws<FTableFormatException>(() => Map(talents));
    }

    [Fact]
    public void Build114_RejectsAPrerequisiteOffTheTree()
    {
        var talents = BuildTable(TalentFormat,
            [new() { [0] = 227, [1] = 2, [2] = 1, [3] = 114, [4] = 5, [5] = 26, [8] = 1, [9] = 5297 }]);

        Assert.Throws<FTableFormatException>(() => Map(talents));
    }

    private static IReadOnlyList<Ubiquitous.Data.TalentTreeDefinition> Map(byte[] talents) =>
        TalentTableMapper.MapTrees(FTableDocument.Load(talents), FTableDocument.Load(Classes),
            TalentColumns.Build114, TalentColumns.ClassBuild114);

    private static byte[] BuildTable(string format, IReadOnlyList<Dictionary<int, uint>> rows)
    {
        var widths = format.Select(column => column == 's' ? 6 : 4).ToArray();
        int entrySize = widths.Sum();
        const int headerSize = 44;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows.Count * 4;
        int offsetEntry = offsetFormat + format.Length;
        int offsetStrings = offsetEntry + entrySize * rows.Count;
        var data = new byte[offsetStrings];
        Encoding.ASCII.GetBytes("FTABLE").CopyTo(data, 0);
        int[] header = [0x100000, rows.Count, format.Length, offsetIndex, offsetFormat, offsetEntry, entrySize, offsetStrings, 0];
        for (int i = 0; i < header.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8 + i * 4, 4), header[i]);
        }

        Encoding.ASCII.GetBytes(format).CopyTo(data, offsetFormat);
        int at = offsetEntry;
        foreach (var row in rows)
        {
            for (int c = 0; c < format.Length; c++)
            {
                if (format[c] != 's') BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), row.GetValueOrDefault(c));
                at += widths[c];
            }
        }

        return data;
    }
}
