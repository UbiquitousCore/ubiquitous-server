using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ubiquitous.World;

namespace Ubiquitous.Server;

// One turn of the world: maps, then sessions, then saves, then modules.
public sealed class WorldTick(
    SharedWorld world,
    PlaySessionHub sessions,
    Func<DateTimeOffset, CancellationToken, Task>? afterTick = null,
    ILogger? logger = null,
    TimeProvider? time = null)
{
    public static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(SimulationTiming.InCombatIntervalMs);

    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private long _tick;

    public async Task RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var restingTurn = SimulationTiming.IsRestingSessionTurn(_tick++);

        var started = _time.GetTimestamp();
        world.Tick(now);
        var maps = _time.GetElapsedTime(started);

        started = _time.GetTimestamp();
        var slowest = TimeSpan.Zero;
        var ticked = 0;
        await sessions.TickAsync(session =>
        {
            if (!session.NeedsFastTick && !restingTurn) return [];
            var sessionStarted = _time.GetTimestamp();
            var responses = session.TickCombat(now);
            var spent = _time.GetElapsedTime(sessionStarted);
            if (spent > slowest) slowest = spent;
            ticked++;
            return responses;
        });
        var sessionTime = _time.GetElapsedTime(started);

        started = _time.GetTimestamp();
        await sessions.AutosaveAsync(now, cancellationToken);
        var autosave = _time.GetElapsedTime(started);

        started = _time.GetTimestamp();
        if (afterTick is not null) await afterTick(now, cancellationToken);
        var modules = _time.GetElapsedTime(started);

        var total = maps + sessionTime + autosave + modules;
        if (total > Budget)
            _logger.LogWarning(
                "World tick took {TotalMs:F0} ms of its {BudgetMs:F0} ms: maps {MapsMs:F0}, sessions {SessionsMs:F0} ({Ticked} ticked, slowest {SlowestMs:F0}), autosave {AutosaveMs:F0}, modules {ModulesMs:F0}",
                total.TotalMilliseconds, Budget.TotalMilliseconds, maps.TotalMilliseconds,
                sessionTime.TotalMilliseconds, ticked, slowest.TotalMilliseconds,
                autosave.TotalMilliseconds, modules.TotalMilliseconds);
    }
}
