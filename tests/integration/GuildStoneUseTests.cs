using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class GuildStoneUseTests
{
    private const uint StoneEntry = 3800;

    [Fact]
    public async Task UsingAGuildStone_AsksWhatToCallTheGuild()
    {
        var session = await SessionAsync(WithItem(
            new ItemDefinition(StoneEntry, "item-guild-stone", Class: 0, SubClass: 10, DisplayInfoId: 0)));

        var use = Assert.Single(await UseFirstBagSlotAsync(session));

        Assert.Contains("\"type\":\"guild.foundPrompt\"", use);
        Assert.Contains("\"entry\":3800", use);
    }

    [Fact]
    public async Task UsingAGuildStone_SpendsNothing()
    {
        var session = await SessionAsync(WithItem(
            new ItemDefinition(StoneEntry, "item-guild-stone", Class: 0, SubClass: 10, DisplayInfoId: 0)));

        var use = Assert.Single(await UseFirstBagSlotAsync(session));

        Assert.DoesNotContain("inventory.itemRemoved", use);
        Assert.DoesNotContain("inventory.stackChanged", use);
    }

    [Fact]
    public async Task AnOrdinaryConsumableWithNoUse_StillSaysSo()
    {
        var session = await SessionAsync(WithItem(
            new ItemDefinition(StoneEntry, "item-plain", Class: 0, SubClass: 0, DisplayInfoId: 0)));

        var use = Assert.Single(await UseFirstBagSlotAsync(session));

        Assert.Contains("cannot_use", use);
    }

    private static GameContent WithItem(ItemDefinition item) => GameContent.Development with
    {
        Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items) { [item.Id] = item },
        StartingItems = [new StartingItemStack(item.Id, 1)],
    };

    private static async Task<IReadOnlyList<string>> UseFirstBagSlotAsync(PlaySession session) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 2,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

    private static async Task<PlaySession> SessionAsync(GameContent content)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 12, [100, 200])]),
            [], [], content, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
