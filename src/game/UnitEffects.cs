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
}

public abstract record UnitEffect(string TargetId, uint SpellId, string? CasterId)
{
    public EffectKind Kind { get; init; }

    public abstract bool Harmful { get; }
}

public static class EffectKinds
{
    // A creature's own pulse is its script's to end.
    public static bool EndsWithUnit(EffectKind kind) => kind is not EffectKind.CreaturePulse;

    public static bool Cleansable(EffectKind kind) => kind is EffectKind.Periodic or EffectKind.Control
        or EffectKind.Silence or EffectKind.Slow or EffectKind.Speedup or EffectKind.MeleeAttackSpeed
        or EffectKind.Chill or EffectKind.AttributeModifier;

    // TODO: Verify a ranged attack speed change should outlive death; nothing but its own time ends it.
    public static bool EndsWithDeath(EffectKind kind) => Cleansable(kind) || kind is EffectKind.Absorb
        or EffectKind.Avatar or EffectKind.Stealth or EffectKind.PeriodicTrigger;
}

// Everything on every unit a session tracks, in the order it was put there. Each kind's rules live in
// their own service; this is the one place their state is kept, so a rule about a whole unit is one call.
public sealed class UnitEffects
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
            return true;
        }
    }

    public IReadOnlyList<T> RemoveWhere<T>(EffectKind kind, Predicate<T> match) where T : UnitEffect
    {
        lock (_gate)
        {
            bool Matches(UnitEffect effect) => effect.Kind == kind && match((T)effect);
            var removed = _held.FindAll(Matches);
            if (removed.Count > 0) _held.RemoveAll(Matches);
            return [.. removed.Cast<T>()];
        }
    }

    public IReadOnlyList<UnitEffect> RemoveWhere(Predicate<UnitEffect> match)
    {
        lock (_gate)
        {
            var removed = _held.FindAll(match);
            if (removed.Count > 0) _held.RemoveAll(match);
            return removed;
        }
    }

    public IReadOnlyList<UnitEffect> Clear(string unitId) =>
        RemoveWhere(effect => IsOn(effect, unitId) && EffectKinds.EndsWithUnit(effect.Kind));

    public IReadOnlyList<T> RemoveOn<T>(EffectKind kind, string unitId) where T : UnitEffect =>
        RemoveWhere<T>(kind, effect => IsOn(effect, unitId));

    private static bool IsOn(UnitEffect effect, string unitId) =>
        string.Equals(effect.TargetId, unitId, StringComparison.Ordinal);
}
