
namespace Ubiquitous.Game.Tests;

public sealed class TargetingServiceTests
{
    [Fact]
    public void Select_AcceptsVisibleTargetInRange()
    {
        var result = new TargetingService().Select("Creature:7", 0, 0, 0,
            [new TargetCandidate("Creature:7", 3, 0, 4)]);

        Assert.True(result.Accepted);
    }

    [Fact]
    public void Select_RejectsUnknownAndDistantTargets()
    {
        var service = new TargetingService();

        Assert.False(service.Select("Creature:8", 0, 0, 0, [new TargetCandidate("Creature:7", 0, 0, 0)]).Accepted);
        Assert.False(service.Select("Creature:7", 0, 0, 0, [new TargetCandidate("Creature:7", 100, 0, 0)]).Accepted);
    }
}
