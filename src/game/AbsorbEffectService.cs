namespace Ubiquitous.Game;

public sealed record ActiveAbsorbEffect(string TargetId, uint SpellId, int Remaining, int Percent, DateTimeOffset ExpiresAt)
    : UnitEffect(TargetId, SpellId, null)
{
    public override bool Harmful => false;
}

public sealed record AbsorbEnded(ActiveAbsorbEffect Shield, bool Broken);

public sealed class AbsorbEffectService(UnitEffects effects)
{
    private const EffectKind Kind = EffectKind.Absorb;

    private readonly List<ActiveAbsorbEffect> _usedUp = new();

    public void Apply(string targetId, uint spellId, int amount, int percent, int durationMs, DateTimeOffset now)
    {
        var shield = new ActiveAbsorbEffect(
            targetId, spellId, Math.Max(0, amount), Math.Clamp(percent, 0, 100), EffectLength.ExpiresAt(now, durationMs)) { Kind = Kind };
        if (ShieldOn(targetId) is { } held) effects.Replace(held, shield);
        else effects.Add(shield);
    }

    public int Absorb(string targetId, int damage, DateTimeOffset now)
    {
        if (damage <= 0 || ShieldOn(targetId) is not { } shield || now >= shield.ExpiresAt) return damage;
        var share = (int)Math.Round(damage * shield.Percent / 100.0, MidpointRounding.AwayFromZero);
        var taken = Math.Min(share, shield.Remaining);
        var left = shield.Remaining - taken;
        if (left <= 0)
        {
            effects.Remove(shield);
            _usedUp.Add(shield with { Remaining = 0 });
        }
        else effects.Replace(shield, shield with { Remaining = left });
        return damage - taken;
    }

    public IReadOnlyList<AbsorbEnded> TickExpired(DateTimeOffset now)
    {
        var ended = new List<AbsorbEnded>(_usedUp.Select(shield => new AbsorbEnded(shield, true)));
        _usedUp.Clear();
        ended.AddRange(effects.RemoveWhere<ActiveAbsorbEffect>(Kind, shield => now >= shield.ExpiresAt)
            .Select(shield => new AbsorbEnded(shield, false)));
        return ended;
    }

    private ActiveAbsorbEffect? ShieldOn(string targetId) =>
        effects.Find<ActiveAbsorbEffect>(Kind, shield => string.Equals(shield.TargetId, targetId, StringComparison.Ordinal));
}
