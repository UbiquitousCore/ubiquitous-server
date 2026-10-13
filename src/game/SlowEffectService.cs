namespace Ubiquitous.Game;

public sealed record ActiveSlowEffect(string TargetId, uint SpellId, int Percent, DateTimeOffset ExpiresAt, string? CasterId = null);

public sealed class SlowEffectService
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);

    private readonly List<ActiveSlowEffect> _active = new();

    public void Apply(string targetId, uint spellId, int percent, int durationMs, DateTimeOffset now, string? casterId = null)
    {
        _active.RemoveAll(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal));
        var duration = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : DefaultDuration;
        _active.Add(new ActiveSlowEffect(targetId, spellId, percent, now + duration, casterId));
    }

    public IReadOnlyList<uint> Clear(string targetId)
    {
        var removed = _active.Where(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal))
            .Select(e => e.SpellId).ToArray();
        _active.RemoveAll(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal));
        return removed;
    }

    public IReadOnlyList<ActiveSlowEffect> ClearWhere(Predicate<ActiveSlowEffect> match)
    {
        var removed = _active.FindAll(match);
        if (removed.Count > 0) _active.RemoveAll(match);
        return removed;
    }

    public int CurrentPercent(string targetId, DateTimeOffset now)
    {
        var active = _active.Find(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal) && now < e.ExpiresAt);
        return active is null ? 0 : Math.Clamp(active.Percent, 0, 100);
    }

    public bool IsSlowed(string targetId, DateTimeOffset now) =>
        _active.Exists(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal) && now < e.ExpiresAt);

    public IReadOnlyList<ActiveSlowEffect> TickExpired(DateTimeOffset now)
    {
        var expired = _active.FindAll(e => now >= e.ExpiresAt);
        if (expired.Count > 0) _active.RemoveAll(e => now >= e.ExpiresAt);
        return expired;
    }
}
