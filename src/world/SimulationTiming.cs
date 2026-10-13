namespace Ubiquitous.World;

public static class SimulationTiming
{
    public const double InCombatIntervalMs = 400d;
    public const int RestingSessionTickEvery = 2;

    public static bool IsRestingSessionTurn(long tick) => tick % RestingSessionTickEvery == 0;
}
