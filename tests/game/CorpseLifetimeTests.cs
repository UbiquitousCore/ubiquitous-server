using Ubiquitous.Game;

namespace Ubiquitous.Game.Tests;

public sealed class CorpseLifetimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ACorpseWithLootOutlastsAnEmptyOne()
    {
        Assert.Equal(Now.AddSeconds(150), CorpseLifetime.Until(Now, carriesLoot: true, inDungeon: false));
        Assert.Equal(Now.AddSeconds(30), CorpseLifetime.Until(Now, carriesLoot: false, inDungeon: false));
    }

    [Fact]
    public void ADungeonCorpseLastsAsLongAsTheInstance()
    {
        Assert.Equal(CorpseLifetime.UntilInstanceEnds, CorpseLifetime.Until(Now, carriesLoot: false, inDungeon: true));
        Assert.Equal(CorpseLifetime.UntilInstanceEnds, CorpseLifetime.Until(Now, carriesLoot: true, inDungeon: true));
    }

    [Fact]
    public void EmptyingACorpseShortensItButNothingLengthensIt()
    {
        var shared = new SharedMapState(13);
        shared.HoldCorpse(7, Now.AddSeconds(150));
        shared.HoldCorpse(7, Now.AddSeconds(40));
        Assert.True(shared.TryGetCorpseUntil(7, out var emptied));
        Assert.Equal(Now.AddSeconds(40), emptied);

        shared.HoldCorpse(7, Now.AddSeconds(150));
        Assert.True(shared.TryGetCorpseUntil(7, out var held));
        Assert.Equal(Now.AddSeconds(40), held);
    }
}
