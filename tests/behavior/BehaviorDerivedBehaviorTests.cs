using Ubiquitous.Common;
using Ubiquitous.Game;
using Ubiquitous.World;

namespace Ubiquitous.Behavior.Tests;

public class MovementBehaviorTests
{
    [Theory]
    [InlineData(15.0f, true)]
    [InlineData(17.9f, true)]
    [InlineData(18.1f, false)]
    public void PlayerStep_AroundCheatThreshold_IsJudged(float stepUnits, bool plausible)
    {
        var from = new Position(100f, 50f, -20f);
        var to = new Position(from.X + stepUnits, from.Y, from.Z);
        var world = new Ubiquitous.World.WorldSessionState(13, from, 0);

        var decision = world.ApplyPlayerMovement(to, 0f);

        Assert.Equal(plausible, decision.Accepted);
        Assert.Equal(!plausible, decision.Corrected);
        Assert.Equal(plausible ? to : from, decision.Position);
    }
}
