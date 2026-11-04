namespace Ubiquitous.Game;

public sealed record ActiveChillEffect(string TargetId, uint SpellId, DateTimeOffset ExpiresAt, string? CasterId = null)
    : UnitEffect(TargetId, SpellId, CasterId);

public sealed class ChillEffectService(UnitEffects effects)
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);

    private const EffectKind Kind = EffectKind.Chill;

    public void Apply(string targetId, uint spellId, int durationMs, DateTimeOffset now, string? casterId = null)
    {
        effects.RemoveOn<ActiveChillEffect>(Kind, targetId);
        var duration = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : DefaultDuration;
        effects.Add(new ActiveChillEffect(targetId, spellId, now + duration, casterId) { Kind = Kind });
    }

    public IReadOnlyList<uint> Clear(string targetId) =>
        [.. effects.RemoveOn<ActiveChillEffect>(Kind, targetId).Select(e => e.SpellId)];

    public IReadOnlyList<ActiveChillEffect> ClearWhere(Predicate<ActiveChillEffect> match) =>
        effects.RemoveWhere(Kind, match);

    public bool IsChilled(string targetId, DateTimeOffset now) =>
        effects.Exists<ActiveChillEffect>(Kind, e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal) && now < e.ExpiresAt);

    public IReadOnlyList<ActiveChillEffect> TickExpired(DateTimeOffset now) =>
        effects.RemoveWhere<ActiveChillEffect>(Kind, e => now >= e.ExpiresAt);
}
