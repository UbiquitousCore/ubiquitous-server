using Ubiquitous.Common;

namespace Ubiquitous.Game.Tests;

public sealed class MeleeReachTests
{
    [Fact]
    public void Reaches_APlayerOnABankJustAboveTheCreature()
    {
        Assert.True(MeleeReach.Reaches(new Position(0, 0, 0), new Position(2, 4, 0)));
    }

    [Fact]
    public void Reaches_NotAPlayerOnACliffTopOverhead()
    {
        Assert.False(MeleeReach.Reaches(new Position(0, 0, 0), new Position(0, 10, 0)));
    }

    [Fact]
    public void Reaches_NotBeyondMeleeRangeAcrossTheGround()
    {
        Assert.False(MeleeReach.Reaches(new Position(0, 0, 0), new Position(3.5f, 0, 0)));
    }
}
