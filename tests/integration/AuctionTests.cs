using Ubiquitous.Common;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class AuctionTests
{
    [Fact]
    public async Task FileAuctionStore_RoundTripsAPersistedListing()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-auction-{Guid.NewGuid():N}");
        try
        {
            var store = new FileAuctionStore(directory);
            var listing = new PersistedAuctionListing("auction-1", "char-a", "Ayla", 100, 1, 250, 1000, 1000 + 7200);
            await store.SaveAllAsync([listing]);

            var loaded = Assert.Single(await store.ListAllAsync());
            Assert.Equal("Ayla", loaded.SellerName);
            Assert.Equal(250, loaded.StartPrice);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ListBrowseAndCancel_WorkEndToEndAndSurviveARestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-auction-session-{Guid.NewGuid():N}");
        try
        {
            var auctionStore = new FileAuctionStore(directory);
            var (ayla, _) = await JoinAsync(auctionStore, "s1", "Ayla");
            var (borin, _) = await JoinAsync(auctionStore, "b1", "Borin");

            var listed = await SendAsync(ayla, WebMessageTypes.AuctionList, 5,
                new AuctionListPayload(new StorageSlotPayload("bag", 0), "starter-item", 250, 2));
            Assert.Contains(listed, r => r.Contains("\"type\":\"auction.listed\"") && r.Contains("\"ok\":true"));
            Assert.Contains(listed, r => r.Contains("\"type\":\"inventory.itemRemoved\"") && r.Contains("starter-item"));
            var listingId = ExtractId(Assert.Single(listed, r => r.Contains("\"type\":\"auction.listed\"")));

            var moveGone = await SendAsync(ayla, WebMessageTypes.InventoryMove, 6,
                new { from = new { area = "bag", index = 0 }, to = new { area = "bag", index = 0 } });
            Assert.Contains(moveGone, m => m.Contains("\"type\":\"protocol.error\""));

            var browsed = await SendAsync(borin, WebMessageTypes.AuctionBrowse, 5, new AuctionBrowsePayload());
            var listingsReply = Assert.Single(browsed, r => r.Contains("\"type\":\"auction.listings\""));
            Assert.Contains("\"sellerName\":\"Ayla\"", listingsReply);
            Assert.Contains("\"itemEntry\":100", listingsReply);
            Assert.Contains("\"startPrice\":250", listingsReply);

            var byName = await SendAsync(borin, WebMessageTypes.AuctionBrowse, 6, new AuctionBrowsePayload("item-100"));
            Assert.Contains(listingId, Assert.Single(byName));

            Assert.Contains("\"total\":1", listingsReply);
            Assert.Contains("\"offset\":0", listingsReply);

            var wrongKind = await SendAsync(borin, WebMessageTypes.AuctionBrowse, 10,
                new AuctionBrowsePayload(Kinds: [new AuctionKindPayload(ItemClass: 99)]));
            Assert.Contains("\"listings\":[]", Assert.Single(wrongKind));

            var pastFirstPage = await SendAsync(borin, WebMessageTypes.AuctionBrowse, 11,
                new AuctionBrowsePayload(Offset: 1, Limit: 10));
            var pagedReply = Assert.Single(pastFirstPage);
            Assert.Contains("\"listings\":[]", pagedReply);
            Assert.Contains("\"total\":1", pagedReply);

            var miss = await SendAsync(borin, WebMessageTypes.AuctionBrowse, 7, new AuctionBrowsePayload("no such item"));
            Assert.Contains("\"listings\":[]", Assert.Single(miss));

            var foreignCancel = await SendAsync(borin, WebMessageTypes.AuctionCancel, 8, new AuctionCancelPayload(listingId));
            var foreignReply = Assert.Single(foreignCancel);
            Assert.Contains("\"type\":\"auction.cancelled\"", foreignReply);
            Assert.Contains("\"ok\":false", foreignReply);
            Assert.Contains("your own", foreignReply);

            var (aylaAgain, _) = await JoinAsync(auctionStore, "s1", "Ayla");
            var rebrowsed = await SendAsync(aylaAgain, WebMessageTypes.AuctionBrowse, 5, new AuctionBrowsePayload());
            Assert.Contains(listingId, Assert.Single(rebrowsed));

            var cancelled = await SendAsync(aylaAgain, WebMessageTypes.AuctionCancel, 6, new AuctionCancelPayload(listingId));
            Assert.Contains(cancelled, r => r.Contains("\"type\":\"auction.cancelled\"") && r.Contains("\"ok\":true"));
            Assert.Contains(cancelled, r => r.Contains("\"type\":\"inventory.itemAdded\"") && r.Contains("\"entry\":100"));

            var moveBack = await SendAsync(aylaAgain, WebMessageTypes.InventoryMove, 7,
                new { from = new { area = "bag", index = 0 }, to = new { area = "bag", index = 0 } });
            Assert.Contains(moveBack, m => m.Contains("\"type\":\"inventory.itemMoved\""));

            var empty = await SendAsync(borin, WebMessageTypes.AuctionBrowse, 9, new AuctionBrowsePayload());
            Assert.Contains("\"listings\":[]", Assert.Single(empty));

            var secondCancel = await SendAsync(aylaAgain, WebMessageTypes.AuctionCancel, 8, new AuctionCancelPayload(listingId));
            Assert.Contains("\"ok\":false", Assert.Single(secondCancel));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task List_WithAFreePrice_IsRefusedAndLeavesTheBagUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-auction-price-{Guid.NewGuid():N}");
        try
        {
            var (ayla, _) = await JoinAsync(new FileAuctionStore(directory), "s2", "Ayla");

            var listed = await SendAsync(ayla, WebMessageTypes.AuctionList, 5,
                new AuctionListPayload(new StorageSlotPayload("bag", 0), "starter-item", 0, 2));
            var listedReply = Assert.Single(listed);
            Assert.Contains("\"type\":\"auction.listed\"", listedReply);
            Assert.Contains("\"ok\":false", listedReply);

            var moveReply = await SendAsync(ayla, WebMessageTypes.InventoryMove, 6,
                new { from = new { area = "bag", index = 0 }, to = new { area = "bag", index = 0 } });
            Assert.Contains(moveReply, m => m.Contains("\"type\":\"inventory.itemMoved\"") && m.Contains("starter-item"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Cancel_WithAFullBag_RefusesAndKeepsTheListing()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-auction-fullbag-{Guid.NewGuid():N}");
        try
        {
            var auctionStore = new FileAuctionStore(directory);
            var seeder = new AuctionService(auctionStore, new MailService(new InMemoryMailStore()));
            var listed = await seeder.ListAsync("Ayla", "char-s3", 100, 1, 250, 7200);
            Assert.Equal(AuctionResultCode.Ok, listed.Code);

            var fullBag = new FixedInventoryStore(Enumerable.Range(0, 12)
                .Select(slot => new PersistedInventoryItem($"filler-{slot}", 900 + slot, 1, "bag", slot)).ToArray());
            var (ayla, _) = await JoinAsync(auctionStore, "s3", "Ayla", fullBag);

            var cancelled = await SendAsync(ayla, WebMessageTypes.AuctionCancel, 5,
                new AuctionCancelPayload(listed.Listing!.Id));
            var cancelledReply = Assert.Single(cancelled);
            Assert.Contains("\"type\":\"auction.cancelled\"", cancelledReply);
            Assert.Contains("\"ok\":false", cancelledReply);
            Assert.Contains("bags are full", cancelledReply);

            var browsed = await SendAsync(ayla, WebMessageTypes.AuctionBrowse, 6, new AuctionBrowsePayload());
            Assert.Contains(listed.Listing.Id, Assert.Single(browsed));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class FixedInventoryStore(IReadOnlyList<PersistedInventoryItem> items) : IInventoryStore
    {
        public Task<IReadOnlyList<PersistedInventoryItem>> ListAsync(string characterGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(items);

        public Task SaveAsync(string characterGuid, IReadOnlyList<PersistedInventoryItem> items, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static async Task<(PlaySession Session, List<string> Inbox)> JoinAsync(
        FileAuctionStore auctionStore, string guidSuffix, string name,
        IInventoryStore? inventoryStore = null)
    {
        var characters = new InMemoryCharacterStore([Character(guidSuffix, name)]);
        var session = TestSessions.Create(new DevelopmentAccountStore(guidSuffix, "p"), characters,
            auctionStore: auctionStore, inventoryStore: inventoryStore);
        var inbox = new List<string>();
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.AccountLogin, 0, new LoginRequest(guidSuffix, "p"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name))));

        return (session, inbox);
    }

    private static async Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload)));

    private static string ExtractId(string listedJson)
    {
        const string marker = "\"id\":\"";
        var start = listedJson.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = listedJson.IndexOf('"', start);
        return listedJson[start..end];
    }

    private static CharacterRecord Character(string guidSuffix, string name) => new(
        $"char-{guidSuffix}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);
}
