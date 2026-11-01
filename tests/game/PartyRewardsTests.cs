namespace Ubiquitous.Game.Tests;

public sealed class PartyRewardsTests
{
    [Fact]
    public void ASoloKill_IsWorthTheOrdinaryKillXp()
    {
        var shares = PartyRewards.KillXpShares([new("a", 10)], creatureLevel: 10, elite: false);

        Assert.Equal(LevelingService.KillXp(10, 10), shares["a"]);
    }

    [Theory]
    [InlineData(2, 160)]
    [InlineData(3, 180)]
    [InlineData(4, 200)]
    public void APartyKill_IsWorthMoreInTotalTheMoreOfThemAreNearby(int size, int percent)
    {
        var earners = Enumerable.Range(0, size).Select(i => new PartyEarner($"p{i}", 20)).ToArray();

        var shares = PartyRewards.KillXpShares(earners, creatureLevel: 20, elite: false);

        var total = LevelingService.KillXp(20, 20) * percent / 100;
        Assert.All(shares.Values, share => Assert.Equal(total / size, share));
    }

    [Fact]
    public void EachShareFollowsTheMembersLevel_AndTheKillIsValuedAtTheHighestLevel()
    {
        var shares = PartyRewards.KillXpShares([new("high", 30), new("low", 10)], creatureLevel: 30, elite: false);

        var total = LevelingService.KillXp(30, 30) * 160 / 100;
        Assert.Equal(total * 30 / 40, shares["high"]);
        Assert.Equal(total * 10 / 40, shares["low"]);
    }

    [Fact]
    public void AnEliteIsWorthDoubleInAPartyToo()
    {
        var plain = PartyRewards.KillXpShares([new("a", 20), new("b", 20)], 20, elite: false);
        var elite = PartyRewards.KillXpShares([new("a", 20), new("b", 20)], 20, elite: true);

        Assert.Equal(plain["a"] * 2, elite["a"]);
    }

    [Theory]
    [InlineData(100, 1, 100, 0)]
    [InlineData(100, 3, 33, 1)]
    [InlineData(2, 4, 0, 2)]
    [InlineData(0, 2, 0, 0)]
    public void MoneyIsSplitEvenly_AndThePickerKeepsWhatIsLeftOver(int amount, int earners, int each, int extra) =>
        Assert.Equal(new MoneySplit(each, extra), PartyRewards.SplitMoney(amount, earners));
}
