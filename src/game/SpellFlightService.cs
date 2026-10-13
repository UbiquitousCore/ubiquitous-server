using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed record SpellInFlight(uint SpellId, SpellDefinition Spell, string TargetId, DateTimeOffset ArrivesAt);

public sealed class SpellFlightService
{
    private readonly List<SpellInFlight> _inFlight = [];

    // A ranged weapon hit already flies as a shot; a leap carries the caster, not a projectile.
    public static bool Flies(SpellDefinition spell) =>
        spell.Speed > 0 && !spell.EffectRangedWeaponHit && !spell.EffectLeapToTarget;

    public static TimeSpan FlightTime(SpellDefinition spell, double distance) =>
        TimeSpan.FromSeconds(Math.Max(0, distance) / spell.Speed);

    public void Launch(uint spellId, SpellDefinition spell, string targetId, double distance, DateTimeOffset now) =>
        _inFlight.Add(new SpellInFlight(spellId, spell, targetId, now + FlightTime(spell, distance)));

    public IReadOnlyList<SpellInFlight> TakeArrived(DateTimeOffset now)
    {
        var arrived = _inFlight.FindAll(flight => now >= flight.ArrivesAt);
        if (arrived.Count > 0) _inFlight.RemoveAll(flight => now >= flight.ArrivesAt);
        return arrived;
    }

    public void Clear() => _inFlight.Clear();
}
