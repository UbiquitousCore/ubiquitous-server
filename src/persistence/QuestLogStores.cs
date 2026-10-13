using Ubiquitous.Common;

namespace Ubiquitous.Persistence;

public sealed class FileQuestLogStore(string directory) : IQuestLogStore
{
    public async Task<PersistedQuestLog> GetAsync(string characterGuid, CancellationToken cancellationToken = default) =>
        await JsonFile.ReadAsync<PersistedQuestLog>(PathFor(characterGuid), cancellationToken) ?? PersistedQuestLog.Empty;

    public Task SaveAsync(string characterGuid, PersistedQuestLog log, CancellationToken cancellationToken = default) =>
        JsonFile.WriteAsync(PathFor(characterGuid), log, cancellationToken);

    private string PathFor(string characterGuid) => Path.Combine(directory, $"quests-{characterGuid}.json");
}
