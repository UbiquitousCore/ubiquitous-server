using Ubiquitous.Common;

namespace Ubiquitous.Persistence;

public sealed class FileReportStore(string directory) : IReportStore
{
    public Task AppendAsync(PersistedReport report, CancellationToken cancellationToken = default) =>
        JsonFile.WriteAsync(Path.Combine(directory, $"report-{report.Id}.json"), report, cancellationToken);

    public Task<IReadOnlyList<PersistedReport>> ListAsync(CancellationToken cancellationToken = default) =>
        JsonFile.ReadEachAsync<PersistedReport>(directory, "report-*.json", cancellationToken);
}

public sealed class InMemoryReportStore : IReportStore
{
    private readonly List<PersistedReport> _reports = [];

    public Task AppendAsync(PersistedReport report, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _reports.Add(report);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PersistedReport>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PersistedReport>>(_reports.ToArray());
    }
}
