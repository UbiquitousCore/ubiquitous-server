namespace Ubiquitous.Game;

public sealed record ActiveSpeedupEffect(string TargetId, uint SpellId, int Percent, DateTimeOffset ExpiresAt, string? CasterId = null)
    : UnitEffect(TargetId, SpellId, CasterId)
{
    public override bool Harmful => Percent < 0;
}

public sealed class SpeedupEffectService(UnitEffects effects, EffectKind kind)
{
    public void Apply(string targetId, uint spellId, int percent, int durationMs, DateTimeOffset now, string? casterId = null)
    {
        effects.RemoveOn<ActiveSpeedupEffect>(kind, targetId);
        effects.Add(new ActiveSpeedupEffect(targetId, spellId, percent, EffectLength.ExpiresAt(now, durationMs), casterId) { Kind = kind });
    }

    public int CurrentPercent(string targetId, DateTimeOffset now) =>
        effects.Find<ActiveSpeedupEffect>(kind, e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal) && now < e.ExpiresAt)
            ?.Percent ?? 0;

    public bool IsActive(string targetId, DateTimeOffset now) =>
        effects.Exists<ActiveSpeedupEffect>(kind, e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal) && now < e.ExpiresAt);

    public IReadOnlyList<ActiveSpeedupEffect> TickExpired(DateTimeOffset now) =>
        effects.RemoveWhere<ActiveSpeedupEffect>(kind, e => now >= e.ExpiresAt);
}
