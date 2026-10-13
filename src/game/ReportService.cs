using Ubiquitous.Common;

namespace Ubiquitous.Game;

public enum ReportOutcome { Ok, SelfTarget, EmptyReason }

public sealed class ReportService
{
    public const int MaxReasonLength = 256;

    private readonly IReportStore _store;
    private readonly Func<DateTimeOffset> _now;

    public ReportService(IReportStore store, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<ReportOutcome> SubmitAsync(
        string reporterGuid, string reporterName,
        string targetGuid, string targetName,
        string? reason, CancellationToken cancellationToken = default)
    {
        if (string.Equals(reporterGuid, targetGuid, StringComparison.Ordinal))
            return ReportOutcome.SelfTarget;
        if (string.IsNullOrWhiteSpace(reason))
            return ReportOutcome.EmptyReason;

        var trimmed = reason.Trim();
        if (trimmed.Length > MaxReasonLength) trimmed = trimmed[..MaxReasonLength];

        var report = new PersistedReport(
            Guid.NewGuid().ToString("N"), reporterGuid, reporterName, targetGuid, targetName,
            trimmed, _now().ToUnixTimeSeconds());
        await _store.AppendAsync(report, cancellationToken);
        return ReportOutcome.Ok;
    }
}
