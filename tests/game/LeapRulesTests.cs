using Ubiquitous.Common;
using Ubiquitous.Game;

namespace Ubiquitous.Game.Tests;

public sealed class LeapRulesTests
{
    [Fact]
    public void LandsTheStandOffShortOfTheTargetAlongTheWayThere()
    {
        var landing = LeapRules.Landing(new Position(0, 0, 0), new Position(6, 4, 8), 1.5f);

        Assert.NotNull(landing);
        Assert.Equal(5.1f, landing!.Value.X, 3);
        Assert.Equal(6.8f, landing.Value.Z, 3);
        Assert.Equal(4f, landing.Value.Y, 3);
    }

    [Fact]
    public void GoesNowhereFromWithinTheStandOff()
    {
        Assert.Null(LeapRules.Landing(new Position(0, 0, 0), new Position(1, 0, 0), 1.5f));
    }
}
