using Ubiquitous.Modules.Playerbots;

namespace Ubiquitous.Playerbots.Tests;

public sealed class BotCadenceTests
{
    private static bool[] Turns(int index, int every, bool inWorld, bool watched) =>
        [.. Enumerable.Range(0, 4).Select(turn => BotManager.PlaysThisTurn(turn, index, every, inWorld, () => watched))];

    [Fact]
    public void AnUnwatchedBotPlaysEveryOtherTurnOnItsOwnBeat()
    {
        Assert.Equal([true, false, true, false], Turns(index: 0, every: 2, inWorld: true, watched: false));
        Assert.Equal([false, true, false, true], Turns(index: 1, every: 2, inWorld: true, watched: false));
    }

    [Fact]
    public void AWatchedBotASigningInBotAndAnyBotWhenThrottlingIsOffPlayEveryTurn()
    {
        Assert.All(Turns(index: 0, every: 2, inWorld: true, watched: true), Assert.True);
        Assert.All(Turns(index: 0, every: 2, inWorld: false, watched: false), Assert.True);
        Assert.All(Turns(index: 0, every: 1, inWorld: true, watched: false), Assert.True);
    }

    [Fact]
    public async Task APlayerBesideABotIsSeenAsWatchingIt()
    {
        var world = new BotTestWorld(PlayerbotWorldTests.QuestContent(), PlayerbotWorldTests.QuestSpawns,
            PlayerbotWorldTests.OneBot(), new Ubiquitous.Persistence.DevelopmentAccountStore("watcher", "secret"));
        await world.RunUntilAsync(() => world.Bots is [{ Playing: true }], mostTicks: 50);
        var bot = Assert.Single(world.Bots);

        Assert.False(world.Hub.IsPlayerNear(bot.View.MapId, bot.View.Position, BotManager.WatchedWithin));
        await TestPlayer.EnterAsync(world, "watcher", "secret", "Watcher");

        Assert.True(world.Hub.IsPlayerNear(bot.View.MapId, bot.View.Position, BotManager.WatchedWithin));
    }
}
