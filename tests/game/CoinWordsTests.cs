namespace Ubiquitous.Game.Tests;

public sealed class CoinWordsTests
{
    [Theory]
    [InlineData(0, "0 copper")]
    [InlineData(99, "99 copper")]
    [InlineData(100, "1 silver")]
    [InlineData(102, "1 silver 2 copper")]
    [InlineData(9999, "99 silver 99 copper")]
    [InlineData(10000, "1 gold")]
    [InlineData(10102, "1 gold 1 silver 2 copper")]
    public void Names_every_coin_a_sum_holds(int copper, string expected) =>
        Assert.Equal(expected, CoinWords.Of(copper));
}
