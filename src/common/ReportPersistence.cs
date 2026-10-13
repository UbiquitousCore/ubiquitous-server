namespace Ubiquitous.Common;

public sealed record PersistedReport(
    string Id,
    string ReporterGuid,
    string ReporterName,
    string TargetGuid,
    string TargetName,
    string Reason,
    long ReportedAtUnixSeconds);

public interface IReportStore
{
    Task AppendAsync(PersistedReport report, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PersistedReport>> ListAsync(CancellationToken cancellationToken = default);
}
