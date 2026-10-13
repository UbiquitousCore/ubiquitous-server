using Ubiquitous.Common;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class TradeTests
{
    [Fact]
    public async Task ProposeAcceptOfferAndConfirm_MovesItemsAndGoldBothWaysAtOnce()
    {
        var trades = new TradeService();
        var hub = new PlaySessionHub();
        var (ayla, aylaInbox) = await JoinAsync(hub, trades, "a1", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, trades, "b1", "Borin");

        await SendAsync(ayla, WebMessageTypes.TradeRequest, 5, new { targetId = "player:char-b1" });
        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"trade.requested\"") && m.Contains("player:char-a1"));

        var acceptReply = await SendAsync(borin, WebMessageTypes.TradeAccept, 6, new { });
        Assert.Contains(acceptReply, m => m.Contains("\"type\":\"trade.started\"") && m.Contains("player:char-a1"));
        Assert.Contains(aylaInbox, m => m.Contains("\"type\":\"trade.started\"") && m.Contains("player:char-b1"));

        var aylaOfferReply = await SendAsync(ayla, WebMessageTypes.TradeOffer, 7,
            new { itemIds = new[] { "starter-item" }, gold = 100 });
        Assert.Contains(aylaOfferReply, m => m.Contains("\"type\":\"trade.updated\""));
        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"trade.updated\"")
            && m.Contains("\"opponentGold\":100") && m.Contains("\"entry\":100"));

        await SendAsync(borin, WebMessageTypes.TradeOffer, 8, new { itemIds = Array.Empty<string>(), gold = 50 });

        await SendAsync(ayla, WebMessageTypes.TradeConfirm, 9, new { confirmed = true });
        var confirmReply = await SendAsync(borin, WebMessageTypes.TradeConfirm, 10, new { confirmed = true });

        Assert.Contains(confirmReply, m => m.Contains("\"type\":\"inventory.itemAdded\"") && m.Contains("\"entry\":100"));
        Assert.Contains(confirmReply, m => m.Contains("\"type\":\"player.updated\"") && m.Contains("\"gold\":5050"));
        Assert.Contains(confirmReply, m => m.Contains("\"type\":\"trade.completed\"") && m.Contains("player:char-a1"));

        Assert.Contains(aylaInbox, m => m.Contains("\"type\":\"inventory.itemRemoved\"") && m.Contains("starter-item"));
        Assert.Contains(aylaInbox, m => m.Contains("\"type\":\"player.updated\"") && m.Contains("\"gold\":4950"));
        Assert.Contains(aylaInbox, m => m.Contains("\"type\":\"trade.completed\"") && m.Contains("player:char-b1"));
    }

    [Fact]
    public async Task Decline_RefusesTheTradeAndNotifiesTheProposer()
    {
        var trades = new TradeService();
        var hub = new PlaySessionHub();
        var (ayla, aylaInbox) = await JoinAsync(hub, trades, "a2", "Ayla");
        var (borin, _) = await JoinAsync(hub, trades, "b2", "Borin");

        await SendAsync(ayla, WebMessageTypes.TradeRequest, 5, new { targetId = "player:char-b2" });
        await SendAsync(borin, WebMessageTypes.TradeDecline, 6, new { });

        Assert.Contains(aylaInbox, m => m.Contains("\"type\":\"trade.declined\"") && m.Contains("player:char-b2"));

        var offerReply = await SendAsync(ayla, WebMessageTypes.TradeOffer, 7,
            new { itemIds = Array.Empty<string>(), gold = 0 });
        Assert.Contains("not currently trading", offerReply.Single());
    }

    [Fact]
    public async Task ChangingYourOfferAfterEitherSideConfirmed_CancelsTheWholeTrade()
    {
        var trades = new TradeService();
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, trades, "a3", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, trades, "b3", "Borin");

        await SendAsync(ayla, WebMessageTypes.TradeRequest, 5, new { targetId = "player:char-b3" });
        await SendAsync(borin, WebMessageTypes.TradeAccept, 6, new { });
        await SendAsync(borin, WebMessageTypes.TradeConfirm, 7, new { confirmed = true });

        var offerReply = await SendAsync(ayla, WebMessageTypes.TradeOffer, 8,
            new { itemIds = new[] { "starter-item" }, gold = 0 });

        Assert.Contains(offerReply, m => m.Contains("\"type\":\"trade.cancelled\"")
            && m.Contains("\"reason\":\"offer_changed_after_confirm\""));
        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"trade.cancelled\"")
            && m.Contains("\"reason\":\"offer_changed_after_confirm\""));

        var reconfirm = await SendAsync(borin, WebMessageTypes.TradeConfirm, 9, new { confirmed = true });
        Assert.Contains("not currently trading", reconfirm.Single());
    }

    [Fact]
    public async Task ExplicitCancel_TearsDownBothSidesAndLeavesInventoriesUntouched()
    {
        var trades = new TradeService();
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, trades, "a4", "Ayla");
        var (borin, borinInbox) = await JoinAsync(hub, trades, "b4", "Borin");

        await SendAsync(ayla, WebMessageTypes.TradeRequest, 5, new { targetId = "player:char-b4" });
        await SendAsync(borin, WebMessageTypes.TradeAccept, 6, new { });
        await SendAsync(ayla, WebMessageTypes.TradeOffer, 7,
            new { itemIds = new[] { "starter-item" }, gold = 20 });

        var cancelReply = await SendAsync(ayla, WebMessageTypes.TradeCancel, 8, new { });
        Assert.Contains(cancelReply, m => m.Contains("\"type\":\"trade.cancelled\"") && m.Contains("\"reason\":\"cancelled\""));
        Assert.Contains(borinInbox, m => m.Contains("\"type\":\"trade.cancelled\"") && m.Contains("\"reason\":\"cancelled\""));

        var moveReply = await SendAsync(ayla, WebMessageTypes.InventoryMove, 9,
            new { from = new { area = "bag", index = 0 }, to = new { area = "bag", index = 0 } });
        Assert.Contains(moveReply, m => m.Contains("\"type\":\"inventory.itemMoved\"") && m.Contains("starter-item"));
    }

    [Fact]
    public async Task BothConfirmingWithNoRoomOnTheRecipientSide_CancelsAndLosesNeitherSidesItems()
    {
        var trades = new TradeService();
        var hub = new PlaySessionHub();
        var (ayla, aylaInbox) = await JoinAsync(hub, trades, "a5", "Ayla");
        var fullBag = new FixedInventoryStore(Enumerable.Range(0, 12)
            .Select(slot => new PersistedInventoryItem($"filler-{slot}", 900 + slot, 1, "bag", slot)).ToArray());
        var (borin, borinInbox) = await JoinAsync(hub, trades, "b5", "Borin", fullBag);

        await SendAsync(ayla, WebMessageTypes.TradeRequest, 5, new { targetId = "player:char-b5" });
        await SendAsync(borin, WebMessageTypes.TradeAccept, 6, new { });
        await SendAsync(ayla, WebMessageTypes.TradeOffer, 7,
            new { itemIds = new[] { "starter-item" }, gold = 0 });
        await SendAsync(borin, WebMessageTypes.TradeOffer, 8, new { itemIds = Array.Empty<string>(), gold = 0 });

        await SendAsync(ayla, WebMessageTypes.TradeConfirm, 9, new { confirmed = true });
        var confirmReply = await SendAsync(borin, WebMessageTypes.TradeConfirm, 10, new { confirmed = true });

        Assert.Contains(confirmReply, m => m.Contains("\"type\":\"trade.cancelled\"")
            && m.Contains("\"reason\":\"not_enough_bag_space\""));
        Assert.Contains(aylaInbox, m => m.Contains("\"type\":\"trade.cancelled\"")
            && m.Contains("\"reason\":\"not_enough_bag_space\""));
        Assert.DoesNotContain(confirmReply, m => m.Contains("\"type\":\"inventory.itemAdded\""));
        Assert.DoesNotContain(confirmReply, m => m.Contains("\"type\":\"inventory.itemRemoved\""));
        Assert.DoesNotContain(aylaInbox, m => m.Contains("\"type\":\"inventory.itemRemoved\""));

        var aylaMove = await SendAsync(ayla, WebMessageTypes.InventoryMove, 11,
            new { from = new { area = "bag", index = 0 }, to = new { area = "bag", index = 0 } });
        Assert.Contains(aylaMove, m => m.Contains("\"type\":\"inventory.itemMoved\"") && m.Contains("starter-item"));

        var borinMove = await SendAsync(borin, WebMessageTypes.InventoryMove, 12,
            new { from = new { area = "bag", index = 0 }, to = new { area = "bag", index = 0 } });
        Assert.Contains(borinMove, m => m.Contains("\"type\":\"inventory.itemMoved\"") && m.Contains("filler-0"));
    }

    [Fact]
    public async Task RequestingATargetOnAnotherMap_IsRefusedAsNotVisible()
    {
        var trades = new TradeService();
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, trades, "a6", "Ayla", mapId: 13);
        await JoinAsync(hub, trades, "b6", "Borin", mapId: 14);

        var reply = await SendAsync(ayla, WebMessageTypes.TradeRequest, 5,
            new { targetId = "player:char-b6" });

        Assert.Contains(reply, m => m.Contains("\"type\":\"protocol.error\"") && m.Contains("not visible"));
    }

    private sealed class FixedInventoryStore(IReadOnlyList<PersistedInventoryItem> items) : IInventoryStore
    {
        public Task<IReadOnlyList<PersistedInventoryItem>> ListAsync(string characterGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(items);

        public Task SaveAsync(string characterGuid, IReadOnlyList<PersistedInventoryItem> items, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static async Task<(PlaySession Session, List<string> Inbox)> JoinAsync(
        PlaySessionHub hub, TradeService trades, string guidSuffix, string name,
        IInventoryStore? inventoryStore = null, ushort mapId = 13)
    {
        var characters = new InMemoryCharacterStore([Character(guidSuffix, name, mapId)]);
        var session = TestSessions.Create(new DevelopmentAccountStore(guidSuffix, "p"), characters,
            trades: trades, inventoryStore: inventoryStore);
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });

        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.AccountLogin, 0, new LoginRequest(guidSuffix, "p"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name))));

        return (session, inbox);
    }

    private static async Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload)));

    private static CharacterRecord Character(string guidSuffix, string name, ushort mapId) => new(
        $"char-{guidSuffix}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, mapId, 1, 2, 3, 4, [100, 200]);
}
