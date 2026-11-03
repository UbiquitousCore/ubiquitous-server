using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Scripts;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private int _exhaustion;
    private int? _exhaustionDay;

    // Time inside a Legend copy is counted from here; null while outside one.
    private DateTimeOffset? _exhaustingSince;

    private readonly HashSet<string> _milestonesCounted = new(StringComparer.Ordinal);

    private int ExhaustionNow(DateTimeOffset now) => Exhaustion.Today(_exhaustion, _exhaustionDay, now);

    private void TickExhaustion(DateTimeOffset now, List<string> responses)
    {
        if (_character is null || _instanceKey is not { } key || _copy.Mode != DungeonMode.Legend)
        {
            _exhaustingSince = null;
            return;
        }

        if (_exhaustingSince is not { } since)
        {
            // Only bosses that fall while the player is inside count against them.
            _exhaustingSince = now;
            _milestonesCounted.Clear();
            _milestonesCounted.UnionWith(_instances.ReachedBy(key));
            return;
        }

        var gained = 0;
        var periods = (int)((now - since) / Exhaustion.RisesEvery);
        if (periods > 0)
        {
            gained += periods;
            _exhaustingSince = since + (Exhaustion.RisesEvery * periods);
        }
        foreach (var milestone in _instances.ReachedBy(key))
        {
            if (_milestonesCounted.Add(milestone)) gained += ExhaustionForMilestone(milestone);
        }
        if (gained == 0) return;

        var before = ExhaustionNow(now);
        _exhaustion = Exhaustion.Raise(before, gained);
        _exhaustionDay = Exhaustion.DayOf(now);
        if (_exhaustion == before) return;
        responses.Add(Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
            new Dictionary<string, int> { [WireCodes.UnitValue.Exhaustion] = _exhaustion })));
    }

    private int ExhaustionForMilestone(string milestone) =>
        DungeonMilestones.BossOf(milestone) is { } entry && _content.Creatures.TryGetValue(entry, out var boss)
            ? _scripts.For(boss, onDungeonMap: true, advertisesQuests: false).ExhaustsOnDeath
            : 0;
}
