namespace Ubiquitous.Game;

public sealed record ActiveStealthEffect(string TargetId, uint SpellId, DateTimeOffset ExpiresAt)
    : UnitEffect(TargetId, SpellId, null)
{
    public override bool Harmful => false;
}

public sealed class StealthEffectService(UnitEffects effects)
{
    private const EffectKind Kind = EffectKind.Stealth;

    public void Apply(string targetId, uint spellId, int durationMs, DateTimeOffset now)
    {
        effects.RemoveOn<ActiveStealthEffect>(Kind, targetId);
        effects.Add(new ActiveStealthEffect(targetId, spellId, EffectLength.ExpiresAt(now, durationMs))
        { Kind = Kind, AppliedAt = now, Lasts = EffectLength.Lasts(durationMs) });
    }

    public IReadOnlyList<uint> Clear(string targetId) =>
        [.. effects.RemoveOn<ActiveStealthEffect>(Kind, targetId).Select(e => e.SpellId)];

    public bool IsStealthed(string targetId, DateTimeOffset now) =>
        effects.Exists<ActiveStealthEffect>(Kind, e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal) && now < e.ExpiresAt);

    public bool IsHeld(string targetId) =>
        effects.Exists<ActiveStealthEffect>(Kind, e => string.Equals(e.TargetId, targetId, StringComparison.Ordinal));

    public IReadOnlyList<ActiveStealthEffect> TickExpired(DateTimeOffset now) =>
        effects.RemoveWhere<ActiveStealthEffect>(Kind, e => now >= e.ExpiresAt);
}
