using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;

namespace Ubiquitous.Integration.Tests;

public class QuestColumnsTests
{
    private const string Build114Format =
        "uuuuuuuuuuuuuuuuuuuuuuuukkkkkkkkkkkuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuiiiiiiuiuuuuuuuuuuuusuuuusuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuuusuuuuuu";

    [Fact]
    public void Build114_MapsARowShapedLikeTheTable()
    {
        var data = BuildRow(new Dictionary<int, uint> { [0] = 5010, [4] = 1, [24] = 12010, [25] = 12011 });

        var quest = Assert.Single(QuestTableMapper.MapQuests(
            FTableDocument.Load(data), QuestColumns.Build114,
            npcEntries: new HashSet<uint> { 9010 }, resolveGiverEntry: id => id == 5010 ? 9010u : null));

        Assert.Equal(5010u, quest.Id);
        Assert.Equal(9010u, quest.GiverEntryId);
        Assert.Equal(12010u, quest.TitleStringId);
        Assert.Equal(12011u, quest.DetailsStringId);
        Assert.Equal(1, quest.MinLevel);
    }

    [Fact]
    public void Build114_DropsAQuestWithNoResolvedGiver()
    {
        var data = BuildRow(new Dictionary<int, uint> { [0] = 5010, [4] = 1, [24] = 12010, [25] = 12011 });

        Assert.Throws<FTableFormatException>(() => QuestTableMapper.MapQuests(
            FTableDocument.Load(data), QuestColumns.Build114,
            npcEntries: new HashSet<uint> { 9010 }, resolveGiverEntry: _ => null));
    }

    [Fact]
    public void Build114_ReadsTheRequiredSpellColumnAsATriggeredObjective()
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 5014,
            [4] = 1,
            [24] = 12018,
            [25] = 12019,
            [31] = 12020,
            [63] = 1,
            [67] = 5692,
        });

        var quest = Assert.Single(QuestTableMapper.MapQuests(
            FTableDocument.Load(data), QuestColumns.Build114,
            npcEntries: new HashSet<uint> { 9013 }, resolveGiverEntry: id => id == 5014 ? 9013u : null));

        var objective = Assert.Single(quest.Objectives);
        Assert.Equal(Ubiquitous.Data.QuestObjectiveKind.Triggered, objective.Kind);
        Assert.Equal(1, objective.GoalCount);
        Assert.Equal(5692u, objective.ObjectiveSpell);
        Assert.Equal(12020u, objective.ObjectiveStringId);
        Assert.Equal(0u, objective.ObjectiveObject);
        Assert.Equal(0u, objective.ObjectiveCreature);
        Assert.Equal(0u, objective.ObjectiveItem);
    }

    [Fact]
    public void Build114_KeepsAStaticObjectAlongsideARequiredSpell()
    {
        var data = BuildRow(new Dictionary<int, uint>
        {
            [0] = 851,
            [4] = 1,
            [24] = 1,
            [25] = 2,
            [59] = 700,
            [63] = 1,
            [67] = 6025,
        });

        var quest = Assert.Single(QuestTableMapper.MapQuests(
            FTableDocument.Load(data), QuestColumns.Build114,
            npcEntries: new HashSet<uint> { 9013 }, resolveGiverEntry: _ => 9013u));

        var objective = Assert.Single(quest.Objectives);
        Assert.Equal(6025u, objective.ObjectiveSpell);
        Assert.Equal(700u, objective.ObjectiveObject);
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
