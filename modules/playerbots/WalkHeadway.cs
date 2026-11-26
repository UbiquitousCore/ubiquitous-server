namespace Ubiquitous.Modules.Playerbots;

// How close each goal has come and when it last got closer, kept whatever else the
// bot does in between, so a goal it never gets nearer is given up. Reaching a goal
// clears its record: a bot that went there, left, and comes back starts afresh.
internal sealed class WalkHeadway
{
    private const float Headway = 1f;
    private static readonly TimeSpan MostTimeWithout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ForgetAfter = TimeSpan.FromMinutes(2);

    private readonly Dictionary<string, (float Closest, DateTimeOffset Since)> _seen = new(StringComparer.Ordinal);

    public bool Progressing(string purpose, float left, bool reached, DateTimeOffset now)
    {
        foreach (var stale in _seen.Where(pair => now - pair.Value.Since > ForgetAfter).Select(pair => pair.Key).ToList())
            _seen.Remove(stale);
        if (reached)
        {
            _seen.Remove(purpose);
            return true;
        }

        if (!_seen.TryGetValue(purpose, out var seen) || left < seen.Closest - Headway)
        {
            _seen[purpose] = (left, now);
            return true;
        }

        return now - seen.Since < MostTimeWithout;
    }

    public void Forget(string purpose) => _seen.Remove(purpose);
}
