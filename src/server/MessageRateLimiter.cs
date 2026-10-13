namespace Ubiquitous.Server;

public sealed class MessageRateLimiter(int messagesPerSecond)
{
    private double _tokens = messagesPerSecond;
    private long _refilledAtTicks = -1;

    public bool TryTake(long nowTicks)
    {
        if (_refilledAtTicks >= 0)
        {
            var elapsedSeconds = (double)(nowTicks - _refilledAtTicks) / TimeSpan.TicksPerSecond;
            _tokens = Math.Min(messagesPerSecond, _tokens + elapsedSeconds * messagesPerSecond);
        }

        _refilledAtTicks = nowTicks;
        if (_tokens < 1) return false;
        _tokens -= 1;
        return true;
    }
}
