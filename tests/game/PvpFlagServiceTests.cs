namespace Ubiquitous.Game.Tests;

public sealed class PvpFlagServiceTests
{
    [Fact]
    public void IsFlagged_FalseUntilSet()
    {
        var flags = new PvpFlagService();

        Assert.False(flags.IsFlagged("player:a"));
    }

    [Fact]
    public void SetFlagged_True_MakesTheFlagRead()
    {
        var flags = new PvpFlagService();

        var result = flags.SetFlagged("player:a", true);

        Assert.True(result);
        Assert.True(flags.IsFlagged("player:a"));
    }

    [Fact]
    public void SetFlagged_False_ClearsAPreviouslySetFlag()
    {
        var flags = new PvpFlagService();
        flags.SetFlagged("player:a", true);

        var result = flags.SetFlagged("player:a", false);

        Assert.False(result);
        Assert.False(flags.IsFlagged("player:a"));
    }

    [Fact]
    public void IsFlaggedPair_TrueOnlyWhenBothSidesAreFlagged()
    {
        var flags = new PvpFlagService();
        flags.SetFlagged("player:a", true);

        Assert.False(flags.IsFlaggedPair("player:a", "player:b"));

        flags.SetFlagged("player:b", true);

        Assert.True(flags.IsFlaggedPair("player:a", "player:b"));
        Assert.True(flags.IsFlaggedPair("player:b", "player:a"));
    }

    [Fact]
    public void IsFlaggedPair_FalseForTheSamePlayerEvenIfFlagged()
    {
        var flags = new PvpFlagService();
        flags.SetFlagged("player:a", true);

        Assert.False(flags.IsFlaggedPair("player:a", "player:a"));
    }

    [Fact]
    public void IsFlaggedPair_StopsBeingTrueOnceEitherSideUnflags()
    {
        var flags = new PvpFlagService();
        flags.SetFlagged("player:a", true);
        flags.SetFlagged("player:b", true);
        Assert.True(flags.IsFlaggedPair("player:a", "player:b"));

        flags.SetFlagged("player:a", false);

        Assert.False(flags.IsFlaggedPair("player:a", "player:b"));
    }
}
