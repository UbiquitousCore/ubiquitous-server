using Ubiquitous.Common;

namespace Ubiquitous.Game.Tests;

public sealed class AuctionServiceTests
{
    private sealed class FakeAuctionStore : IAuctionStore
    {
        private List<PersistedAuctionListing> _listings = new();

        public Task<IReadOnlyList<PersistedAuctionListing>> ListAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PersistedAuctionListing> result = _listings.ToArray();
            return Task.FromResult(result);
        }

        public Task SaveAllAsync(IReadOnlyList<PersistedAuctionListing> listings, CancellationToken cancellationToken = default)
        {
            _listings = listings.ToList();
            return Task.CompletedTask;
        }
    }

    private static AuctionService NewService(out FakeAuctionStore store, out FakeMailStore mail, long now = 1_000)
    {
        store = new FakeAuctionStore();
        mail = new FakeMailStore();
        return new AuctionService(store, new MailService(mail, () => now), () => now);
    }

    private sealed class FakeMailStore : IMailStore
    {
        private readonly Dictionary<string, List<PersistedMailMessage>> _byRecipient = new(StringComparer.Ordinal);

        public IReadOnlyList<PersistedMailMessage> SentTo(string recipientGuid) =>
            _byRecipient.TryGetValue(recipientGuid, out var list) ? list.ToArray() : [];

        public Task<IReadOnlyList<PersistedMailMessage>> ListAsync(string recipientGuid, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PersistedMailMessage> result = SentTo(recipientGuid);
            return Task.FromResult(result);
        }

        public Task SaveAsync(string recipientGuid, IReadOnlyList<PersistedMailMessage> messages, CancellationToken cancellationToken = default)
        {
            _byRecipient[recipientGuid] = messages.ToList();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ListAsync_ValidRequest_PersistsTheListing()
    {
        var service = NewService(out var store, out _);
        var result = await service.ListAsync("Ayla", "char-a", 100, 1, 250, 7200);

        Assert.Equal(AuctionResultCode.Ok, result.Code);
        Assert.NotNull(result.Listing);
        Assert.Equal("Ayla", result.Listing.SellerName);
        Assert.Equal(100, result.Listing.ItemEntry);
        Assert.Equal(1, result.Listing.ItemCount);
        Assert.Equal(250, result.Listing.StartPrice);
        Assert.Equal(1_000 + 7200, result.Listing.ExpiresAtUnixSeconds);
        Assert.Single(await store.ListAllAsync());
    }

    [Fact]
    public async Task ListAsync_ClampsTheDurationToTheAllowedRange()
    {
        var service = NewService(out _, out _);
        var shortListing = await service.ListAsync("Ayla", "char-a", 100, 1, 10, 60);
        Assert.Equal(1_000 + AuctionService.MinDurationSeconds, shortListing.Listing!.ExpiresAtUnixSeconds);

        var longListing = await service.ListAsync("Ayla", "char-a", 101, 1, 10, int.MaxValue);
        Assert.Equal(1_000 + AuctionService.MaxDurationSeconds, longListing.Listing!.ExpiresAtUnixSeconds);
    }

    [Fact]
    public async Task ListAsync_RefusesAFreeListing()
    {
        var service = NewService(out var store, out _);
        var result = await service.ListAsync("Ayla", "char-a", 100, 1, 0, 7200);

        Assert.Equal(AuctionResultCode.InvalidPrice, result.Code);
        Assert.Empty(await store.ListAllAsync());
    }

    [Fact]
    public async Task ListAsync_RefusesANonPositiveDuration()
    {
        var service = NewService(out var store, out _);
        var result = await service.ListAsync("Ayla", "char-a", 100, 1, 10, 0);

        Assert.Equal(AuctionResultCode.InvalidDuration, result.Code);
        Assert.Empty(await store.ListAllAsync());
    }

    [Fact]
    public async Task ListAsync_RefusesABadItem()
    {
        var service = NewService(out _, out _);
        Assert.Equal(AuctionResultCode.InvalidItem,
            (await service.ListAsync("Ayla", "char-a", 0, 1, 10, 7200)).Code);
        Assert.Equal(AuctionResultCode.InvalidItem,
            (await service.ListAsync("Ayla", "char-a", 100, 0, 10, 7200)).Code);
    }

    [Fact]
    public async Task Search_ShowsNewestFirstAndHidesExpiredRows()
    {
        var store = new FakeAuctionStore();
        var mail = new FakeMailStore();
        var seller = new AuctionService(store, new MailService(mail, () => 1_000), () => 1_000);
        await seller.ListAsync("Ayla", "char-a", 100, 1, 10, 7200);
        await seller.ListAsync("Borin", "char-b", 101, 2, 20, 7200);

        var later = new AuctionService(store, new MailService(mail, () => 1_000_000), () => 1_000_000);
        await later.ListAsync("Ceri", "char-c", 102, 1, 30, 7200);

        var active = (await later.SearchAsync(new AuctionQuery())).Listings;
        var entry = Assert.Single(active);
        Assert.Equal("Ceri", entry.SellerName);
        Assert.Equal(102, entry.ItemEntry);
    }

    [Fact]
    public async Task Search_NameMatchesItemNamesCaseInsensitivelyAndFallsBackToEntryNumbers()
    {
        var service = NewService(out _, out _);
        await service.ListAsync("Ayla", "char-a", 100, 1, 10, 7200);
        await service.ListAsync("Borin", "char-b", 205, 1, 10, 7200);

        string? ResolveName(int entry) => entry == 100 ? "Swift Windblade" : null;

        var byName = (await service.SearchAsync(new AuctionQuery(Name: "windblade"), ResolveName)).Listings;
        Assert.Equal("Ayla", Assert.Single(byName).SellerName);

        var byNumber = (await service.SearchAsync(new AuctionQuery(Name: "205"), ResolveName)).Listings;
        Assert.Equal("Borin", Assert.Single(byNumber).SellerName);

        var miss = (await service.SearchAsync(new AuctionQuery(Name: "no such item"), ResolveName)).Listings;
        Assert.Empty(miss);
    }

    [Fact]
    public async Task CancelAsync_ByTheSeller_RemovesTheRowAndReportsWhatWasOnIt()
    {
        var service = NewService(out var store, out _);
        var listed = await service.ListAsync("Ayla", "char-a", 100, 3, 10, 7200);

        var result = await service.CancelAsync("char-a", listed.Listing!.Id);

        Assert.Equal(AuctionResultCode.Ok, result.Code);
        Assert.Equal(100, result.Listing!.ItemEntry);
        Assert.Equal(3, result.Listing.ItemCount);
        Assert.Empty(await store.ListAllAsync());
    }

    [Fact]
    public async Task CancelAsync_ByAnyoneElse_RefusesAndKeepsTheListing()
    {
        var service = NewService(out var store, out _);
        var listed = await service.ListAsync("Ayla", "char-a", 100, 1, 10, 7200);

        var result = await service.CancelAsync("char-b", listed.Listing!.Id);

        Assert.Equal(AuctionResultCode.NotSeller, result.Code);
        Assert.Single(await store.ListAllAsync());
    }

    [Fact]
    public async Task CancelAsync_UnknownId_IsRefused()
    {
        var service = NewService(out _, out _);
        var result = await service.CancelAsync("char-a", "auction-nope");

        Assert.Equal(AuctionResultCode.NotFound, result.Code);
    }

    [Fact]
    public async Task CancelAsync_ExpiredRow_StillCancelsSoTheItemIsNotLost()
    {
        var store = new FakeAuctionStore();
        var mail = new FakeMailStore();
        var seller = new AuctionService(store, new MailService(mail, () => 1_000), () => 1_000);
        var listed = await seller.ListAsync("Ayla", "char-a", 100, 1, 10, 7200);

        var later = new AuctionService(store, new MailService(mail, () => 1_000_000), () => 1_000_000);
        Assert.Empty((await later.SearchAsync(new AuctionQuery())).Listings);

        var result = await later.CancelAsync("char-a", listed.Listing!.Id);
        Assert.Equal(AuctionResultCode.Ok, result.Code);
        Assert.Equal(100, result.Listing!.ItemEntry);
    }

    [Fact]
    public async Task ListAsync_WithABuyout_PersistsIt_AndAFlatBuyoutIsRefused()
    {
        var service = NewService(out _, out _);
        var result = await service.ListAsync("Ayla", "char-a", 100, 1, 100, 7200, buyoutPrice: 500);

        Assert.Equal(AuctionResultCode.Ok, result.Code);
        Assert.Equal(500, result.Listing!.BuyoutPrice);

        var flat = await service.ListAsync("Ayla", "char-a", 101, 1, 100, 7200, buyoutPrice: 100);
        Assert.Equal(AuctionResultCode.InvalidPrice, flat.Code);
    }

    [Fact]
    public async Task PlaceBidAsync_FirstBidTakesTheLead_WithNoRefundOwed()
    {
        var service = NewService(out _, out var mail);
        var listed = await service.ListAsync("Ayla", "char-a", 100, 1, 100, 7200);

        var result = await service.PlaceBidAsync("Borin", "char-b", listed.Listing!.Id, 150, 0, null);

        Assert.Equal(AuctionResultCode.Ok, result.Code);
        Assert.Equal(150, result.Listing!.CurrentBid);
        Assert.Equal("Borin", result.Listing.HighestBidderName);
        Assert.Equal(0, result.RefundedAmount);
        Assert.Empty(mail.SentTo("char-b"));
    }

    [Fact]
    public async Task PlaceBidAsync_OutbidRefundsThePriorBidderByMail()
    {
        var service = NewService(out _, out var mail);
        var listed = await service.ListAsync("Ayla", "char-a", 100, 1, 100, 7200);
        await service.PlaceBidAsync("Borin", "char-b", listed.Listing!.Id, 150, 0, null);

        var result = await service.PlaceBidAsync("Ceri", "char-c", listed.Listing!.Id, 200, 150, "char-b");

        Assert.Equal(AuctionResultCode.Ok, result.Code);
        Assert.Equal("Borin", result.RefundedBidderName);
        Assert.Equal(150, result.RefundedAmount);
        var refund = Assert.Single(mail.SentTo("char-b"));
        Assert.Equal(150, refund.Gold);
        Assert.Equal("Auction House", refund.SenderName);
    }

    [Fact]
    public async Task PlaceBidAsync_RaisingYourOwnBid_RefundsNobody()
    {
        var service = NewService(out _, out var mail);
        var listed = await service.ListAsync("Ayla", "char-a", 100, 1, 100, 7200);
        await service.PlaceBidAsync("Borin", "char-b", listed.Listing!.Id, 150, 0, null);

        var result = await service.PlaceBidAsync("Borin", "char-b", listed.Listing!.Id, 180, 150, "char-b");

        Assert.Equal(AuctionResultCode.Ok, result.Code);
        Assert.Equal(180, result.Listing!.CurrentBid);
        Assert.Equal(0, result.RefundedAmount);
        Assert.Empty(mail.SentTo("char-b"));
    }

    [Fact]
    public async Task PlaceBidAsync_RefusesSelfBidsLowBidsAndExpiredRows()
    {
        var service = NewService(out _, out _);
        var listed = await service.ListAsync("Ayla", "char-a", 100, 1, 100, 7200);

        Assert.Equal(AuctionResultCode.SelfBid,
            (await service.PlaceBidAsync("Ayla", "char-a", listed.Listing!.Id, 150, 0, null)).Code);
        Assert.Equal(AuctionResultCode.BidTooLow,
            (await service.PlaceBidAsync("Borin", "char-b", listed.Listing.Id, 100, 0, null)).Code);
        Assert.Equal(AuctionResultCode.BidTooLow,
            (await service.PlaceBidAsync("Borin", "char-b", listed.Listing.Id, 50, 0, null)).Code);
        Assert.Equal(AuctionResultCode.NotFound,
            (await service.PlaceBidAsync("Borin", "char-b", "auction-nope", 150, 0, null)).Code);
    }

    [Fact]
    public async Task PlaceBidAsync_StalePeek_IsRefusedSoTheChargeStaysCorrect()
    {
        var service = NewService(out _, out _);
        var listed = await service.ListAsync("Ayla", "char-a", 100, 1, 100, 7200);
        await service.PlaceBidAsync("Borin", "char-b", listed.Listing!.Id, 150, 0, null);

        var stale = await service.PlaceBidAsync("Ceri", "char-c", listed.Listing.Id, 160, 0, null);

        Assert.Equal(AuctionResultCode.BidStale, stale.Code);
        Assert.Equal(150, (await service.TryGetAsync(listed.Listing.Id))!.CurrentBid);
    }

    [Fact]
    public async Task ExpiredListingWithAWinner_SettlesLazily_ItemToWinnerMoneyToSeller()
    {
        var store = new FakeAuctionStore();
        var mail = new FakeMailStore();
        var seller = new AuctionService(store, new MailService(mail, () => 1_000), () => 1_000);
        var listed = await seller.ListAsync("Ayla", "char-a", 100, 2, 100, 7200);
        await seller.PlaceBidAsync("Borin", "char-b", listed.Listing!.Id, 150, 0, null);

        var later = new AuctionService(store, new MailService(mail, () => 1_000_000), () => 1_000_000);
        Assert.Empty((await later.SearchAsync(new AuctionQuery())).Listings);

        var winnerMail = Assert.Single(mail.SentTo("char-b"));
        Assert.Equal(0, winnerMail.Gold);
        var attachment = Assert.Single(winnerMail.Attachments ?? []);
        Assert.Equal(100, attachment.Entry);
        Assert.Equal(2, attachment.Count);

        var sellerMail = Assert.Single(mail.SentTo("char-a"));
        Assert.Equal(150, sellerMail.Gold);
        Assert.Empty(await store.ListAllAsync());
    }

    [Fact]
    public async Task BuyoutAsync_DeliversImmediately_ItemToBuyerMoneyToSellerRefundToOutbid()
    {
        var service = NewService(out var store, out var mail);
        var listed = await service.ListAsync("Ayla", "char-a", 100, 1, 100, 7200, buyoutPrice: 500);
        await service.PlaceBidAsync("Borin", "char-b", listed.Listing!.Id, 150, 0, null);

        var result = await service.BuyoutAsync("Ceri", "char-c", listed.Listing.Id, 150, "char-b");

        Assert.Equal(AuctionResultCode.Ok, result.Code);
        Assert.Equal(500, result.Listing!.BuyoutPrice);
        Assert.Empty(await store.ListAllAsync());

        var buyerMail = Assert.Single(mail.SentTo("char-c"));
        Assert.Equal(100, Assert.Single(buyerMail.Attachments ?? []).Entry);

        var sellerMail = Assert.Single(mail.SentTo("char-a"));
        Assert.Equal(500, sellerMail.Gold);

        var refund = Assert.Single(mail.SentTo("char-b"));
        Assert.Equal(150, refund.Gold);
    }

    [Fact]
    public async Task BuyoutAsync_RefusesSelfBuyoutMissingBuyoutAndStalePeeks()
    {
        var service = NewService(out _, out _);
        var listed = await service.ListAsync("Ayla", "char-a", 100, 1, 100, 7200);
        var flat = await service.ListAsync("Ayla", "char-a", 101, 1, 100, 7200, buyoutPrice: 500);

        Assert.Equal(AuctionResultCode.NoBuyout,
            (await service.BuyoutAsync("Borin", "char-b", listed.Listing!.Id, 0, null)).Code);
        Assert.Equal(AuctionResultCode.SelfBid,
            (await service.BuyoutAsync("Ayla", "char-a", flat.Listing!.Id, 0, null)).Code);

        await service.PlaceBidAsync("Borin", "char-b", flat.Listing.Id, 150, 0, null);
        Assert.Equal(AuctionResultCode.BidStale,
            (await service.BuyoutAsync("Ceri", "char-c", flat.Listing.Id, 0, null)).Code);
    }

    [Fact]
    public async Task CancelAsync_WithBids_RefundsTheHighestBidderByMail()
    {
        var service = NewService(out var store, out var mail);
        var listed = await service.ListAsync("Ayla", "char-a", 100, 1, 100, 7200);
        await service.PlaceBidAsync("Borin", "char-b", listed.Listing!.Id, 150, 0, null);

        var result = await service.CancelAsync("char-a", listed.Listing.Id);

        Assert.Equal(AuctionResultCode.Ok, result.Code);
        Assert.Empty(await store.ListAllAsync());
        Assert.Equal(150, Assert.Single(mail.SentTo("char-b")).Gold);
    }

    [Fact]
    public async Task OldRowWithoutBiddingFields_LoadsAsAnUnbidListing()
    {
        var store = new FakeAuctionStore();
        var mail = new FakeMailStore();
        await store.SaveAllAsync([new PersistedAuctionListing(
            "auction-old", "char-a", "Ayla", 100, 1, 100, 1_000, 1_000 + 7200)]);

        var service = new AuctionService(store, new MailService(mail, () => 1_000), () => 1_000);
        var found = await service.TryGetAsync("auction-old");

        Assert.NotNull(found);
        Assert.Null(found.BuyoutPrice);
        Assert.Equal(0, found.CurrentBid);
        Assert.Null(found.HighestBidderName);
    }

    private static AuctionItemFacts? Facts(int entry) => entry switch
    {
        100 => new AuctionItemFacts(ItemClass: 2, SubClass: 16, RequiredLevel: 30, Quality: 3, InventoryType: 15),
        101 => new AuctionItemFacts(ItemClass: 2, SubClass: 3, RequiredLevel: 10, Quality: 2, InventoryType: 13),
        102 => new AuctionItemFacts(ItemClass: 4, SubClass: 4, RequiredLevel: 42, Quality: 4, InventoryType: 7),
        _ => null,
    };

    private static string? Named(int entry) => entry switch
    {
        100 => "Bronze Dagger",
        101 => "Test Staff",
        102 => "Steel Legplate",
        _ => null,
    };

    [Fact]
    public async Task narrows_by_the_kind_of_thing_rather_than_by_its_name()
    {
        var service = NewService(out _, out _);
        await service.ListAsync("Ayla", "char-a", 100, 1, 10, 7200);
        await service.ListAsync("Ayla", "char-a", 101, 1, 10, 7200);
        await service.ListAsync("Ayla", "char-a", 102, 1, 10, 7200);

        var page = await service.SearchAsync(
            new AuctionQuery(Kinds: [new AuctionKind(ItemClass: 2, SubClassFrom: 16, SubClassTo: 20)]),
            Named, Facts);

        Assert.Equal(1, page.Total);
        Assert.Equal(100, page.Listings.Single().ItemEntry);
    }

    [Fact]
    public async Task narrows_by_where_a_thing_is_worn_whatever_class_it_is()
    {
        var service = NewService(out _, out _);
        await service.ListAsync("Ayla", "char-a", 100, 1, 10, 7200);
        await service.ListAsync("Ayla", "char-a", 102, 1, 10, 7200);

        var page = await service.SearchAsync(
            new AuctionQuery(Kinds: [new AuctionKind(InventoryType: 7), new AuctionKind(InventoryType: 2)]),
            Named, Facts);

        Assert.Equal(102, Assert.Single(page.Listings).ItemEntry);
    }

    [Fact]
    public async Task narrows_by_the_level_an_item_asks_for()
    {
        var service = NewService(out _, out _);
        await service.ListAsync("Ayla", "char-a", 100, 1, 10, 7200);
        await service.ListAsync("Ayla", "char-a", 101, 1, 10, 7200);
        await service.ListAsync("Ayla", "char-a", 102, 1, 10, 7200);

        var page = await service.SearchAsync(new AuctionQuery(MinLevel: 20, MaxLevel: 40), Named, Facts);

        Assert.Equal(100, Assert.Single(page.Listings).ItemEntry);
    }

    [Fact]
    public async Task keeps_a_row_it_cannot_describe_rather_than_hiding_it()
    {
        var service = NewService(out _, out _);
        await service.ListAsync("Ayla", "char-a", 999, 1, 10, 7200);

        var page = await service.SearchAsync(
            new AuctionQuery(Kinds: [new AuctionKind(ItemClass: 2)], MinLevel: 60), Named, Facts);

        Assert.Equal(999, Assert.Single(page.Listings).ItemEntry);
    }

    [Fact]
    public async Task ignores_a_rank_the_package_cannot_read()
    {
        var service = NewService(out _, out _);
        await service.ListAsync("Ayla", "char-a", 100, 1, 10, 7200);

        var rankless = await service.SearchAsync(
            new AuctionQuery(Quality: 5), Named,
            entry => new AuctionItemFacts(2, 16, 30, Quality: null));

        Assert.Single(rankless.Listings);
    }

    [Fact]
    public async Task counts_every_match_while_answering_with_one_page()
    {
        var service = NewService(out _, out _);
        for (var i = 0; i < 5; i++)
            await service.ListAsync("Ayla", "char-a", 100, 1, 10 + i, 7200);

        var first = await service.SearchAsync(new AuctionQuery(Limit: 2), Named, Facts);
        var second = await service.SearchAsync(new AuctionQuery(Offset: 2, Limit: 2), Named, Facts);
        var last = await service.SearchAsync(new AuctionQuery(Offset: 4, Limit: 2), Named, Facts);

        Assert.Equal(5, first.Total);
        Assert.Equal(2, first.Listings.Count);
        Assert.Equal(2, second.Listings.Count);
        Assert.Single(last.Listings);
        Assert.Empty(first.Listings.Select(row => row.Id).Intersect(second.Listings.Select(row => row.Id)));
    }

    [Fact]
    public async Task answers_everything_when_asked_for_nothing_in_particular()
    {
        var service = NewService(out _, out _);
        await service.ListAsync("Ayla", "char-a", 100, 1, 10, 7200);
        await service.ListAsync("Ayla", "char-a", 102, 1, 10, 7200);

        var page = await service.SearchAsync(new AuctionQuery(), Named, Facts);

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Listings.Count);
    }

    [Fact]
    public async Task sorts_by_the_column_the_screen_was_sorted_by()
    {
        var service = NewService(out _, out _);
        await service.ListAsync("Ayla", "char-a", 102, 1, 10, 7200);
        await service.ListAsync("Ayla", "char-a", 101, 1, 10, 7200);
        await service.ListAsync("Ayla", "char-a", 100, 1, 10, 7200);

        var up = await service.SearchAsync(
            new AuctionQuery(OrderBy: AuctionOrder.Level, Ascending: true), Named, Facts);

        Assert.Equal([101, 100, 102], up.Listings.Select(row => row.ItemEntry));
    }
}
