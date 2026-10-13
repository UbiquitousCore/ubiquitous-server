using Ubiquitous.Data;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class AuctionBiddingTests
{
    [Fact]
    public async Task BidOutbidAndBuyout_SettleEntirelyThroughMail()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-auction-bid-{Guid.NewGuid():N}");
        try
        {
            var auctionStore = new FileAuctionStore(directory);
            var characters = new InMemoryCharacterStore(
            [
                Character("char-s1", 1, "Ayla"),
                Character("char-b1", 2, "Borin"),
                Character("char-c1", 3, "Ceri"),
            ]);
            var mailboxSpawn = new[] { new StaticObjectSpawnDefinition(1, 13, 9102, 2, 2, 4, 0) };
            var mailStore = new InMemoryMailStore();

            var ayla = await JoinAsync(auctionStore, mailStore, characters, mailboxSpawn, "ayla-dev", 1, "Ayla");
            var borin = await JoinAsync(auctionStore, mailStore, characters, mailboxSpawn, "borin-dev", 2, "Borin");
            var ceri = await JoinAsync(auctionStore, mailStore, characters, mailboxSpawn, "ceri-dev", 3, "Ceri");

            var listed = await SendAsync(ayla, WebMessageTypes.AuctionList, 5,
                new AuctionListPayload(new StorageSlotPayload("bag", 0), "starter-item", 100, 2, 500));
            var listedReply = Assert.Single(listed, r => r.Contains("\"type\":\"auction.listed\""));
            Assert.Contains("\"ok\":true", listedReply);
            var listingId = ExtractId(listedReply);

            var bid = await SendAsync(borin, WebMessageTypes.AuctionBid, 5, new AuctionBidPayload(listingId, 150));
            Assert.Contains(bid, r => r.Contains("\"type\":\"auction.bidPlaced\"") && r.Contains("\"ok\":true") && r.Contains("\"currentBid\":150"));
            Assert.Contains(bid, r => r.Contains("\"type\":\"player.updated\"") && r.Contains("\"gold\":4850"));

            var outbid = await SendAsync(ceri, WebMessageTypes.AuctionBid, 5, new AuctionBidPayload(listingId, 200));
            Assert.Contains(outbid, r => r.Contains("\"currentBid\":200"));

            var borinInbox = await OpenInboxAsync(borin);
            Assert.Contains("\"gold\":150", borinInbox);
            var borinClaim = await SendAsync(borin, WebMessageTypes.MailClaim, 8, new MailIdPayload(ExtractId(borinInbox)));
            Assert.Contains(borinClaim, r => r.Contains("\"type\":\"mail.claimed\"") && r.Contains("\"ok\":true"));
            Assert.Contains(borinClaim, r => r.Contains("\"gold\":5000"));

            var bought = await SendAsync(ceri, WebMessageTypes.AuctionBuyout, 6, new AuctionBuyoutPayload(listingId));
            Assert.Contains(bought, r => r.Contains("\"type\":\"auction.bought\"") && r.Contains("\"ok\":true") && r.Contains("\"price\":500"));
            Assert.Contains(bought, r => r.Contains("\"gold\":4500"));

            var empty = await SendAsync(ayla, WebMessageTypes.AuctionBrowse, 6, new AuctionBrowsePayload());
            Assert.Contains("\"listings\":[]", Assert.Single(empty));

            var ceriInbox = await OpenInboxAsync(ceri);
            var ceriClaim = await SendAsync(ceri, WebMessageTypes.MailClaim, 8, new MailIdPayload(ExtractId(ceriInbox)));
            Assert.Contains(ceriClaim, r => r.Contains("\"type\":\"inventory.itemAdded\"") && r.Contains("\"entry\":100"));
            Assert.Contains(ceriClaim, r => r.Contains("\"gold\":4500"));

            var aylaInbox = await OpenInboxAsync(ayla);
            Assert.Contains("\"gold\":500", aylaInbox);
            var aylaClaim = await SendAsync(ayla, WebMessageTypes.MailClaim, 8, new MailIdPayload(ExtractId(aylaInbox)));
            Assert.Contains(aylaClaim, r => r.Contains("\"gold\":5500"));

            var lateBid = await SendAsync(borin, WebMessageTypes.AuctionBid, 9, new AuctionBidPayload(listingId, 600));
            Assert.Contains("\"ok\":false", Assert.Single(lateBid));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task TwoSessionsBuyingOutOneListingTogether_OnlyOneGetsIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-auction-race-{Guid.NewGuid():N}");
        try
        {
            var auctionStore = new FileAuctionStore(directory);
            var characters = new InMemoryCharacterStore(
            [
                Character("char-s1", 1, "Ayla"),
                Character("char-b1", 2, "Borin"),
                Character("char-c1", 3, "Ceri"),
            ]);
            var mailboxSpawn = new[] { new StaticObjectSpawnDefinition(1, 13, 9102, 2, 2, 4, 0) };
            var mailStore = new InMemoryMailStore();
            var world = new SharedWorld(new SessionStores(
                new AccountAuthenticator(new DevelopmentAccountStore("ayla-dev", "p", 1)), characters, Mail: mailStore, Auction: auctionStore));

            var ayla = await JoinAsync(auctionStore, mailStore, characters, mailboxSpawn, "ayla-dev", 1, "Ayla", world);
            var borin = await JoinAsync(auctionStore, mailStore, characters, mailboxSpawn, "borin-dev", 2, "Borin", world);
            var ceri = await JoinAsync(auctionStore, mailStore, characters, mailboxSpawn, "ceri-dev", 3, "Ceri", world);
            var listed = await SendAsync(ayla, WebMessageTypes.AuctionList, 5,
                new AuctionListPayload(new StorageSlotPayload("bag", 0), "starter-item", 100, 2, 500));
            var listingId = ExtractId(Assert.Single(listed, r => r.Contains("\"type\":\"auction.listed\"")));

            var replies = await Task.WhenAll(
                SendAsync(borin, WebMessageTypes.AuctionBuyout, 6, new AuctionBuyoutPayload(listingId)),
                SendAsync(ceri, WebMessageTypes.AuctionBuyout, 6, new AuctionBuyoutPayload(listingId)));

            var bought = replies.SelectMany(reply => reply)
                .Where(reply => reply.Contains("\"type\":\"auction.bought\"") && reply.Contains("\"ok\":true"));
            Assert.Single(bought);
            var sellerMail = await mailStore.ListAsync("char-s1");
            Assert.Single(sellerMail, message => message.Subject == "Auction sold");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SelfBid_MissingBuyout_LowBid_AndShortGold_AreAllRefusedWithoutDeduction()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-auction-selfbid-{Guid.NewGuid():N}");
        try
        {
            var auctionStore = new FileAuctionStore(directory);
            var characters = new InMemoryCharacterStore(
            [
                Character("char-s2", 1, "Ayla"),
                Character("char-b2", 2, "Borin"),
            ]);
            var mailboxSpawn = new[] { new StaticObjectSpawnDefinition(1, 13, 9102, 2, 2, 4, 0) };
            var mailStore = new InMemoryMailStore();

            var ayla = await JoinAsync(auctionStore, mailStore, characters, mailboxSpawn, "ayla-dev", 1, "Ayla");
            var borin = await JoinAsync(auctionStore, mailStore, characters, mailboxSpawn, "borin-dev", 2, "Borin");

            var listed = await SendAsync(ayla, WebMessageTypes.AuctionList, 5,
                new AuctionListPayload(new StorageSlotPayload("bag", 0), "starter-item", 100, 2));
            var listingId = ExtractId(Assert.Single(listed, r => r.Contains("\"type\":\"auction.listed\"")));

            var selfBid = await SendAsync(ayla, WebMessageTypes.AuctionBid, 6, new AuctionBidPayload(listingId, 150));
            var selfBidReply = Assert.Single(selfBid);
            Assert.Contains("\"ok\":false", selfBidReply);
            Assert.Contains("your own", selfBidReply);

            var noBuyout = await SendAsync(borin, WebMessageTypes.AuctionBuyout, 6, new AuctionBuyoutPayload(listingId));
            var noBuyoutReply = Assert.Single(noBuyout);
            Assert.Contains("\"ok\":false", noBuyoutReply);
            Assert.Contains("no buyout", noBuyoutReply);

            var lowBid = await SendAsync(borin, WebMessageTypes.AuctionBid, 7, new AuctionBidPayload(listingId, 100));
            Assert.Contains("\"ok\":false", Assert.Single(lowBid));
            var poorBid = await SendAsync(borin, WebMessageTypes.AuctionBid, 8, new AuctionBidPayload(listingId, 6000));
            var poorBidReply = Assert.Single(poorBid);
            Assert.Contains("\"ok\":false", poorBidReply);
            Assert.Contains("much gold", poorBidReply);

            var realBid = await SendAsync(borin, WebMessageTypes.AuctionBid, 9, new AuctionBidPayload(listingId, 150));
            Assert.Contains(realBid, r => r.Contains("\"ok\":true"));
            Assert.Contains(realBid, r => r.Contains("\"gold\":4850"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task<string> OpenInboxAsync(PlaySession session)
    {
        var opened = await SendAsync(session, WebMessageTypes.MailOpen, 7,
            new { networkId = "StaticObject:1" });
        Assert.Contains(opened, r => r.Contains("\"type\":\"mail.opened\"") && r.Contains("\"open\":true"));
        return Assert.Single(opened, r => r.Contains("\"type\":\"mail.inbox\""));
    }

    private static async Task<PlaySession> JoinAsync(
        FileAuctionStore auctionStore, InMemoryMailStore mailStore, InMemoryCharacterStore characters,
        StaticObjectSpawnDefinition[] mailboxSpawn, string accountName, int accountId, string characterName,
        SharedWorld? world = null)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(accountName, "p", accountId),
            characters, staticObjectSpawns: mailboxSpawn, mailStore: mailStore, auctionStore: auctionStore,
            world: world);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.AccountLogin, 0, new LoginRequest(accountName, "p"))));
        var entered = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(characterName))));
        Assert.DoesNotContain(entered, r => r.Contains("protocol.error"));
        return session;
    }

    private static async Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload)));

    private static string ExtractId(string json)
    {
        const string marker = "\"id\":\"";
        var start = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = json.IndexOf('"', start);
        return json[start..end];
    }

    private static CharacterRecord Character(string guid, int accountId, string name) => new(
        guid, accountId, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);
}
