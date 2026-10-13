namespace Ubiquitous.Game;

public sealed record TargetCandidate(string NetworkId, double X, double Y, double Z);

public sealed record TargetResult(bool Accepted, string NetworkId, string? Reason = null);

public sealed class TargetingService
{
    private const double InteractionRangeSquared = 45d * 45d;

    public TargetResult Select(string networkId, double playerX, double playerY, double playerZ,
        IReadOnlyCollection<TargetCandidate> visible)
    {
        var target = visible.FirstOrDefault(candidate =>
            string.Equals(candidate.NetworkId, networkId, StringComparison.Ordinal));
        if (target is null) return new TargetResult(false, networkId, "Target is not visible.");

        var dx = target.X - playerX;
        var dy = target.Y - playerY;
        var dz = target.Z - playerZ;
        return dx * dx + dy * dy + dz * dz <= InteractionRangeSquared
            ? new TargetResult(true, networkId)
            : new TargetResult(false, networkId, "Target is out of range.");
    }
}
