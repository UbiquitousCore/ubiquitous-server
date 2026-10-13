using Ubiquitous.Common;

namespace Ubiquitous.Game.Tests;

public sealed class SharedServiceRaceTests
{
    private sealed class SlowMailStore : IMailStore
    {
        private readonly Dictionary<string, List<PersistedMailMessage>> _byRecipient = new(StringComparer.Ordinal);
        private int _saves;

        public int FailOnSave { get; init; }

        public IReadOnlyList<PersistedMailMessage> SentTo(string recipientGuid) =>
            _byRecipient.TryGetValue(recipientGuid, out var list) ? list.ToArray() : [];

        public async Task<IReadOnlyList<PersistedMailMessage>> ListAsync(string recipientGuid, CancellationToken cancellationToken = default)
        {
            var snapshot = SentTo(recipientGuid);
            await Task.Delay(20, cancellationToken);
            return snapshot;
        }

        public async Task SaveAsync(string recipientGuid, IReadOnlyList<PersistedMailMessage> messages, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (++_saves == FailOnSave) throw new IOException("disk full");
            _byRecipient[recipientGuid] = messages.ToList();
        }
    }

    private sealed class SlowAuctionStore : IAuctionStore
    {
        private List<PersistedAuctionListing> _listings = [];

        public async Task<IReadOnlyList<PersistedAuctionListing>> ListAllAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = _listings.ToArray();
            await Task.Delay(20, cancellationToken);
            return snapshot;
        }

        public async Task SaveAllAsync(IReadOnlyList<PersistedAuctionListing> listings, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            _listings = listings.ToList();
        }
    }

    private sealed class SlowGuildStore : IGuildStore
    {
        private readonly Dictionary<string, PersistedGuild> _byId = new(StringComparer.Ordinal);

        public async Task<IReadOnlyList<PersistedGuild>> ListAllAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = _byId.Values.ToArray();
            await Task.Delay(20, cancellationToken);
            return snapshot;
        }

        public async Task SaveAsync(PersistedGuild guild, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            _byId[guild.Id] = guild;
        }

        public Task DeleteAsync(string guildId, CancellationToken cancellationToken = default)
        {
            _byId.Remove(guildId);
            return Task.CompletedTask;
        }
    }

    private static async Task<(AuctionService Auctions, SlowMailStore Mail, string ListingId)> ListedAsync(int failMailSave = 0)
    {
        var mail = new SlowMailStore { FailOnSave = failMailSave };
        var auctions = new AuctionService(new SlowAuctionStore(), new MailService(mail, () => 1_000), () => 1_000);
        var listed = await auctions.ListAsync("Ayla", "char-seller", 100, 1, 10, 7200, buyoutPrice: 50);
        return (auctions, mail, listed.Listing!.Id);
    }

    [Fact]
    public async Task TwoSimultaneousBuyouts_OnlyOneWinsTheItem()
    {
        var (auctions, mail, listingId) = await ListedAsync();

        var results = await Task.WhenAll(
            auctions.BuyoutAsync("Borin", "char-b", listingId, 0, null),
            auctions.BuyoutAsync("Cora", "char-c", listingId, 0, null));

        Assert.Single(results, result => result.Code == AuctionResultCode.Ok);
        Assert.Single(results, result => result.Code == AuctionResultCode.NotFound);
        var won = mail.SentTo("char-b").Concat(mail.SentTo("char-c")).Where(message => message.Subject == "Auction won");
        Assert.Single(won);
        Assert.Single(mail.SentTo("char-seller"));
    }

    [Fact]
    public async Task TwoSimultaneousMailsToOneRecipient_BothArrive()
    {
        var mail = new SlowMailStore();
        var service = new MailService(mail, () => 1_000);

        await Task.WhenAll(
            service.SendAsync("Ayla", "char-a", "char-r", "One", "body", 0),
            service.SendAsync("Borin", "char-b", "char-r", "Two", "body", 0));

        Assert.Equal(2, mail.SentTo("char-r").Count);
    }

    [Fact]
    public async Task Buyout_RemovesTheListingBeforeMailing()
    {
        var (auctions, mail, listingId) = await ListedAsync(failMailSave: 2);

        await Assert.ThrowsAsync<IOException>(() => auctions.BuyoutAsync("Borin", "char-b", listingId, 0, null));
        var retry = await auctions.BuyoutAsync("Borin", "char-b", listingId, 0, null);

        Assert.Equal(AuctionResultCode.NotFound, retry.Code);
        Assert.Single(mail.SentTo("char-b"), message => message.Subject == "Auction won");
    }

    [Fact]
    public async Task TwoSimultaneousGuildsWithOneName_OnlyOneIsFounded()
    {
        var service = new GuildService(new SlowGuildStore());

        var results = await Task.WhenAll(
            service.Found("a1", "Ayla", 10, "Ironclad"),
            service.Found("b1", "Borin", 10, "Ironclad"));

        Assert.Single(results, result => result.Code == GuildResultCode.Ok);
        Assert.Single(results, result => result.Code == GuildResultCode.NameTaken);
    }
}
