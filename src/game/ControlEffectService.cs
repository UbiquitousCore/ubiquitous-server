namespace Ubiquitous.Game;

public sealed record ActiveControlEffect(string TargetId, uint SpellId, DateTimeOffset ExpiresAt, string? CasterId = null);

public sealed class ControlEffectService
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);

    private readonly List<ActiveControlEffect> _active = new();

    public void Apply(string targetId, uint spellId, int durationMs, DateTimeOffset now, string? casterId = null)
    {
        _active.RemoveAll(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal));
        var duration = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : DefaultDuration;
        _active.Add(new ActiveControlEffect(targetId, spellId, now + duration, casterId));
    }

    public IReadOnlyList<uint> Clear(string targetId)
    {
        var removed = _active.Where(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal))
            .Select(e => e.SpellId).ToArray();
        _active.RemoveAll(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal));
        return removed;
    }

    public IReadOnlyList<ActiveControlEffect> ClearWhere(Predicate<ActiveControlEffect> match)
    {
        var removed = _active.FindAll(match);
        if (removed.Count > 0) _active.RemoveAll(match);
        return removed;
    }

    public bool IsControlled(string targetId, DateTimeOffset now) =>
        _active.Exists(e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal) && now < e.ExpiresAt);

    public IReadOnlyList<ActiveControlEffect> TickExpired(DateTimeOffset now)
    {
        var expired = _active.FindAll(e => now >= e.ExpiresAt);
        if (expired.Count > 0) _active.RemoveAll(e => now >= e.ExpiresAt);
        return expired;
    }
}
