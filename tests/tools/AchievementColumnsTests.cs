using System.Buffers.Binary;
using System.Text;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class AchievementColumnsTests
{
    private static readonly string Format = "u" + new string('i', 56);

    private const uint LevelCategory = 1161;
    private const uint GuideCategory = 1555;
    private const uint GeneralCategory = 8;

    private static readonly IReadOnlyDictionary<uint, string> Strings = new Dictionary<uint, string>
    {
        [LevelCategory] = "Level",
        [GuideCategory] = "Guide",
        [GeneralCategory] = "General",
    };

    private static Dictionary<int, uint> Achievement(uint id, uint category, params (uint Type, uint Value)[] conditions)
    {
        var row = new Dictionary<int, uint> { [0] = id, [AchievementColumns.Category] = category, [6] = (uint)conditions.Length };
        for (int i = 0; i < conditions.Length; i++)
        {
            row[AchievementColumns.FirstConditionType + i] = conditions[i].Type;
            row[AchievementColumns.FirstConditionValue + i] = conditions[i].Value;
        }

        return row;
    }

    [Fact]
    public void TheHighestPlayerLevel_IsTheLargestLevelTheLevelCategoryAsksFor()
    {
        var table = FTableDocument.Load(BuildTable(
        [
            Achievement(15, LevelCategory, (30, 60)),
            Achievement(442, LevelCategory, (30, 75)),
            Achievement(152, LevelCategory, (26, 16912), (30, 70)),
            Achievement(342, GuideCategory, (30, 99)),
            Achievement(9, LevelCategory, (6, 120)),
        ]));

        Assert.Equal(75, AchievementColumns.HighestPlayerLevelAskedFor(table, Strings));
    }

    [Fact]
    public void ABuildWithoutALevelCategory_ReadsItsGeneralOne()
    {
        var table = FTableDocument.Load(BuildTable(
        [
            Achievement(15, GeneralCategory, (30, 60)),
            Achievement(16, GeneralCategory, (30, 70)),
            Achievement(342, GuideCategory, (30, 99)),
        ]));

        Assert.Equal(70, AchievementColumns.HighestPlayerLevelAskedFor(table, Strings));
    }

    [Fact]
    public void ALevelCategory_LeavesTheGeneralOneUncounted()
    {
        var table = FTableDocument.Load(BuildTable(
        [
            Achievement(442, LevelCategory, (30, 75)),
            Achievement(16, GeneralCategory, (30, 99)),
        ]));

        Assert.Equal(75, AchievementColumns.HighestPlayerLevelAskedFor(table, Strings));
    }

    [Fact]
    public void ATableWithNoLevelAchievement_SaysNothing()
    {
        var table = FTableDocument.Load(BuildTable([Achievement(9, GuideCategory, (30, 40))]));

        Assert.Null(AchievementColumns.HighestPlayerLevelAskedFor(table, Strings));
    }

    private static byte[] BuildTable(IReadOnlyList<Dictionary<int, uint>> rows)
    {
        int entrySize = Format.Length * 4;
        const int headerSize = 44;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows.Count * 4;
        int offsetEntry = offsetFormat + Format.Length;
        int offsetStrings = offsetEntry + entrySize * rows.Count;
        var data = new byte[offsetStrings];
        Encoding.ASCII.GetBytes("FTABLE").CopyTo(data, 0);
        int[] header = [0x100000, rows.Count, Format.Length, offsetIndex, offsetFormat, offsetEntry, entrySize, offsetStrings, 0];
        for (int i = 0; i < header.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8 + i * 4, 4), header[i]);
        }

        Encoding.ASCII.GetBytes(Format).CopyTo(data, offsetFormat);
        int at = offsetEntry;
        foreach (var row in rows)
        {
            for (int c = 0; c < Format.Length; c++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), row.GetValueOrDefault(c));
                at += 4;
            }
        }

        return data;
    }
}
