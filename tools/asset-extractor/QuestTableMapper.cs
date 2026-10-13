using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public sealed record QuestTableColumns(
    int Id, int GiverEntryId, int TitleStringId, int DetailsStringId, int MinLevel, int AllowedClassMask,
    int Level = -1,
    int QuestFlags = -1, int AnyOfQuestsColumn = -1, IReadOnlyList<int>? PrerequisitesAll = null,
    IReadOnlyList<int>? ObjectiveStringIds = null, IReadOnlyList<int>? ObjectiveItems = null,
    IReadOnlyList<int>? ObjectiveItemCounts = null,
    IReadOnlyList<IReadOnlyList<int>>? ObjectiveCreatureColumns = null,
    IReadOnlyList<int>? ObjectiveObjects = null, IReadOnlyList<int>? ObjectiveGoalCounts = null,
    IReadOnlyList<int>? ObjectiveSpells = null)
{
    public IReadOnlyList<int> ObjectiveSpells { get; init; } = ObjectiveSpells ?? [];
    public IReadOnlyList<int> PrerequisitesAll { get; init; } = PrerequisitesAll ?? [];
    public IReadOnlyList<int> ObjectiveStringIds { get; init; } = ObjectiveStringIds ?? [];
    public IReadOnlyList<int> ObjectiveItems { get; init; } = ObjectiveItems ?? [];
    public IReadOnlyList<int> ObjectiveItemCounts { get; init; } = ObjectiveItemCounts ?? [];
    public IReadOnlyList<IReadOnlyList<int>> ObjectiveCreatureColumns { get; init; } = ObjectiveCreatureColumns ?? [];
    public IReadOnlyList<int> ObjectiveObjects { get; init; } = ObjectiveObjects ?? [];
    public IReadOnlyList<int> ObjectiveGoalCounts { get; init; } = ObjectiveGoalCounts ?? [];
}

public static class QuestTableMapper
{
    public static IReadOnlyList<QuestDefinition> MapQuests(
        FTableDocument document,
        QuestTableColumns columns,
        IReadOnlySet<uint> npcEntries,
        Func<uint, uint?>? resolveGiverEntry = null,
        IReadOnlySet<uint>? nonCreatureStarterQuestIds = null)
    {
        if (columns.GiverEntryId < 0 && resolveGiverEntry is null)
        {
            throw new FTableFormatException(
                "This table has no giver column; a resolveGiverEntry delegate is required.");
        }

        var quests = new List<QuestDefinition>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            uint id = RequireUInt(row, columns.Id, i, "id");
            if (!seen.Add(id))
            {
                throw new FTableFormatException($"Duplicate quest id {id}.");
            }

            uint? giverEntryId = columns.GiverEntryId >= 0
                ? RequireUInt(row, columns.GiverEntryId, i, "giver")
                : resolveGiverEntry!(id);
            uint titleStringId = RequireUInt(row, columns.TitleStringId, i, "title");
            uint detailsStringId = RequireUInt(row, columns.DetailsStringId, i, "details");
            int minLevel = RequireInt(row, columns.MinLevel, i, "level");
            int maxLevel = columns.Level >= 0 ? RequireInt(row, columns.Level, i, "quest level") : 0;
            uint requiredClassMask = RequireUInt(row, columns.AllowedClassMask, i, "required class mask");
            uint questFlags = columns.QuestFlags >= 0
                ? RequireUInt(row, columns.QuestFlags, i, "quest flags")
                : 0;
            bool isRepeatable = (questFlags & 0x0004) != 0;

            var prerequisitesAll = columns.PrerequisitesAll
                .Select(column => RequireUInt(row, column, i, "required quest"))
                .Where(value => value != 0)
                .ToArray();
            var anyOfQuestIds = columns.AnyOfQuestsColumn >= 0
                && row[columns.AnyOfQuestsColumn] is string anyOfText
                    ? anyOfText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .Select(token => uint.TryParse(token, out var parsed) ? parsed : (uint?)null)
                        .Where(parsed => parsed is > 0)
                        .Select(parsed => parsed!.Value)
                        .ToArray()
                    : [];

            bool hasKnownNpcGiver = giverEntryId is not null && npcEntries.Contains(giverEntryId.Value);
            bool hasNonCreatureStarter = nonCreatureStarterQuestIds?.Contains(id) ?? false;
            if (!hasKnownNpcGiver && !hasNonCreatureStarter)
            {
                throw new FTableFormatException($"Quest {id} references missing giver {giverEntryId}.");
            }

            if (minLevel < 0)
            {
                throw new FTableFormatException($"Quest {id} has a negative minimum level.");
            }

            var objectives = MapObjectives(row, columns, i);

            quests.Add(new QuestDefinition(
                id, hasKnownNpcGiver ? giverEntryId : null, titleStringId, detailsStringId, minLevel,
                Objectives: objectives, AllowedClassMask: requiredClassMask, IsRepeatable: isRepeatable,
                PrerequisitesAll: prerequisitesAll, AnyOfQuestIds: anyOfQuestIds,
                Level: maxLevel));
        }

        return quests;
    }

    private static IReadOnlyList<QuestObjectiveDefinition> MapObjectives(
        IReadOnlyList<object> row, QuestTableColumns columns, int rowIndex)
    {
        int SlotValue(IReadOnlyList<int> slotColumns, int slot) =>
            slot < slotColumns.Count && slotColumns[slot] is int column
                ? (int)RequireUInt(row, column, rowIndex, "objective")
                : 0;

        var objectives = new List<QuestObjectiveDefinition>();
        int slotCount = new[]
        {
            columns.ObjectiveItems.Count, columns.ObjectiveCreatureColumns.Count,
            columns.ObjectiveObjects.Count, columns.ObjectiveGoalCounts.Count,
            columns.ObjectiveSpells.Count,
        }.Max();

        for (int slot = 0; slot < slotCount; slot++)
        {
            uint itemId = (uint)SlotValue(columns.ObjectiveItems, slot);
            uint goalCount = (uint)SlotValue(columns.ObjectiveGoalCounts, slot);
            uint objectiveStringId = (uint)SlotValue(columns.ObjectiveStringIds, slot);

            if (itemId != 0)
            {
                uint itemCount = (uint)SlotValue(columns.ObjectiveItemCounts, slot);
                objectives.Add(new QuestObjectiveDefinition(
                    slot, QuestObjectiveKind.Held, (int)itemCount, ObjectiveItem: itemId,
                    ObjectiveStringId: objectiveStringId));
                continue;
            }

            uint killCreatureId = slot < columns.ObjectiveCreatureColumns.Count
                ? columns.ObjectiveCreatureColumns[slot]
                    .Select(column => RequireUInt(row, column, rowIndex, "objective kill id"))
                    .FirstOrDefault(value => value != 0)
                : 0;
            if (killCreatureId != 0)
            {
                objectives.Add(new QuestObjectiveDefinition(
                    slot, QuestObjectiveKind.Killed, (int)goalCount, ObjectiveCreature: killCreatureId,
                    ObjectiveStringId: objectiveStringId));
                continue;
            }

            uint staticObjectId = (uint)SlotValue(columns.ObjectiveObjects, slot);
            uint objectiveSpell = (uint)SlotValue(columns.ObjectiveSpells, slot);
            if (staticObjectId != 0 || objectiveSpell != 0 || goalCount != 0)
            {
                objectives.Add(new QuestObjectiveDefinition(
                    slot, QuestObjectiveKind.Triggered, (int)goalCount, ObjectiveObject: staticObjectId,
                    ObjectiveStringId: objectiveStringId, ObjectiveSpell: objectiveSpell));
            }
        }

        return objectives.Count == 0 ? Array.Empty<QuestObjectiveDefinition>() : objectives;
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not uint value)
        {
            throw new FTableFormatException($"Row {index} has no usable quest {field}.");
        }

        return value;
    }

    private static int RequireInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column < 0 || column >= row.Count || row[column] is not int and not uint)
        {
            throw new FTableFormatException($"Row {index} has no usable quest {field}.");
        }

        return row[column] is uint u ? unchecked((int)u) : (int)row[column];
    }
}
