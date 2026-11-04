namespace Ubiquitous.Game;

public sealed record ActiveControlEffect(string TargetId, uint SpellId, DateTimeOffset ExpiresAt, string? CasterId = null)
    : UnitEffect(TargetId, SpellId, CasterId);

public sealed class ControlEffectService(UnitEffects effects, EffectKind kind)
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);

    public void Apply(string targetId, uint spellId, int durationMs, DateTimeOffset now, string? casterId = null)
    {
        effects.RemoveOn<ActiveControlEffect>(kind, targetId);
        var duration = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : DefaultDuration;
        effects.Add(new ActiveControlEffect(targetId, spellId, now + duration, casterId) { Kind = kind });
    }

    public IReadOnlyList<uint> Clear(string targetId) =>
        [.. effects.RemoveOn<ActiveControlEffect>(kind, targetId).Select(e => e.SpellId)];

    public IReadOnlyList<ActiveControlEffect> ClearWhere(Predicate<ActiveControlEffect> match) =>
        effects.RemoveWhere(kind, match);

    public bool IsControlled(string targetId, DateTimeOffset now) =>
        effects.Exists<ActiveControlEffect>(kind, e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal) && now < e.ExpiresAt);

    public IReadOnlyList<ActiveControlEffect> TickExpired(DateTimeOffset now) =>
        effects.RemoveWhere<ActiveControlEffect>(kind, e => now >= e.ExpiresAt);
}
