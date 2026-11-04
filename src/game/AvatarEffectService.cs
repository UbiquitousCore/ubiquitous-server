namespace Ubiquitous.Game;

public sealed record ActiveAvatar(string TargetId, uint SpellId, DateTimeOffset? ExpiresAt)
    : UnitEffect(TargetId, SpellId, null)
{
    public override bool Harmful => false;
}

// The shape a body has been turned into by an aura (a cookie, a wand, a spirit). One at a time per body;
// a spell with no duration lasts until it is cast again or the body is cleared.
public sealed class AvatarEffectService(UnitEffects effects)
{
    private const EffectKind Kind = EffectKind.Avatar;

    public uint? ActiveOn(string targetId) => Worn(targetId)?.SpellId;

    // The spell it replaced, if another shape was worn.
    public uint? Apply(string targetId, uint spellId, int durationMs, DateTimeOffset now)
    {
        var held = Worn(targetId);
        uint? replaced = held is not null && held.SpellId != spellId ? held.SpellId : null;
        TimeSpan? lasts = durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : null;
        var shape = new ActiveAvatar(targetId, spellId, now + lasts) { Kind = Kind, AppliedAt = now, Lasts = lasts };
        if (held is not null) effects.Reapply(held, shape);
        else effects.Add(shape);
        return replaced;
    }

    public IReadOnlyList<uint> Clear(string targetId) =>
        [.. effects.RemoveOn<ActiveAvatar>(Kind, targetId).Select(held => held.SpellId)];

    public IReadOnlyList<ActiveAvatar> TickExpired(DateTimeOffset now) =>
        effects.RemoveWhere<ActiveAvatar>(Kind, held => held.ExpiresAt is { } until && now >= until);

    private ActiveAvatar? Worn(string targetId) =>
        effects.Find<ActiveAvatar>(Kind, held => string.Equals(held.TargetId, targetId, StringComparison.Ordinal));
}
