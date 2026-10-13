namespace Ubiquitous.Game.Tests;

public sealed class DeathPenaltyServiceTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 0)]
    [InlineData(11, 500)]
    [InlineData(20, 500)]
    [InlineData(21, 1500)]
    [InlineData(30, 1500)]
    [InlineData(31, 3000)]
    [InlineData(40, 3000)]
    [InlineData(41, 5000)]
    [InlineData(50, 5000)]
    [InlineData(51, 8000)]
    [InlineData(60, 8000)]
    public void AmountFor_PaysAFlatAmountPerLevelBand(int level, int expected)
    {
        Assert.Equal(expected, DeathPenaltyService.AmountFor(level));
    }

    [Fact]
    public void GoldLost_TakesNothingFromAPlayerKilledByAnotherPlayer()
    {
        Assert.Equal(0, DeathPenaltyService.GoldLost(level: 60, carriedCurrency: 999999, killedByPlayer: true));
    }

    [Fact]
    public void GoldLost_TakesTheBandAmountFromAPlayerKilledByAnythingElse()
    {
        Assert.Equal(1500, DeathPenaltyService.GoldLost(level: 25, carriedCurrency: 999999, killedByPlayer: false));
    }

    [Fact]
    public void GoldLost_IsCappedAtWhatIsActuallyCarried()
    {
        Assert.Equal(120, DeathPenaltyService.GoldLost(level: 25, carriedCurrency: 120, killedByPlayer: false));
    }

    [Fact]
    public void GoldLost_CanEmptyAPurseExactly()
    {
        Assert.Equal(1500, DeathPenaltyService.GoldLost(level: 25, carriedCurrency: 1500, killedByPlayer: false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GoldLost_NeverOverdrawsAnEmptyPurse(int carried)
    {
        Assert.Equal(0, DeathPenaltyService.GoldLost(level: 60, carriedCurrency: carried, killedByPlayer: false));
    }
}
