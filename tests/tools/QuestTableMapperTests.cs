using System.Buffers.Binary;
using Ubiquitous.AssetExtractor;
using Ubiquitous.Data;

namespace Ubiquitous.Integration.Tests;

public class QuestTableMapperTests
{
    private static readonly QuestTableColumns Columns = new(0, 1, 2, 3, 4, 5);
    private static readonly HashSet<uint> Npcs = [9001];

    [Fact]
    public void MapQuests_ConvertsRowsWithResolvedGiver()
    {
        var data = BuildTable(rows: 2);
        WriteQuest(data, rows: 2, row: 0, id: 5001, giver: 9001, title: 12001, details: 12002, minLevel: 1);
        WriteQuest(data, rows: 2, row: 1, id: 5002, giver: 9001, title: 12003, details: 12004, minLevel: 5);

        var quests = QuestTableMapper.MapQuests(FTableDocument.Load(data), Columns, Npcs);

        Assert.Equal(2, quests.Count);
        Assert.Equal(new Ubiquitous.Data.QuestDefinition(5001, 9001, 12001, 12002, 1), quests[0]);
    }

    [Fact]
    public void MapQuests_ReadsTheRequiredClassMask()
    {
        var data = BuildTable(rows: 1);
        WriteQuest(data, rows: 1, row: 0, id: 5001, giver: 9001, title: 12001, details: 12002, minLevel: 1,
            requiredClassMask: 0b0110);

        var quest = Assert.Single(QuestTableMapper.MapQuests(FTableDocument.Load(data), Columns, Npcs));

        Assert.Equal(0b0110u, quest.AllowedClassMask);
    }

    private static readonly QuestTableColumns ColumnsWithQuestFlags = Columns with { QuestFlags = 6 };

    [Fact]
    public void MapQuests_ReadsIsRepeatableFromTheQuestFlagsBit()
    {
        var data = BuildTableWithQuestFlags(id: 5001, giver: 9001, title: 12001, details: 12002, minLevel: 1,
            questFlags: 0b0110);

        var quest = Assert.Single(QuestTableMapper.MapQuests(FTableDocument.Load(data), ColumnsWithQuestFlags, Npcs));

        Assert.True(quest.IsRepeatable);
    }

    [Fact]
    public void MapQuests_LeavesIsRepeatableFalseWhenTheRepeatableBitIsNotSet()
    {
        var data = BuildTableWithQuestFlags(id: 5001, giver: 9001, title: 12001, details: 12002, minLevel: 1,
            questFlags: 0b0010);

        var quest = Assert.Single(QuestTableMapper.MapQuests(FTableDocument.Load(data), ColumnsWithQuestFlags, Npcs));

        Assert.False(quest.IsRepeatable);
    }

    [Fact]
    public void MapQuests_LeavesIsRepeatableFalseWhenNoQuestFlagsColumnIsConfigured()
    {
        var data = BuildTable(rows: 1);
        WriteQuest(data, rows: 1, row: 0, id: 5001, giver: 9001, title: 12001, details: 12002, minLevel: 1);

        var quest = Assert.Single(QuestTableMapper.MapQuests(FTableDocument.Load(data), Columns, Npcs));

        Assert.False(quest.IsRepeatable);
    }

    private static byte[] BuildTableWithQuestFlags(
        uint id, uint giver, uint title, uint details, int minLevel, uint questFlags)
    {
        const string format = "uuuuiuu";
        const int entrySize = 28;
        const int headerSize = 44;
        const int rows = 1;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + format.Length;
        int offsetStrings = offsetEntry + rows * entrySize;
        var data = new byte[offsetStrings];
        WriteAscii(data, 0, "FTABLE");
        WriteU32(data, 8, 0x100000);
        WriteU32(data, 12, rows);
        WriteU32(data, 16, format.Length);
        WriteU32(data, 20, offsetIndex);
        WriteU32(data, 24, offsetFormat);
        WriteU32(data, 28, offsetEntry);
        WriteU32(data, 32, entrySize);
        WriteU32(data, 36, offsetStrings);
        WriteU32(data, 40, 0);
        WriteAscii(data, offsetFormat, format);

        int at = offsetEntry;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), giver);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 8, 4), title);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 12, 4), details);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at + 16, 4), minLevel);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 20, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 24, 4), questFlags);
        return data;
    }

    private static readonly QuestTableColumns ColumnsWithPrerequisites = Columns with
    {
        AnyOfQuestsColumn = 6,
        PrerequisitesAll = [7, 8, 9, 10],
    };

    private static FTableDocument DocumentWithRow(params object[] row) =>
        FTableDocument.Load(BuildTable(rows: 1)).WithRows([row]);

    [Fact]
    public void MapQuests_ReadsARequiredQuestChain()
    {
        var document = DocumentWithRow(7u, 9001u, 12001u, 12002u, 1u, 0u, "", 6u, 0u, 0u, 0u);

        var quest = Assert.Single(QuestTableMapper.MapQuests(document, ColumnsWithPrerequisites, Npcs));

        Assert.Equal(new uint[] { 6 }, quest.PrerequisitesAll);
    }

    [Fact]
    public void MapQuests_DropsZeroRequiredQuestSlotsRatherThanKeepingThemAsALiteralId()
    {
        var document = DocumentWithRow(6u, 9001u, 12001u, 12002u, 1u, 0u, "", 0u, 0u, 0u, 0u);

        var quest = Assert.Single(QuestTableMapper.MapQuests(document, ColumnsWithPrerequisites, Npcs));

        Assert.Empty(quest.PrerequisitesAll);
    }

    [Fact]
    public void MapQuests_ReadsAllFourRequiredQuestSlotsWhenPresent()
    {
        var document = DocumentWithRow(11u, 9001u, 12001u, 12002u, 1u, 0u, "", 5u, 6u, 7u, 8u);

        var quest = Assert.Single(QuestTableMapper.MapQuests(document, ColumnsWithPrerequisites, Npcs));

        Assert.Equal(new uint[] { 5, 6, 7, 8 }, quest.PrerequisitesAll);
    }

    [Fact]
    public void MapQuests_LeavesRequiredQuestIdsEmptyWhenNoColumnIsConfigured()
    {
        var data = BuildTable(rows: 1);
        WriteQuest(data, rows: 1, row: 0, id: 5001, giver: 9001, title: 12001, details: 12002, minLevel: 1);

        var quest = Assert.Single(QuestTableMapper.MapQuests(FTableDocument.Load(data), Columns, Npcs));

        Assert.Empty(quest.PrerequisitesAll);
    }

    [Fact]
    public void MapQuests_ReadsASpaceSeparatedAnyOfQuestList()
    {
        var document = DocumentWithRow(
            540u, 9001u, 12001u, 12002u, 1u, 0u, "537 538 639 640", 0u, 0u, 0u, 0u);

        var quest = Assert.Single(QuestTableMapper.MapQuests(document, ColumnsWithPrerequisites, Npcs));

        Assert.Equal(new uint[] { 537, 538, 639, 640 }, quest.AnyOfQuestIds);
    }

    [Fact]
    public void MapQuests_DropsANonNumericTokenFromTheAnyOfQuestListRatherThanFailingTheImport()
    {
        var document = DocumentWithRow(
            888u, 9001u, 12001u, 12002u, 1u, 0u, "not an id", 0u, 0u, 0u, 0u);

        var quest = Assert.Single(QuestTableMapper.MapQuests(document, ColumnsWithPrerequisites, Npcs));

        Assert.Empty(quest.AnyOfQuestIds);
    }

    [Fact]
    public void MapQuests_LeavesAnyOfQuestIdsEmptyWhenNoColumnIsConfigured()
    {
        var data = BuildTable(rows: 1);
        WriteQuest(data, rows: 1, row: 0, id: 5001, giver: 9001, title: 12001, details: 12002, minLevel: 1);

        var quest = Assert.Single(QuestTableMapper.MapQuests(FTableDocument.Load(data), Columns, Npcs));

        Assert.Empty(quest.AnyOfQuestIds);
    }

    private static readonly QuestTableColumns ColumnsWithObjectives = Columns with
    {
        ObjectiveStringIds = [10, 11, 12, 13],
        ObjectiveItems = [14, 15, 16, 17],
        ObjectiveItemCounts = [18, 19, 20, 21],
        ObjectiveCreatureColumns = [[22, 23, 24, 25], [26, 27, 28, 29], [30, 31, 32, 33], [34, 35, 36, 37]],
        ObjectiveObjects = [38, 39, 40, 41],
        ObjectiveGoalCounts = [42, 43, 44, 45],
    };

    private static object[] ObjectiveRow(uint id = 5001, uint giver = 9001)
    {
        var row = new object[46];
        for (int i = 0; i < row.Length; i++) row[i] = 0u;
        row[0] = id;
        row[1] = giver;
        row[2] = 12001u;
        row[3] = 12002u;
        row[4] = 1u;
        row[5] = 0u;
        return row;
    }

    [Fact]
    public void MapQuests_ReadsAKilledObjectiveFromRequiredIdKillAndRequiredIdCount()
    {
        var row = ObjectiveRow();
        row[22] = 756u;
        row[42] = 5u;
        row[10] = 173u;

        var quest = Assert.Single(QuestTableMapper.MapQuests(DocumentWithRow(row), ColumnsWithObjectives, Npcs));

        var objective = Assert.Single(quest.Objectives);
        Assert.Equal(0, objective.Index);
        Assert.Equal(QuestObjectiveKind.Killed, objective.Kind);
        Assert.Equal(756u, objective.ObjectiveCreature);
        Assert.Equal(5, objective.GoalCount);
        Assert.Equal(173u, objective.ObjectiveStringId);
    }

    [Fact]
    public void MapQuests_ReadsAHeldObjectiveFromRequiredItem()
    {
        var row = ObjectiveRow();
        row[14] = 1035u;
        row[18] = 6u;

        var quest = Assert.Single(QuestTableMapper.MapQuests(DocumentWithRow(row), ColumnsWithObjectives, Npcs));

        var objective = Assert.Single(quest.Objectives);
        Assert.Equal(QuestObjectiveKind.Held, objective.Kind);
        Assert.Equal(1035u, objective.ObjectiveItem);
        Assert.Equal(6, objective.GoalCount);
    }

    [Fact]
    public void MapQuests_ReadsATriggeredObjectiveFromRequiredStaticObject()
    {
        var row = ObjectiveRow();
        row[38] = 200u;
        row[42] = 1u;

        var quest = Assert.Single(QuestTableMapper.MapQuests(DocumentWithRow(row), ColumnsWithObjectives, Npcs));

        var objective = Assert.Single(quest.Objectives);
        Assert.Equal(QuestObjectiveKind.Triggered, objective.Kind);
        Assert.Equal(200u, objective.ObjectiveObject);
        Assert.Equal(1, objective.GoalCount);
    }

    [Fact]
    public void MapQuests_ReadsAScriptedTriggeredObjectiveWithNoStaticObjectIdAtAll()
    {
        var row = ObjectiveRow();
        row[42] = 1u;
        row[10] = 327u;

        var quest = Assert.Single(QuestTableMapper.MapQuests(DocumentWithRow(row), ColumnsWithObjectives, Npcs));

        var objective = Assert.Single(quest.Objectives);
        Assert.Equal(QuestObjectiveKind.Triggered, objective.Kind);
        Assert.Equal(0u, objective.ObjectiveObject);
        Assert.Equal(1, objective.GoalCount);
        Assert.Equal(327u, objective.ObjectiveStringId);
    }

    [Fact]
    public void MapQuests_PrefersHeldOverStaticObjectWhenASlotNamesBoth()
    {
        var row = ObjectiveRow();
        row[14] = 1124u;
        row[18] = 1u;
        row[38] = 3u;

        var quest = Assert.Single(QuestTableMapper.MapQuests(DocumentWithRow(row), ColumnsWithObjectives, Npcs));

        var objective = Assert.Single(quest.Objectives);
        Assert.Equal(QuestObjectiveKind.Held, objective.Kind);
        Assert.Equal(1124u, objective.ObjectiveItem);
    }

    [Fact]
    public void MapQuests_UsesTheFirstOfUpToFourAlternateKillIdsInASlot()
    {
        var row = ObjectiveRow();
        row[22] = 500u;
        row[23] = 600u;
        row[42] = 3u;

        var quest = Assert.Single(QuestTableMapper.MapQuests(DocumentWithRow(row), ColumnsWithObjectives, Npcs));

        var objective = Assert.Single(quest.Objectives);
        Assert.Equal(500u, objective.ObjectiveCreature);
    }

    [Fact]
    public void MapQuests_DropsAnEmptyObjectiveSlotRatherThanAddingAPlaceholder()
    {
        var row = ObjectiveRow();
        row[22] = 756u;
        row[42] = 5u;

        var quest = Assert.Single(QuestTableMapper.MapQuests(DocumentWithRow(row), ColumnsWithObjectives, Npcs));

        Assert.Single(quest.Objectives);
    }

    [Fact]
    public void MapQuests_LeavesObjectivesEmptyWhenNoObjectiveColumnsAreConfigured()
    {
        var data = BuildTable(rows: 1);
        WriteQuest(data, rows: 1, row: 0, id: 5001, giver: 9001, title: 12001, details: 12002, minLevel: 1);

        var quest = Assert.Single(QuestTableMapper.MapQuests(FTableDocument.Load(data), Columns, Npcs));

        Assert.Empty(quest.Objectives);
    }

    [Fact]
    public void MapQuests_RejectsMissingGiverReference()
    {
        var data = BuildTable(rows: 1);
        WriteQuest(data, rows: 1, row: 0, id: 5001, giver: 9999, title: 12001, details: 12002, minLevel: 1);

        var error = Assert.Throws<FTableFormatException>(
            () => QuestTableMapper.MapQuests(FTableDocument.Load(data), Columns, Npcs));

        Assert.Contains("missing giver 9999", error.Message);
    }

    [Fact]
    public void MapQuests_KeepsAnItemStarterQuestWithANullGiverEvenWithNoKnownNpc()
    {
        var data = BuildTable(rows: 1);
        WriteQuest(data, rows: 1, row: 0, id: 5003, giver: 9999, title: 12005, details: 12006, minLevel: 2);

        var quests = QuestTableMapper.MapQuests(
            FTableDocument.Load(data), Columns, Npcs, nonCreatureStarterQuestIds: new HashSet<uint> { 5003 });

        var quest = Assert.Single(quests);
        Assert.Equal(5003u, quest.Id);
        Assert.Null(quest.GiverEntryId);
    }

    [Fact]
    public void MapQuests_StillRejectsAQuestNamedByNeitherAGiverNorAnItemStarter()
    {
        var data = BuildTable(rows: 1);
        WriteQuest(data, rows: 1, row: 0, id: 5001, giver: 9999, title: 12001, details: 12002, minLevel: 1);

        Assert.Throws<FTableFormatException>(() => QuestTableMapper.MapQuests(
            FTableDocument.Load(data), Columns, Npcs, nonCreatureStarterQuestIds: new HashSet<uint> { 9999 }));
    }

    [Fact]
    public void MapQuests_RejectsNegativeMinimumLevel()
    {
        var data = BuildTable(rows: 1);
        WriteQuest(data, rows: 1, row: 0, id: 5001, giver: 9001, title: 12001, details: 12002, minLevel: -1);

        Assert.Throws<FTableFormatException>(
            () => QuestTableMapper.MapQuests(FTableDocument.Load(data), Columns, Npcs));
    }

    [Fact]
    public void MapQuests_RejectsDuplicateQuestId()
    {
        var data = BuildTable(rows: 2);
        WriteQuest(data, rows: 2, row: 0, id: 5001, giver: 9001, title: 12001, details: 12002, minLevel: 1);
        WriteQuest(data, rows: 2, row: 1, id: 5001, giver: 9001, title: 12003, details: 12004, minLevel: 1);

        var error = Assert.Throws<FTableFormatException>(
            () => QuestTableMapper.MapQuests(FTableDocument.Load(data), Columns, Npcs));

        Assert.Contains("Duplicate quest id 5001", error.Message);
    }

    [Fact]
    public void MapQuests_RejectsWrongCellType()
    {
        var data = BuildTable(rows: 1);
        WriteQuest(data, rows: 1, row: 0, id: 5001, giver: 9001, title: 12001, details: 12002, minLevel: 1);

        var shifted = Columns with { Id = 4 };

        Assert.Throws<FTableFormatException>(
            () => QuestTableMapper.MapQuests(FTableDocument.Load(data), shifted, Npcs));
    }

    private static void WriteQuest(
        byte[] data, int rows, int row, uint id, uint giver, uint title, uint details, int minLevel,
        uint requiredClassMask = 0)
    {
        int at = 44 + rows * 4 + "uuuuiu".Length + row * 24;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at, 4), id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 4, 4), giver);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 8, 4), title);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 12, 4), details);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at + 16, 4), minLevel);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at + 20, 4), requiredClassMask);
    }

    private static byte[] BuildTable(int rows)
    {
        const string format = "uuuuiu";
        const int entrySize = 24;
        const int headerSize = 44;
        int offsetIndex = headerSize;
        int offsetFormat = offsetIndex + rows * 4;
        int offsetEntry = offsetFormat + format.Length;
        int offsetStrings = offsetEntry + rows * entrySize;
        var data = new byte[offsetStrings];
        WriteAscii(data, 0, "FTABLE");
        WriteU32(data, 8, 0x100000);
        WriteU32(data, 12, rows);
        WriteU32(data, 16, format.Length);
        WriteU32(data, 20, offsetIndex);
        WriteU32(data, 24, offsetFormat);
        WriteU32(data, 28, offsetEntry);
        WriteU32(data, 32, entrySize);
        WriteU32(data, 36, offsetStrings);
        WriteU32(data, 40, 0);
        WriteAscii(data, offsetFormat, format);
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
