using Ubiquitous.Modules.Playerbots;

namespace Ubiquitous.Playerbots.Tests;

public sealed class WalkHeadwayTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AGoalNeverGotNearer_IsGivenUpAfterEightSeconds()
    {
        var headway = new WalkHeadway();

        Assert.True(headway.Progressing("Creature:1", 20, reached: false, Start));
        Assert.True(headway.Progressing("Creature:1", 20, reached: false, Start.AddSeconds(7.9)));
        Assert.False(headway.Progressing("Creature:1", 20, reached: false, Start.AddSeconds(8)));
    }

    [Fact]
    public void ABotThatReachedAGoalLeftAndComesBack_IsNotGivenUp()
    {
        var headway = new WalkHeadway();
        headway.Progressing("Creature:1", 15, reached: false, Start);
        headway.Progressing("Creature:1", 5, reached: true, Start.AddSeconds(2));

        // Off doing something else, then back from further away than it ever stood.
        Assert.True(headway.Progressing("Creature:1", 18, reached: false, Start.AddSeconds(10)));
        Assert.True(headway.Progressing("Creature:1", 12, reached: false, Start.AddSeconds(14)));
    }

    [Fact]
    public void ABotThatLeavesAndComesBackWithoutEverReachingAGoal_StillGivesItUp()
    {
        var headway = new WalkHeadway();
        headway.Progressing("Creature:1", 10, reached: false, Start);

        Assert.False(headway.Progressing("Creature:1", 18, reached: false, Start.AddSeconds(10)));
    }
}
