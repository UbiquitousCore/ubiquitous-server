using Ubiquitous.Modules.Playerbots;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Playerbots.Tests;

public sealed class PlayerbotPartyTests
{
    [Fact]
    public async Task ABot_JoinsAPlayersPartyFollowsThemAndStaysWhenTold()
    {
        var world = new BotTestWorld(PlayerbotWorldTests.QuestContent(), [], PlayerbotWorldTests.OneBot(),
            new DevelopmentAccountStore("hero", "secret"));
        await world.RunUntilAsync(() => world.Bots is [{ Playing: true }], mostTicks: 50);
        var bot = world.Bots[0];
        var player = await TestPlayer.EnterAsync(world, "hero", "secret", "Hero");

        await player.SendAsync(WebMessageTypes.PartyInvite, new { networkId = bot.View.SelfId });
        await world.RunUntilAsync(() => bot.View.InParty, mostTicks: 20);
        Assert.True(bot.View.InParty);

        var z = 55f;
        await world.RunUntilAsync(() =>
        {
            if (z < 85f)
            {
                z += 2f;
                player.SendAsync(WebMessageTypes.PlayerMove,
                    new PlayerMovePayload(2, 7, z, 0, z < 85f, MovementState.Walking)).GetAwaiter().GetResult();
            }

            return z >= 85f && BotBrain.Flat(bot.View.Position, new Ubiquitous.Common.Position(2, 7, 85)) <= BotBrain.FollowSlack;
        }, mostTicks: 120);
        Assert.Equal(BotMode.Follow, bot.Brain.Mode);
        Assert.InRange(BotBrain.Flat(bot.View.Position, new Ubiquitous.Common.Position(2, 7, 85)), 0f, BotBrain.FollowSlack);

        await player.SendAsync(WebMessageTypes.PlayerChat, new ChatPayload("party", "stay"));
        await world.RunUntilAsync(() => bot.Brain.Mode == BotMode.Stay, mostTicks: 10);
        await world.RunUntilAsync(() => false, mostTicks: 3);
        player.Collect();

        Assert.Equal(BotMode.Stay, bot.Brain.Mode);
        Assert.Contains(player.Received, message => message.Contains("Staying here.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABot_AcceptsAPlayersDuelAndFightsIt()
    {
        var world = new BotTestWorld(PlayerbotWorldTests.QuestContent(), [], PlayerbotWorldTests.OneBot(),
            new DevelopmentAccountStore("hero", "secret"));
        await world.RunUntilAsync(() => world.Bots is [{ Playing: true }], mostTicks: 50);
        var bot = world.Bots[0];
        var player = await TestPlayer.EnterAsync(world, "hero", "secret", "Hero");

        await player.SendAsync(WebMessageTypes.DuelChallenge, new { targetId = bot.View.SelfId });
        await world.RunUntilAsync(() =>
        {
            player.Collect();
            return player.Received.Any(message => message.Contains(WebMessageTypes.AttackMeleeStart, StringComparison.Ordinal)
                && message.Contains($"\"attackerId\":\"{bot.View.SelfId}\"", StringComparison.Ordinal));
        }, mostTicks: 60);

        Assert.Contains(player.Received, message => message.Contains(WebMessageTypes.DuelStarted, StringComparison.Ordinal));
        Assert.Contains(player.Received, message => message.Contains(WebMessageTypes.AttackMeleeStart, StringComparison.Ordinal)
            && message.Contains($"\"attackerId\":\"{bot.View.SelfId}\"", StringComparison.Ordinal));
    }
}
