using Ubiquitous.Common;

namespace Ubiquitous.Game.Tests;

public sealed class ReportServiceTests
{
    private sealed class FakeReportStore : IReportStore
    {
        public readonly List<PersistedReport> Reports = [];

        public Task AppendAsync(PersistedReport report, CancellationToken cancellationToken = default)
        {
            Reports.Add(report);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PersistedReport>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PersistedReport>>(Reports.ToArray());
    }

    [Fact]
    public async Task SubmitAsync_RefusesReportingYourself()
    {
        var reports = new ReportService(new FakeReportStore());

        var outcome = await reports.SubmitAsync("char-a", "Ayla", "char-a", "Ayla", "griefing");

        Assert.Equal(ReportOutcome.SelfTarget, outcome);
    }

    [Fact]
    public async Task SubmitAsync_RefusesAnEmptyReason()
    {
        var store = new FakeReportStore();
        var reports = new ReportService(store);

        var outcome = await reports.SubmitAsync("char-a", "Ayla", "char-b", "Borin", "   ");

        Assert.Equal(ReportOutcome.EmptyReason, outcome);
        Assert.Empty(store.Reports);
    }

    [Fact]
    public async Task SubmitAsync_RefusesANullReason()
    {
        var reports = new ReportService(new FakeReportStore());

        var outcome = await reports.SubmitAsync("char-a", "Ayla", "char-b", "Borin", null);

        Assert.Equal(ReportOutcome.EmptyReason, outcome);
    }

    [Fact]
    public async Task SubmitAsync_TrimsSurroundingWhitespaceFromTheReason()
    {
        var store = new FakeReportStore();
        var reports = new ReportService(store);

        var outcome = await reports.SubmitAsync("char-a", "Ayla", "char-b", "Borin", "  griefing in the square  ");

        Assert.Equal(ReportOutcome.Ok, outcome);
        var report = Assert.Single(store.Reports);
        Assert.Equal("griefing in the square", report.Reason);
    }

    [Fact]
    public async Task SubmitAsync_ClampsAnOverlongReasonToTheSameLimitChatUses()
    {
        var store = new FakeReportStore();
        var reports = new ReportService(store);
        var overlong = new string('x', ReportService.MaxReasonLength + 50);

        var outcome = await reports.SubmitAsync("char-a", "Ayla", "char-b", "Borin", overlong);

        Assert.Equal(ReportOutcome.Ok, outcome);
        var report = Assert.Single(store.Reports);
        Assert.Equal(ReportService.MaxReasonLength, report.Reason.Length);
    }

    [Fact]
    public async Task SubmitAsync_PersistsTheReporterAndTargetTogether()
    {
        var store = new FakeReportStore();
        var reports = new ReportService(store);

        await reports.SubmitAsync("char-a", "Ayla", "char-b", "Borin", "spamming trade chat");

        var report = Assert.Single(store.Reports);
        Assert.Equal("char-a", report.ReporterGuid);
        Assert.Equal("Ayla", report.ReporterName);
        Assert.Equal("char-b", report.TargetGuid);
        Assert.Equal("Borin", report.TargetName);
        Assert.Equal("spamming trade chat", report.Reason);
        Assert.NotEmpty(report.Id);
    }

    [Fact]
    public async Task SubmitAsync_TwoDistinctReportsGetTwoDistinctIds()
    {
        var store = new FakeReportStore();
        var reports = new ReportService(store);

        await reports.SubmitAsync("char-a", "Ayla", "char-b", "Borin", "first report");
        await reports.SubmitAsync("char-a", "Ayla", "char-b", "Borin", "second report");

        Assert.Equal(2, store.Reports.Count);
        Assert.NotEqual(store.Reports[0].Id, store.Reports[1].Id);
    }

    [Fact]
    public async Task SubmitAsync_StampsTheReportWithTheProvidedClock()
    {
        var store = new FakeReportStore();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var reports = new ReportService(store, () => now);

        await reports.SubmitAsync("char-a", "Ayla", "char-b", "Borin", "griefing");

        var report = Assert.Single(store.Reports);
        Assert.Equal(now.ToUnixTimeSeconds(), report.ReportedAtUnixSeconds);
    }
}
