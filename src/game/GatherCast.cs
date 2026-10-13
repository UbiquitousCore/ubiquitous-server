namespace Ubiquitous.Game;

public sealed record GatherInProgress(string NetworkId, DateTimeOffset ReadyAt);

public sealed class GatherCast
{
    private GatherInProgress? _current;

    public bool InProgress => _current is not null;

    public bool Begin(string networkId, DateTimeOffset now, TimeSpan castTime)
    {
        if (_current is not null) return false;
        _current = new GatherInProgress(networkId, now + castTime);
        return true;
    }

    public GatherInProgress? TakeFinished(DateTimeOffset now)
    {
        if (_current is not { } current || now < current.ReadyAt) return null;
        _current = null;
        return current;
    }

    public bool Cancel()
    {
        if (_current is null) return false;
        _current = null;
        return true;
    }
}
