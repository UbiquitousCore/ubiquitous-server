namespace Ubiquitous.Game;

// Observed in play: a landing faster than HarmSpeed costs a share of maximum
// health that grows with the landing speed, and DeathSpeed or faster kills.
// Only a fall that began with a jump counts; walking off a ledge, and landing
// in water, never hurt.
public static class FallDamage
{
    public const float Gravity = 18f;
    public const float HarmSpeed = 18f;
    public const float DeathSpeed = 30f;

    public static float LandingSpeed(float height) =>
        height <= 0f ? 0f : MathF.Sqrt(2f * Gravity * height);

    public static int DamageFor(float landingSpeed, int maxHealth)
    {
        if (maxHealth <= 0 || landingSpeed <= HarmSpeed) return 0;
        var share = (landingSpeed - HarmSpeed) / (DeathSpeed - HarmSpeed);
        return (int)(maxHealth * share);
    }
}

// Follows one body through the air from the movement states its client
// reports. Heights come from accepted positions, never from a sent speed.
public sealed class FallTracker
{
    private float? _peakY;

    public void Jumping(float y) => _peakY = _peakY is { } peak ? MathF.Max(peak, y) : y;

    // A drop that did not start with a jump is not followed.
    public void Falling(float y)
    {
        if (_peakY is { } peak) _peakY = MathF.Max(peak, y);
    }

    // How far below the highest point a ground landing ended, or null when no
    // counted fall was under way. The fall is over either way.
    public float? Landed(float y)
    {
        var peak = _peakY;
        _peakY = null;
        return peak is { } top ? MathF.Max(0f, top - y) : null;
    }

    public void Cancel() => _peakY = null;
}
