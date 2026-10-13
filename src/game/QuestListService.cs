using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed record QuestListEvent(uint GiverEntryId, IReadOnlyList<QuestDefinition> Quests);

public sealed class QuestListService
{
    public QuestListEvent? List(uint giverEntryId, IReadOnlyList<QuestDefinition> quests,
        IReadOnlyDictionary<uint, NpcDefinition> npcs, int? playerClassId = null,
        IReadOnlyCollection<uint>? accepted = null, IReadOnlyCollection<uint>? completed = null,
        int? playerLevel = null, IReadOnlySet<uint>? knownSpells = null)
    {
        if (!npcs.TryGetValue(giverEntryId, out var npc) || !npc.OffersQuests) return null;
        return new QuestListEvent(giverEntryId, quests
            .Where(quest => quest.GiverEntryId == giverEntryId && QuestClassGate.Allows(quest, playerClassId)
                && (accepted is null || !accepted.Contains(quest.Id))
                && (completed is null || !completed.Contains(quest.Id))
                && QuestPrerequisiteGate.Allows(quest, completed)
                && QuestLevelGate.Allows(quest, playerLevel)
                && QuestSpellGate.Allows(quest, knownSpells))
            .ToArray());
    }
}
