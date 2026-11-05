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

    [Fact]
    public async Task ABotDuellingAPlayerWhoSneaksBeforeTheFight_NeverFindsThemToFightOrFollow()
    {
        const uint sneak = 980;
        var content = PlayerbotWorldTests.QuestContent() with
        {
            Spells = new Dictionary<uint, Ubiquitous.Data.SpellDefinition>(Ubiquitous.Game.GameContent.Development.Spells)
            {
                [sneak] = new(sneak, "spell-980", 0, 0, 0f, PowerCost: 0, AuraStealth: true, DurationMs: 600_000),
            },
            StartingSpells = [.. Ubiquitous.Game.GameContent.Development.StartingSpells, sneak],
        };
        var world = new BotTestWorld(content, [], PlayerbotWorldTests.OneBot(), new DevelopmentAccountStore("hero", "secret"));
        await world.RunUntilAsync(() => world.Bots is [{ Playing: true }], mostTicks: 50);
        var bot = world.Bots[0];
        var player = await TestPlayer.EnterAsync(world, "hero", "secret", "Hero");
        var heroId = player.Received.Select(m => System.Text.Json.JsonDocument.Parse(m).RootElement)
            .First(r => r.GetProperty("type").GetString() == WebMessageTypes.WorldEntered)
            .GetProperty("payload").GetProperty("self").GetString()!;
        await player.SendAsync(WebMessageTypes.DuelChallenge, new { targetId = bot.View.SelfId });
        await world.RunUntilAsync(() =>
        {
            player.Collect();
            return player.Received.Any(message => message.Contains(WebMessageTypes.DuelStarted, StringComparison.Ordinal));
        }, mostTicks: 20);

        await player.SendAsync(WebMessageTypes.SpellRequest, new { spellId = sneak, targetId = heroId });
        await world.RunUntilAsync(() => false, mostTicks: 5);
        player.Collect();
        player.Received.Clear();
        var heroAt = new Ubiquitous.Common.Position(2, 7, 55);
        var gapBefore = Gap(bot.View.Position, heroAt);
        await world.RunUntilAsync(() => false, mostTicks: 30);
        player.Collect();

        Assert.False(bot.View.Units.ContainsKey(heroId), "the bot still sees the sneaking player");
        Assert.DoesNotContain(player.Received, m => m.Contains(WebMessageTypes.AttackSwingResult, StringComparison.Ordinal)
            && m.Contains($"\"attackerId\":\"{bot.View.SelfId}\"", StringComparison.Ordinal));
        Assert.DoesNotContain(player.Received, m => m.Contains(WebMessageTypes.SpellOutcome, StringComparison.Ordinal)
            && m.Contains($"\"casterId\":\"{bot.View.SelfId}\"", StringComparison.Ordinal));
        Assert.True(Gap(bot.View.Position, heroAt) >= gapBefore - 3f, $"the bot closed in to {bot.View.Position}");
    }

    private static float Gap(Ubiquitous.Common.Position a, Ubiquitous.Common.Position b) =>
        MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}
