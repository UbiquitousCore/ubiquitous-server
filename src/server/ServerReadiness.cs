namespace Ubiquitous.Server;

public sealed record ServerReadiness(bool ProcessUp, bool DataReady, string? Reason = null);

public sealed class ServerReadinessState
{
    private volatile ServerReadiness _current = new(true, false, "Data has not been loaded.");

    public ServerReadiness Current => _current;

    public void SetDataReady() => _current = new(true, true);
    public void SetDataUnavailable(string reason) => _current = new(true, false, reason);
}
