namespace Ubiquitous.Game.Tests;

public sealed class GameRatesTests
{
    [Fact]
    public void NormalRatesChangeNothing()
    {
        var rates = GameRates.Normal;

        Assert.Equal(55, rates.ScaleXp(55));
        Assert.Equal(12, rates.ScaleDamage(12, dealtByPlayer: true));
        Assert.Equal(12, rates.ScaleDamage(12, dealtByPlayer: false));
        Assert.Equal(340, rates.ScaleCreatureHealth(340));
        Assert.Equal(Exhaustion.Max, rates.ExhaustionLimit);
    }

    [Theory]
    [InlineData(1.5, 55, 83)]
    [InlineData(0.5, 55, 28)]
    [InlineData(0, 55, 0)]
    [InlineData(10, 55, 550)]
    public void XpIsMultipliedAndRoundedToTheNearestPoint(double rate, int gained, int expected) =>
        Assert.Equal(expected, new GameRates(Xp: rate).ScaleXp(gained));

    [Fact]
    public void PlayerAndCreatureDamageScaleApart()
    {
        var rates = new GameRates(PlayerDamage: 2.0, CreatureDamage: 0.5);

        Assert.Equal(24, rates.ScaleDamage(12, dealtByPlayer: true));
        Assert.Equal(6, rates.ScaleDamage(12, dealtByPlayer: false));
    }

    [Fact]
    public void ACreatureKeepsAtLeastOneHealth()
    {
        Assert.Equal(1, new GameRates(CreatureHealth: 0.001).ScaleCreatureHealth(10));
        Assert.Equal(25, new GameRates(CreatureHealth: 2.5).ScaleCreatureHealth(10));
    }

    [Fact]
    public void AHugeRateStopsAtTheLargestWholeNumber()
    {
        Assert.Equal(int.MaxValue, new GameRates(Xp: 1e12).ScaleXp(1000));
    }
}
