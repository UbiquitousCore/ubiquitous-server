using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Scripts;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private int _exhaustion;
    private int? _exhaustionDay;

    private bool _countingBosses;

    private readonly HashSet<string> _milestonesCounted = new(StringComparer.Ordinal);

    private int ExhaustionNow(DateTimeOffset now) => Exhaustion.Today(_exhaustion, _exhaustionDay, now);

    private void TickExhaustion(DateTimeOffset now, List<string> responses)
    {
        if (_character is null || _instanceKey is not { } key || _copy.Mode != DungeonMode.Legend)
        {
            _countingBosses = false;
            return;
        }

        if (!_countingBosses)
        {
            // Only bosses that fall while the player is inside count against them.
            _countingBosses = true;
            _milestonesCounted.Clear();
            _milestonesCounted.UnionWith(_instances.ReachedBy(key));
            return;
        }

        var gained = 0;
        foreach (var milestone in _instances.ReachedBy(key))
        {
            if (_milestonesCounted.Add(milestone)) gained += ExhaustionForMilestone(milestone);
        }
        if (gained == 0) return;

        var before = ExhaustionNow(now);
        _exhaustion = Exhaustion.Raise(before, gained, _content.Rates.ExhaustionLimit);
        _exhaustionDay = Exhaustion.DayOf(now);
        if (_exhaustion == before) return;
        responses.Add(Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
            new Dictionary<string, int> { [WireCodes.UnitValue.Exhaustion] = _exhaustion })));
    }

    // Asked before a copy of mapId is entered: null lets the player in.
    private string? LegendEntryRefused(ushort mapId)
    {
        if (_instances.ModeOfNext(mapId, InstanceOwnerId) != DungeonMode.Legend) return null;
        return Exhaustion.WhyNotLegend(_level, ExhaustionNow(Now), _content.Rates.ExhaustionLimit) switch
        {
            LegendRefusal.TooLow => Error(WireCodes.ProtocolError.LegendTooLow,
                $"Legend dungeons are open from level {Exhaustion.LegendLevel}."),
            LegendRefusal.Exhausted => Error(WireCodes.ProtocolError.Exhausted,
                "You are too exhausted for a Legend dungeon until the daily reset."),
            _ => null,
        };
    }

    private int ExhaustionForMilestone(string milestone) =>
        DungeonMilestones.BossOf(milestone) is { } entry && _content.Creatures.TryGetValue(entry, out var boss)
            ? _scripts.For(boss, onDungeonMap: true, advertisesQuests: false).ExhaustsOnDeath
            : 0;
}
