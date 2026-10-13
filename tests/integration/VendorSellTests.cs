using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class VendorSellTests
{
    private const uint Pelt = 4200;
    private const uint Keepsake = 4300;

    private static GameContent Content() => TestSessions.WithTestPurse(GameContent.Development) with
    {
        Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
        {
            [Pelt] = new(Pelt, "item-4200", Class: 15, SubClass: 0, DisplayInfoId: 0, BuyPrice: 40, SellPrice: 10),
            [Keepsake] = new(Keepsake, "item-4300", Class: 12, SubClass: 0, DisplayInfoId: 0, BuyPrice: 0, SellPrice: 0),
        },
        StartingItems =
        [
            new StartingItemStack(100, 1),
            new StartingItemStack(Pelt, 5),
            new StartingItemStack(Keepsake, 1),
        ],
    };

    private static async Task<PlaySession> EnterAsync()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            content: Content());
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static Task<IReadOnlyList<string>> SellAsync(PlaySession session, int sequence, int slot, string itemId) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorSell, sequence,
            new { networkId = "Creature:1", from = new { area = "bag", index = slot }, itemId })));

    [Fact]
    public async Task AVendorBuysAStackItDoesNotStock_PayingForEveryOneInIt()
    {
        var session = await EnterAsync();

        var response = await SellAsync(session, 2, 1, "starter-item-1");

        Assert.Contains(response, m => m.Contains("\"type\":\"npc.sellResult\"") && m.Contains("\"code\":\"ok\""));
        Assert.Contains(response, m => m.Contains("\"type\":\"inventory.itemRemoved\"") && m.Contains("starter-item-1"));
        Assert.Contains(response, m => m.Contains("\"type\":\"player.updated\"") && m.Contains("\"gold\":5050"));

        var again = await SellAsync(session, 3, 1, "starter-item-1");
        Assert.Contains(again, m => m.Contains("\"type\":\"npc.sellResult\"") && m.Contains("\"code\":\"itemNotInSlot\""));
        Assert.DoesNotContain(again, m => m.Contains("\"type\":\"player.updated\""));
    }

    [Fact]
    public async Task AnItemWithNoSellPrice_IsRefusedAndStaysInTheBag()
    {
        var session = await EnterAsync();

        var response = await SellAsync(session, 2, 2, "starter-item-2");

        var refusal = Assert.Single(response);
        Assert.Contains("\"type\":\"npc.sellResult\"", refusal);
        Assert.Contains("\"code\":\"cantSell\"", refusal);

        var again = await SellAsync(session, 3, 2, "starter-item-2");
        Assert.Contains("\"code\":\"cantSell\"", Assert.Single(again));
    }

    [Fact]
    public async Task ASlotThatDoesNotHoldTheNamedItem_IsRefusedWithoutPaying()
    {
        var session = await EnterAsync();

        var response = await SellAsync(session, 2, 0, "starter-item-1");

        Assert.Contains("\"code\":\"itemNotInSlot\"", Assert.Single(response));
    }
}
