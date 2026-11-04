namespace Ubiquitous.Game;

public enum EffectKind
{
    Periodic,
    Control,
    Silence,
    Slow,
    Speedup,
    MeleeAttackSpeed,
    Chill,
    AttributeModifier,
    Absorb,
    Avatar,
    Stealth,
    PeriodicTrigger,
    RangeAttackSpeed,
    CreaturePulse,
    Mount,
    CreatureAura,
}

public abstract record UnitEffect(string TargetId, uint SpellId, string? CasterId)
{
    public EffectKind Kind { get; init; }

    public DateTimeOffset AppliedAt { get; init; }

    // How long the client was told it lasts; none for what stays until taken off.
    public TimeSpan? Lasts { get; init; }

    public abstract bool Harmful { get; }

    public HeldAura Held(DateTimeOffset now)
    {
        var lasts = Lasts is { } span ? (int)span.TotalMilliseconds : 0;
        var left = lasts <= 0 ? 0 : (int)Math.Max(0, Math.Min(lasts, Math.Ceiling(lasts - (now - AppliedAt).TotalMilliseconds)));
        return new HeldAura(TargetId, CasterId ?? TargetId, SpellId, left, lasts, Harmful);
    }
}

// Shown among a unit's auras while something else holds what it does: the saddle, or a creature's script.
public sealed record ShownAura(string TargetId, uint SpellId, string? CasterId) : UnitEffect(TargetId, SpellId, CasterId)
{
    public override bool Harmful => false;
}

public static class EffectKinds
{
    // A mount comes off when the rider gets down, and a creature's own aura is its script's to end.
    public static bool EndsWithUnit(EffectKind kind) =>
        kind is not (EffectKind.CreaturePulse or EffectKind.Mount or EffectKind.CreatureAura);

    public static bool Shown(EffectKind kind) => kind is not EffectKind.CreaturePulse;

    public static bool Cleansable(EffectKind kind) => kind is EffectKind.Periodic or EffectKind.Control
        or EffectKind.Silence or EffectKind.Slow or EffectKind.Speedup or EffectKind.MeleeAttackSpeed
        or EffectKind.Chill or EffectKind.AttributeModifier;

    // TODO: Verify a ranged attack speed change should outlive death; nothing but its own time ends it.
    public static bool EndsWithDeath(EffectKind kind) => Cleansable(kind) || kind is EffectKind.Absorb
        or EffectKind.Avatar or EffectKind.Stealth or EffectKind.PeriodicTrigger;
}

// Everything on every unit a session tracks, in the order it was put there. Each kind's rules live in
// their own service; this is the one place their state is kept, so a rule about a whole unit is one call.
// Whoever watches is told each effect put on or taken off, but not a tick or a soak.
public sealed class UnitEffects(Action<UnitEffect, bool>? changed = null)
{
    // Another session asks whether this one's hero is hidden.
    private readonly Lock _gate = new();
    private readonly List<UnitEffect> _held = [];

    public IReadOnlyList<UnitEffect> On(string unitId)
    {
        lock (_gate) return _held.FindAll(effect => IsOn(effect, unitId));
    }

    public IReadOnlyList<T> Of<T>(EffectKind kind) where T : UnitEffect
    {
        lock (_gate) return [.. _held.Where(effect => effect.Kind == kind).Cast<T>()];
    }

    public T? Find<T>(EffectKind kind, Predicate<T> match) where T : UnitEffect
    {
        lock (_gate) return (T?)_held.Find(effect => effect.Kind == kind && match((T)effect));
    }

    public bool Exists<T>(EffectKind kind, Predicate<T> match) where T : UnitEffect => Find(kind, match) is not null;

    public void Add(UnitEffect effect)
    {
        lock (_gate) _held.Add(effect);
        changed?.Invoke(effect, true);
    }

    // Put on again in the same place: what watches sees the old one end and the new one begin.
    public void Reapply(UnitEffect held, UnitEffect again)
    {
        Replace(held, again);
        changed?.Invoke(held, false);
        changed?.Invoke(again, true);
    }

    public void Replace(UnitEffect held, UnitEffect updated)
    {
        lock (_gate)
        {
            var at = _held.FindIndex(effect => ReferenceEquals(effect, held));
            if (at >= 0) _held[at] = updated;
        }
    }

    public bool Remove(UnitEffect held)
    {
        lock (_gate)
        {
            var at = _held.FindIndex(effect => ReferenceEquals(effect, held));
            if (at < 0) return false;
            _held.RemoveAt(at);
        }
        changed?.Invoke(held, false);
        return true;
    }

    public IReadOnlyList<T> RemoveWhere<T>(EffectKind kind, Predicate<T> match) where T : UnitEffect
    {
        List<UnitEffect> removed;
        lock (_gate)
        {
            bool Matches(UnitEffect effect) => effect.Kind == kind && match((T)effect);
            removed = _held.FindAll(Matches);
            if (removed.Count > 0) _held.RemoveAll(Matches);
        }
        Ended(removed);
        return [.. removed.Cast<T>()];
    }

    public IReadOnlyList<UnitEffect> RemoveWhere(Predicate<UnitEffect> match)
    {
        List<UnitEffect> removed;
        lock (_gate)
        {
            removed = _held.FindAll(match);
            if (removed.Count > 0) _held.RemoveAll(match);
        }
        Ended(removed);
        return removed;
    }

    public IReadOnlyList<UnitEffect> Clear(string unitId) =>
        RemoveWhere(effect => IsOn(effect, unitId) && EffectKinds.EndsWithUnit(effect.Kind));

    // Out of sight: nothing is shown on it any more. A creature's pulse runs on, as its script says.
    public IReadOnlyList<UnitEffect> Forget(string unitId) =>
        RemoveWhere(effect => IsOn(effect, unitId) && effect.Kind != EffectKind.CreaturePulse);

    public IReadOnlyList<T> RemoveOn<T>(EffectKind kind, string unitId) where T : UnitEffect =>
        RemoveWhere<T>(kind, effect => IsOn(effect, unitId));

    private void Ended(List<UnitEffect> removed)
    {
        if (changed is null) return;
        foreach (var effect in removed) changed(effect, false);
    }

    private static bool IsOn(UnitEffect effect, string unitId) =>
        string.Equals(effect.TargetId, unitId, StringComparison.Ordinal);
}
