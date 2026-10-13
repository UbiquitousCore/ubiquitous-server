using Ubiquitous.Data;
using Ubiquitous.Game;

namespace Ubiquitous.Scripts;

public sealed record DungeonProgressContext(
    ushort MapId,
    IReadOnlyCollection<uint> BossEntriesInView,
    IReadOnlyCollection<uint> DeadBossEntries,
    IReadOnlyCollection<string> Reached);

public sealed record DungeonProgress(
    bool Completed,
    string Name,
    IReadOnlyCollection<string> Reached);

public static class DungeonMilestones
{
    public static string BossDown(uint entryId) => $"boss.{entryId}.down";
}

public abstract class InstanceScript
{
    public abstract string Name { get; }

    public abstract DungeonProgress Progress(DungeonProgressContext context);
}

public sealed class AuthoredInstanceScript(IReadOnlyList<DungeonDefinition> rules) : InstanceScript
{
    private readonly IReadOnlyList<DungeonDefinition> _rules = rules;

    public override string Name => "dungeon.authored";

    public override DungeonProgress Progress(DungeonProgressContext context)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var completed = false;
        var name = string.Empty;

        foreach (var rule in _rules)
        {
            if (rule.MapId != context.MapId || rule.BossEntryIds.Count == 0) continue;

            foreach (var entryId in rule.BossEntryIds)
            {
                if (context.DeadBossEntries.Contains(entryId)) reached.Add(DungeonMilestones.BossDown(entryId));
            }

            if (completed) continue;
            if (!rule.BossEntryIds.All(entryId =>
                reached.Contains(DungeonMilestones.BossDown(entryId))
                || context.Reached.Contains(DungeonMilestones.BossDown(entryId)))) continue;
            completed = true;
            name = rule.Name;
        }

        if (completed) reached.Add(InstanceService.RunCompleted);
        return new DungeonProgress(completed, name, reached);
    }
}
