using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class WorldTickTests
{
    [Fact]
    public async Task ATurnOverItsBudgetIsLoggedWithThePhaseThatSpentIt()
    {
        var clock = new FakeTimeProvider();
        var log = new WarningLog();
        var turn = new WorldTick(new SharedWorld(), new PlaySessionHub(),
            (_, _) => { clock.Advance(WorldTick.Budget + TimeSpan.FromMilliseconds(100)); return Task.CompletedTask; },
            log, clock);

        await turn.RunAsync(clock.GetUtcNow(), CancellationToken.None);

        var warning = Assert.Single(log.Warnings);
        Assert.Contains("took 500 ms of its 400 ms", warning);
        Assert.Contains("modules 500", warning);
    }

    [Fact]
    public async Task ATurnWithinItsBudgetLogsNothing()
    {
        var clock = new FakeTimeProvider();
        var log = new WarningLog();
        var turn = new WorldTick(new SharedWorld(), new PlaySessionHub(), logger: log, time: clock);

        await turn.RunAsync(clock.GetUtcNow(), CancellationToken.None);

        Assert.Empty(log.Warnings);
    }

    private sealed class WarningLog : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
