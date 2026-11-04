namespace Ubiquitous.Game;

public sealed record ActiveSlowEffect(string TargetId, uint SpellId, int Percent, DateTimeOffset ExpiresAt, string? CasterId = null)
    : UnitEffect(TargetId, SpellId, CasterId);

public sealed class SlowEffectService(UnitEffects effects)
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);

    private const EffectKind Kind = EffectKind.Slow;

    public void Apply(string targetId, uint spellId, int percent, int durationMs, DateTimeOffset now, string? casterId = null)
    {
        effects.RemoveOn<ActiveSlowEffect>(Kind, targetId);
        var duration = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : DefaultDuration;
        effects.Add(new ActiveSlowEffect(targetId, spellId, percent, now + duration, casterId) { Kind = Kind });
    }

    public IReadOnlyList<uint> Clear(string targetId) =>
        [.. effects.RemoveOn<ActiveSlowEffect>(Kind, targetId).Select(e => e.SpellId)];

    public IReadOnlyList<ActiveSlowEffect> ClearWhere(Predicate<ActiveSlowEffect> match) =>
        effects.RemoveWhere(Kind, match);

    public int CurrentPercent(string targetId, DateTimeOffset now)
    {
        var active = effects.Find<ActiveSlowEffect>(Kind, e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal) && now < e.ExpiresAt);
        return active is null ? 0 : Math.Clamp(active.Percent, 0, 100);
    }

    public bool IsSlowed(string targetId, DateTimeOffset now) =>
        effects.Exists<ActiveSlowEffect>(Kind, e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal) && now < e.ExpiresAt);

    public IReadOnlyList<ActiveSlowEffect> TickExpired(DateTimeOffset now) =>
        effects.RemoveWhere<ActiveSlowEffect>(Kind, e => now >= e.ExpiresAt);
}
