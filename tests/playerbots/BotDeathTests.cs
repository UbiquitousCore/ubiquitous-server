using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Playerbots.Tests;

public sealed class BotDeathTests
{
    private const uint Brute = 7090;

    // A creature beside the human start point that kills a level 1 body in a blow or two.
    private static readonly GameContent Deadly = PlayerbotWorldTests.QuestContent() with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(PlayerbotWorldTests.QuestContent().Creatures)
        {
            [Brute] = new(Brute, "brute", 10, 5000, MinDamage: 400, MaxDamage: 400, NoticeDistance: 40,
                FactionType: UnitFactionTypes.Hostile),
        },
    };

    private static readonly CreatureSpawnDefinition[] BruteAtTheStart = [new(1, 1, Brute, 2f, 7f, 60f, 0f)];

    [Fact]
    public async Task ABotOnItsOwn_ComesBackAfterDying()
    {
        var world = new BotTestWorld(Deadly, BruteAtTheStart, PlayerbotWorldTests.OneBot());

        await world.RunUntilAsync(() => world.Bots is [{ Playing: true, View.Dead: true }], mostTicks: 200);
        Assert.True(world.Bots[0].View.Dead, "the bot never died");
        await world.RunUntilAsync(() => !world.Bots[0].View.Dead, mostTicks: 100);

        Assert.False(world.Bots[0].View.Dead, "the bot stayed dead");
    }

    [Fact]
    public async Task ABotInAPlayersParty_ComesBackAfterDying_AndThePlayerSeesItStandAgain()
    {
        var world = new BotTestWorld(Deadly, BruteAtTheStart, PlayerbotWorldTests.OneBot(),
            new DevelopmentAccountStore("hero", "secret"));
        await world.RunUntilAsync(() => world.Bots is [{ Playing: true }], mostTicks: 50);
        var bot = world.Bots[0];
        var player = await TestPlayer.EnterAsync(world, "hero", "secret", "Hero");
        await player.SendAsync(WebMessageTypes.PartyInvite, new { networkId = bot.View.SelfId });

        await world.RunUntilAsync(() => bot.View.Dead, mostTicks: 200);
        Assert.True(bot.View.InParty, "the bot never joined");
        Assert.True(bot.View.Dead, "the bot never died");
        player.Collect();
        player.Received.Clear();
        await world.RunUntilAsync(() => !bot.View.Dead, mostTicks: 100);
        await world.RunUntilAsync(() => false, mostTicks: 3);
        player.Collect();

        Assert.False(bot.View.Dead, "the bot stayed dead");
        Assert.Contains(player.Received, message => message.Contains("\"type\":\"entity.spawned\"", StringComparison.Ordinal)
            && message.Contains(bot.View.SelfId!, StringComparison.Ordinal));
    }
}
