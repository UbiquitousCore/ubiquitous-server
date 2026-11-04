namespace Ubiquitous.Game;

public sealed record ActiveAura(
    string TargetId, uint SpellId, bool IsHeal, int PerTick, int TicksRemaining, DateTimeOffset NextTickAt, string? CasterId = null)
    : UnitEffect(TargetId, SpellId, CasterId)
{
    public override bool Harmful => !IsHeal;
}

public sealed record AuraTick(string TargetId, uint SpellId, bool IsHeal, int PerTick, bool Expired, string? CasterId = null);

public sealed class AuraService(UnitEffects effects)
{
    public const int TickCount = 3;
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(3);
    public static TimeSpan Duration => TickInterval * TickCount;

    private const EffectKind Kind = EffectKind.Periodic;

    // How long a periodic effect runs: its spell's own duration where it gives one, a whole number of ticks.
    public static TimeSpan LastsFor(int durationMs) =>
        durationMs > 0 ? TickInterval * TicksIn(durationMs) : Duration;

    private static int TicksIn(int durationMs) =>
        Math.Max(1, (int)Math.Round(durationMs / TickInterval.TotalMilliseconds));

    public void Apply(string targetId, uint spellId, int perTick, bool isHeal, DateTimeOffset now, string? casterId = null,
        int durationMs = 0)
    {
        effects.RemoveWhere<ActiveAura>(Kind, a => string.Equals(a.TargetId, targetId, StringComparison.Ordinal) && a.SpellId == spellId);
        var ticks = durationMs > 0 ? TicksIn(durationMs) : TickCount;
        effects.Add(new ActiveAura(targetId, spellId, isHeal, perTick, ticks, now + TickInterval, casterId)
            { Kind = Kind, AppliedAt = now, Lasts = TickInterval * ticks });
    }

    public IReadOnlyList<uint> Clear(string targetId) =>
        [.. effects.RemoveOn<ActiveAura>(Kind, targetId).Select(a => a.SpellId)];

    public IReadOnlyList<AuraTick> Tick(DateTimeOffset now)
    {
        var held = effects.Of<ActiveAura>(Kind);
        var ticks = new List<AuraTick>();
        for (int i = held.Count - 1; i >= 0; i--)
        {
            var aura = held[i];
            if (now < aura.NextTickAt) continue;

            bool expiring = aura.TicksRemaining <= 1;
            ticks.Add(new AuraTick(aura.TargetId, aura.SpellId, aura.IsHeal, aura.PerTick, expiring, aura.CasterId));
            if (expiring)
            {
                effects.Remove(aura);
            }
            else
            {
                effects.Replace(aura, aura with { TicksRemaining = aura.TicksRemaining - 1, NextTickAt = now + TickInterval });
            }
        }

        return ticks;
    }
}
