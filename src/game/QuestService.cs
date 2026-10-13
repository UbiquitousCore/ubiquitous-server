using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.Game;

public enum QuestMark
{
    None,

    Offered,

    Underway,

    ReadyToHandIn,
}

public static class QuestStates
{
    public const string None = "none";
    public const string Offered = "offered";
    public const string Underway = "underway";
    public const string ReadyToHandIn = "complete";
}

public sealed record QuestStatusEvent(string NetworkId, QuestMark Status);

public sealed record QuestObjectiveProgress(
    int Index,
    QuestObjectiveKind Kind,
    int Count,
    int GoalCount,
    uint ObjectiveItem,
    uint ObjectiveStringId);

public sealed record QuestCounterDelta(int Index, QuestObjectiveKind Kind, int Count);

public sealed record QuestUpdateEvent(
    uint QuestId,
    uint TitleStringId,
    uint DetailsStringId,
    string Status,
    int Slot,
    bool ReadyToHandIn,
    IReadOnlyList<QuestObjectiveProgress> Objectives,
    IReadOnlyList<QuestCounterDelta> Counters);

public sealed record QuestCompletionEvent(
    QuestUpdateEvent Update,
    int Money,
    IReadOnlyList<QuestRewardItem> Items,
    int Xp = 0);

public sealed record QuestLogEntry(
    uint QuestId,
    uint TitleStringId,
    uint DetailsStringId,
    string Status,
    int Slot,
    bool ReadyToHandIn,
    IReadOnlyList<QuestObjectiveProgress> Objectives,
    int MinLevel);

public enum QuestTakeFit
{
    Takes,
    AlreadyHas,
    NotEligible,
}

public sealed class QuestService
{
    public const double ShareRange = 40.0;

    public const int QuestLogSize = 12;

    private sealed class AcceptedQuest
    {
        public required int Slot;
        public readonly Dictionary<int, int> ObjectiveCounts = new();

        public readonly Dictionary<int, HashSet<string>> CountedUnits = new();
    }

    private readonly Dictionary<uint, AcceptedQuest> _accepted = new();
    private readonly HashSet<uint> _completed = new();
    private int _nextSlot;

    public IReadOnlyCollection<uint> Accepted => _accepted.Keys;

    public void Load(IReadOnlyList<PersistedQuestLogEntry> accepted, IReadOnlyCollection<uint> completed)
    {
        _accepted.Clear();
        _completed.Clear();
        foreach (var id in completed) _completed.Add(id);

        var nextSlot = 0;
        foreach (var entry in accepted)
        {
            var restored = new AcceptedQuest { Slot = entry.Slot };
            foreach (var objective in entry.Objectives) restored.ObjectiveCounts[objective.Index] = objective.Count;
            _accepted[entry.QuestId] = restored;
            if (entry.Slot >= nextSlot) nextSlot = entry.Slot + 1;
        }

        _nextSlot = nextSlot;
    }

    public PersistedQuestLog ToPersisted() => new(
        _accepted.Select(pair => new PersistedQuestLogEntry(
            pair.Key, pair.Value.Slot,
            pair.Value.ObjectiveCounts.Select(oc => new PersistedQuestObjective(oc.Key, oc.Value)).ToArray()))
            .ToArray(),
        _completed.ToArray());

    public IReadOnlyCollection<uint> Completed => _completed;

    public QuestUpdateEvent? Accept(
        uint questId, IReadOnlyList<QuestDefinition> quests, Func<uint, int>? heldCountFor = null,
        int playerLevel = int.MaxValue, int? playerClassId = null, IReadOnlySet<uint>? knownSpells = null)
    {
        if (CanTake(questId, quests, playerLevel, playerClassId, knownSpells) != QuestTakeFit.Takes) return null;
        var quest = quests.First(candidate => candidate.Id == questId);
        var accepted = new AcceptedQuest { Slot = _nextSlot++ };
        _accepted[questId] = accepted;
        if (heldCountFor is not null) ApplyHeld(quest, accepted, heldCountFor);
        return BuildUpdate(quest, accepted, QuestStates.Underway, counters: []);
    }

    public QuestTakeFit CanTake(
        uint questId, IReadOnlyList<QuestDefinition> quests,
        int playerLevel = int.MaxValue, int? playerClassId = null, IReadOnlySet<uint>? knownSpells = null)
    {
        var quest = quests.FirstOrDefault(candidate => candidate.Id == questId);
        if (quest is null) return QuestTakeFit.NotEligible;
        if (_accepted.ContainsKey(questId) || _completed.Contains(questId)) return QuestTakeFit.AlreadyHas;
        if (playerLevel < quest.MinLevel) return QuestTakeFit.NotEligible;
        if (!QuestClassGate.Allows(quest, playerClassId)) return QuestTakeFit.NotEligible;
        if (!QuestSpellGate.Allows(quest, knownSpells)) return QuestTakeFit.NotEligible;
        if (!QuestPrerequisiteGate.Allows(quest, _completed)) return QuestTakeFit.NotEligible;
        if (_accepted.Count >= QuestLogSize) return QuestTakeFit.NotEligible;
        return QuestTakeFit.Takes;
    }

    public bool Holds(uint questId) => _accepted.ContainsKey(questId);

    public IReadOnlyList<QuestUpdateEvent> RecordKill(uint creatureEntry, IReadOnlyList<QuestDefinition> quests)
    {
        var events = new List<QuestUpdateEvent>();
        foreach (var (questId, accepted) in _accepted)
        {
            var quest = quests.FirstOrDefault(candidate => candidate.Id == questId);
            if (quest is null) continue;

            foreach (var objective in quest.Objectives)
            {
                if (objective.Kind != QuestObjectiveKind.Killed
                    || objective.ObjectiveCreature != creatureEntry)
                    continue;

                var current = accepted.ObjectiveCounts.GetValueOrDefault(objective.Index);
                if (current >= objective.GoalCount) continue;

                var updated = current + 1;
                accepted.ObjectiveCounts[objective.Index] = updated;
                events.Add(BuildUpdate(quest, accepted, QuestStates.Underway,
                    counters: [new QuestCounterDelta(objective.Index, objective.Kind, updated)]));
            }
        }

        return events;
    }

    public IReadOnlyList<QuestUpdateEvent> RecordSpellCast(
        uint spellId, string? targetId, IReadOnlyList<QuestDefinition> quests)
    {
        var events = new List<QuestUpdateEvent>();
        if (spellId == 0) return events;

        foreach (var (questId, accepted) in _accepted)
        {
            var quest = quests.FirstOrDefault(candidate => candidate.Id == questId);
            if (quest is null) continue;

            foreach (var objective in quest.Objectives)
            {
                if (objective.Kind != QuestObjectiveKind.Triggered
                    || objective.ObjectiveSpell != spellId
                    || objective.ObjectiveObject != 0)
                    continue;

                var current = accepted.ObjectiveCounts.GetValueOrDefault(objective.Index);
                if (current >= objective.GoalCount) continue;
                if (!accepted.CountedUnits.TryGetValue(objective.Index, out var affected))
                {
                    affected = new HashSet<string>(StringComparer.Ordinal);
                    accepted.CountedUnits[objective.Index] = affected;
                }

                if (!affected.Add(targetId ?? string.Empty)) continue;

                var updated = current + 1;
                accepted.ObjectiveCounts[objective.Index] = updated;
                events.Add(BuildUpdate(quest, accepted, QuestStates.Underway,
                    counters: [new QuestCounterDelta(objective.Index, objective.Kind, updated)]));
            }
        }

        return events;
    }

    public IReadOnlyList<QuestUpdateEvent> RecordInteraction(uint staticObjectEntry, IReadOnlyList<QuestDefinition> quests)
    {
        var events = new List<QuestUpdateEvent>();
        foreach (var (questId, accepted) in _accepted)
        {
            var quest = quests.FirstOrDefault(candidate => candidate.Id == questId);
            if (quest is null) continue;

            foreach (var objective in quest.Objectives)
            {
                if (objective.Kind != QuestObjectiveKind.Triggered
                    || objective.ObjectiveObject != staticObjectEntry)
                    continue;

                var current = accepted.ObjectiveCounts.GetValueOrDefault(objective.Index);
                if (current >= objective.GoalCount) continue;

                var updated = current + 1;
                accepted.ObjectiveCounts[objective.Index] = updated;
                events.Add(BuildUpdate(quest, accepted, QuestStates.Underway,
                    counters: [new QuestCounterDelta(objective.Index, objective.Kind, updated)]));
            }
        }

        return events;
    }

    public IReadOnlyList<QuestUpdateEvent> RefreshHeldObjectives(
        Func<uint, int> heldCountFor, IReadOnlyList<QuestDefinition> quests)
    {
        var events = new List<QuestUpdateEvent>();
        foreach (var (questId, accepted) in _accepted)
        {
            var quest = quests.FirstOrDefault(candidate => candidate.Id == questId);
            if (quest is null) continue;

            var changedIndices = ApplyHeld(quest, accepted, heldCountFor);
            if (changedIndices.Count == 0) continue;

            var counters = changedIndices.Select(index =>
            {
                var objective = quest.Objectives.First(candidate => candidate.Index == index);
                return new QuestCounterDelta(index, objective.Kind, accepted.ObjectiveCounts[index]);
            }).ToArray();
            events.Add(BuildUpdate(quest, accepted, QuestStates.Underway, counters));
        }

        return events;
    }

    public QuestCompletionEvent? Complete(
        uint questId,
        int rewardIndex,
        IReadOnlyList<QuestDefinition> quests,
        IReadOnlyDictionary<uint, QuestRewardDefinition>? rewards = null,
        int freeSlots = int.MaxValue,
        int currentMoney = int.MaxValue,
        int? playerLevel = null)
    {
        if (rewardIndex < 0 || !_accepted.TryGetValue(questId, out var accepted)) return null;
        var quest = quests.FirstOrDefault(candidate => candidate.Id == questId);
        if (quest is null || !AllObjectivesMet(quest, accepted)) return null;

        var reward = rewards is not null && rewards.TryGetValue(questId, out var found) ? found : null;
        var money = reward?.Money ?? 0;
        if (money < 0 && currentMoney < -money) return null;

        var paid = new List<QuestRewardItem>(reward?.Fixed ?? []);
        if (reward is not null && reward.Choices.Count > 0)
        {
            if (rewardIndex >= reward.Choices.Count) return null;
            paid.Add(reward.Choices[rewardIndex]);
        }

        if (paid.Count > freeSlots) return null;

        _accepted.Remove(questId);
        if (!quest.IsRepeatable) _completed.Add(questId);
        var xp = LevelingService.QuestXp(
            reward?.Xp ?? 0, quest.Level, playerLevel ?? quest.Level, quest.IsRepeatable);
        return new QuestCompletionEvent(
            BuildUpdate(quest, accepted, QuestStates.ReadyToHandIn, counters: []),
            money,
            paid,
            xp);
    }

    public bool Destroy(int slot, uint questId)
    {
        if (slot < 0 || !_accepted.TryGetValue(questId, out var accepted) || accepted.Slot != slot) return false;
        _accepted.Remove(questId);
        return true;
    }

    public QuestStatusEvent? GetStatus(
        string networkId,
        uint entryId,
        IReadOnlyDictionary<uint, NpcDefinition> npcs,
        IReadOnlyList<QuestDefinition> quests,
        int? playerClassId = null,
        int? playerLevel = null,
        IReadOnlyList<CreatureQuestBindingDefinition>? questFinishersByCreature = null,
        IReadOnlyList<StaticObjectQuestBindingDefinition>? questFinishersByStaticObject = null,
        IReadOnlySet<uint>? knownSpells = null)
    {
        QuestMark? heldHere = null;
        foreach (var (questId, accepted) in _accepted)
        {
            var held = quests.FirstOrDefault(candidate => candidate.Id == questId);
            if (held is null || !QuestFinisherGate.MarksCreature(
                held, entryId, questFinishersByCreature, questFinishersByStaticObject)) continue;
            if (AllObjectivesMet(held, accepted)) return new QuestStatusEvent(networkId, QuestMark.ReadyToHandIn);
            heldHere = QuestMark.Underway;
        }

        if (heldHere is not null) return new QuestStatusEvent(networkId, heldHere.Value);
        if (!npcs.TryGetValue(entryId, out var npc) || !npc.OffersQuests) return null;

        var hasOffer = quests.Any(candidate =>
            candidate.GiverEntryId == entryId && !_accepted.ContainsKey(candidate.Id)
            && !_completed.Contains(candidate.Id)
            && QuestClassGate.Allows(candidate, playerClassId)
            && QuestPrerequisiteGate.Allows(candidate, _completed)
            && QuestLevelGate.Allows(candidate, playerLevel)
            && QuestSpellGate.Allows(candidate, knownSpells));
        return new QuestStatusEvent(networkId, hasOffer ? QuestMark.Offered : QuestMark.None);
    }

    public static string WireStatus(QuestMark status) => status switch
    {
        QuestMark.None => QuestStates.None,
        QuestMark.Offered => QuestStates.Offered,
        QuestMark.Underway => QuestStates.Underway,
        QuestMark.ReadyToHandIn => QuestStates.ReadyToHandIn,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown quest mark."),
    };


    public IReadOnlyList<QuestLogEntry> Log(IReadOnlyList<QuestDefinition> quests)
    {
        var entries = new List<QuestLogEntry>();
        foreach (var (questId, accepted) in _accepted)
        {
            var quest = quests.FirstOrDefault(candidate => candidate.Id == questId);
            if (quest is null) continue;
            entries.Add(new QuestLogEntry(
                quest.Id, quest.TitleStringId, quest.DetailsStringId, QuestStates.Underway,
                accepted.Slot, AllObjectivesMet(quest, accepted), Snapshot(quest, accepted), quest.MinLevel));
        }

        return entries.OrderBy(entry => entry.Slot).ToArray();
    }

    private static QuestUpdateEvent BuildUpdate(
        QuestDefinition quest,
        AcceptedQuest accepted,
        string status,
        IReadOnlyList<QuestCounterDelta> counters) =>
        new(quest.Id, quest.TitleStringId, quest.DetailsStringId, status,
            accepted.Slot, AllObjectivesMet(quest, accepted), Snapshot(quest, accepted), counters);

    private static IReadOnlyList<QuestObjectiveProgress> Snapshot(QuestDefinition quest, AcceptedQuest accepted) =>
        quest.Objectives.Select(objective => new QuestObjectiveProgress(
            objective.Index, objective.Kind,
            accepted.ObjectiveCounts.GetValueOrDefault(objective.Index),
            objective.GoalCount, objective.ObjectiveItem, objective.ObjectiveStringId)).ToArray();

    private static bool AllObjectivesMet(QuestDefinition quest, AcceptedQuest accepted) =>
        quest.Objectives.All(objective =>
            accepted.ObjectiveCounts.GetValueOrDefault(objective.Index) >= objective.GoalCount);

    private static IReadOnlyList<int> ApplyHeld(
        QuestDefinition quest, AcceptedQuest accepted, Func<uint, int> heldCountFor)
    {
        List<int>? changed = null;
        foreach (var objective in quest.Objectives)
        {
            if (objective.Kind != QuestObjectiveKind.Held) continue;
            var held = Math.Min(heldCountFor(objective.ObjectiveItem), objective.GoalCount);
            var current = accepted.ObjectiveCounts.GetValueOrDefault(objective.Index);
            if (held == current) continue;
            accepted.ObjectiveCounts[objective.Index] = held;
            (changed ??= []).Add(objective.Index);
        }

        return (IReadOnlyList<int>?)changed ?? [];
    }
}
