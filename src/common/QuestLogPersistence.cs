namespace Ubiquitous.Common;

public sealed record PersistedQuestObjective(int Index, int Count);

public sealed record PersistedQuestLogEntry(uint QuestId, int Slot, IReadOnlyList<PersistedQuestObjective> Objectives);

public sealed record PersistedQuestLog(
    IReadOnlyList<PersistedQuestLogEntry> Accepted,
    IReadOnlyList<uint> Completed)
{
    public static readonly PersistedQuestLog Empty = new([], []);
}

public interface IQuestLogStore
{
    Task<PersistedQuestLog> GetAsync(string characterGuid, CancellationToken cancellationToken = default);
    Task SaveAsync(string characterGuid, PersistedQuestLog log, CancellationToken cancellationToken = default);
}
