namespace Ubiquitous.Game;

public sealed record ActiveStealthEffect(string TargetId, uint SpellId, DateTimeOffset ExpiresAt);

public sealed class StealthEffectService
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);

    private readonly List<ActiveStealthEffect> _active = new();
    private readonly Lock _gate = new();

    public void Apply(string targetId, uint spellId, int durationMs, DateTimeOffset now)
    {
        lock (_gate)
        {
            _active.RemoveAll(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal));
            var duration = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : DefaultDuration;
            _active.Add(new ActiveStealthEffect(targetId, spellId, now + duration));
        }
    }

    public IReadOnlyList<uint> Clear(string targetId)
    {
        lock (_gate)
        {
            var removed = _active.Where(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal))
                .Select(e => e.SpellId).ToArray();
            _active.RemoveAll(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal));
            return removed;
        }
    }

    public bool IsStealthed(string targetId, DateTimeOffset now)
    {
        lock (_gate)
            return _active.Exists(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal) && now < e.ExpiresAt);
    }

    public bool IsHeld(string targetId)
    {
        lock (_gate)
            return _active.Exists(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal));
    }

    public IReadOnlyList<ActiveStealthEffect> TickExpired(DateTimeOffset now)
    {
        lock (_gate)
        {
            var expired = _active.FindAll(e => now >= e.ExpiresAt);
            if (expired.Count > 0) _active.RemoveAll(e => now >= e.ExpiresAt);
            return expired;
        }
    }
}
