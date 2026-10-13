namespace Ubiquitous.World.Tests;

public sealed class SimulationTimingTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void ASessionAtRest_TicksEveryOtherFightingTick(long tick, bool resting)
    {
        Assert.Equal(resting, SimulationTiming.IsRestingSessionTurn(tick));
    }
}
