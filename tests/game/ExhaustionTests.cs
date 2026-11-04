namespace Ubiquitous.Game.Tests;

public sealed class ExhaustionTests
{
    private static readonly DateTimeOffset Evening = new(2026, 11, 3, 23, 50, 0, TimeSpan.Zero);

    [Fact]
    public void WhatWasGainedTodayIsKept()
    {
        Assert.Equal(12, Exhaustion.Today(12, Exhaustion.DayOf(Evening), Evening.AddMinutes(5)));
    }

    [Fact]
    public void TheDailyResetEmptiesIt()
    {
        Assert.Equal(0, Exhaustion.Today(12, Exhaustion.DayOf(Evening), Evening.AddMinutes(15)));
        Assert.Equal(0, Exhaustion.Today(12, raisedOn: null, Evening));
    }

    [Theory]
    [InlineData(59, 0, LegendRefusal.TooLow)]
    [InlineData(60, 99, null)]
    [InlineData(60, 100, LegendRefusal.Exhausted)]
    public void LegendCopiesWantLevelSixtyAndRoomToTire(int level, int exhaustion, LegendRefusal? expected) =>
        Assert.Equal(expected, Exhaustion.WhyNotLegend(level, exhaustion));

    [Fact]
    public void ItNeverPassesOneHundred()
    {
        Assert.Equal(Exhaustion.Max, Exhaustion.Raise(95, 9));
    }

    [Fact]
    public void ARaisedLimitLetsAPlayerTireFurtherBeforeBeingTurnedAway()
    {
        Assert.Equal(104, Exhaustion.Raise(95, 9, limit: 150));
        Assert.Equal(150, Exhaustion.Raise(145, 9, limit: 150));
        Assert.Null(Exhaustion.WhyNotLegend(60, 120, limit: 150));
        Assert.Equal(LegendRefusal.Exhausted, Exhaustion.WhyNotLegend(60, 150, limit: 150));
    }
}
