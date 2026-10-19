using Ubiquitous.Common;

namespace Ubiquitous.Game.Tests;

public sealed class MailServiceTests
{
    private sealed class FakeMailStore : IMailStore
    {
        private readonly Dictionary<string, List<PersistedMailMessage>> _byRecipient = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<PersistedMailMessage>> ListAsync(string recipientGuid, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PersistedMailMessage> result = _byRecipient.TryGetValue(recipientGuid, out var list) ? list.ToArray() : [];
            return Task.FromResult(result);
        }

        public Task SaveAsync(string recipientGuid, IReadOnlyList<PersistedMailMessage> messages, CancellationToken cancellationToken = default)
        {
            _byRecipient[recipientGuid] = messages.ToList();
            return Task.CompletedTask;
        }
    }

    private static MailService NewService(out FakeMailStore store, long now = 1_000)
    {
        store = new FakeMailStore();
        return new MailService(store, () => now);
    }

    [Fact]
    public async Task SendAsync_RejectsMailingSelf()
    {
        var service = NewService(out _);
        var result = await service.SendAsync("Ayla", "char-a", "char-a", "Hi", "body", 0);
        Assert.Equal(MailResultCode.SelfMail, result.Code);
    }

    [Fact]
    public async Task SendAsync_RejectsEmptyBody()
    {
        var service = NewService(out _);
        var result = await service.SendAsync("Ayla", "char-a", "char-b", "Hi", "   ", 0);
        Assert.Equal(MailResultCode.EmptyBody, result.Code);
    }

    [Fact]
    public async Task SendAsync_RejectsNegativeGold()
    {
        var service = NewService(out _);
        var result = await service.SendAsync("Ayla", "char-a", "char-b", "Hi", "body", -1);
        Assert.Equal(MailResultCode.NegativeGold, result.Code);
    }

    [Fact]
    public async Task SendAsync_ThenListInbox_ShowsTheMessage()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi there", 50);
        Assert.Equal(MailResultCode.Ok, sent.Code);

        var inbox = await service.ListInboxAsync("char-b");
        var entry = Assert.Single(inbox);
        Assert.Equal("Ayla", entry.SenderName);
        Assert.Equal("Hello", entry.Subject);
        Assert.Equal(50, entry.Gold);
        Assert.False(entry.Read);
        Assert.False(entry.GoldClaimed);
    }

    [Fact]
    public async Task ListInbox_NeverSeesAnotherRecipientsMail()
    {
        var service = NewService(out _);
        await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 0);

        var inboxForSomeoneElse = await service.ListInboxAsync("char-c");
        Assert.Empty(inboxForSomeoneElse);
    }

    [Fact]
    public async Task ReadAsync_MarksReadAndReturnsBody()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "the body text", 0);

        var read = await service.ReadAsync("char-b", sent.Sent!.Id);
        Assert.Equal(MailResultCode.Ok, read.Code);
        Assert.Equal("the body text", read.Body);

        var inbox = await service.ListInboxAsync("char-b");
        Assert.True(Assert.Single(inbox).Read);
    }

    [Fact]
    public async Task ReadAsync_UnknownId_IsNotFound()
    {
        var service = NewService(out _);
        var read = await service.ReadAsync("char-b", "no-such-id");
        Assert.Equal(MailResultCode.MailNotFound, read.Code);
    }

    [Fact]
    public async Task Claim_ReturnsGoldOnce_ThenRefusesASecondClaim()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 75);

        var firstClaim = await service.ClaimAsync("char-b", sent.Sent!.Id, freeSlots: 12);
        Assert.Equal(MailResultCode.Ok, firstClaim.Code);
        Assert.Equal(75, firstClaim.GoldClaimed);

        var secondClaim = await service.ClaimAsync("char-b", sent.Sent.Id, freeSlots: 12);
        Assert.Equal(MailResultCode.AlreadyClaimed, secondClaim.Code);
        Assert.Equal(0, secondClaim.GoldClaimed);
    }

    [Fact]
    public async Task Claim_NoGoldAttached_RefusesWithNothingToClaim()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 0);

        var claim = await service.ClaimAsync("char-b", sent.Sent!.Id, freeSlots: 12);
        Assert.Equal(MailResultCode.NothingToClaim, claim.Code);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheMessage_EvenWithUnclaimedGold()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 100);

        var deleted = await service.DeleteAsync("char-b", sent.Sent!.Id);
        Assert.Equal(MailResultCode.Ok, deleted.Code);

        var inbox = await service.ListInboxAsync("char-b");
        Assert.Empty(inbox);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_IsNotFound()
    {
        var service = NewService(out _);
        var deleted = await service.DeleteAsync("char-b", "no-such-id");
        Assert.Equal(MailResultCode.MailNotFound, deleted.Code);
    }

    [Fact]
    public async Task ListInbox_NewestFirst()
    {
        var store = new FakeMailStore();
        var earlyClock = 100L;
        var service = new MailService(store, () => earlyClock);
        await service.SendAsync("Ayla", "char-a", "char-b", "First", "Hi", 0);
        earlyClock = 200L;
        await service.SendAsync("Ayla", "char-a", "char-b", "Second", "Hi", 0);

        var inbox = await service.ListInboxAsync("char-b");
        Assert.Equal(["Second", "First"], inbox.Select(m => m.Subject));
    }

    [Fact]
    public async Task SendAsync_WithAttachments_RecordsThemOnTheSummary()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 10,
            [new MailItemAttachment(100, 1), new MailItemAttachment(200, 3)]);
        Assert.Equal(MailResultCode.Ok, sent.Code);
        Assert.Equal(2, sent.Sent!.ItemCount);
        Assert.False(sent.Sent.ItemsClaimed);

        var inbox = await service.ListInboxAsync("char-b");
        var entry = Assert.Single(inbox);
        Assert.Equal(2, entry.ItemCount);
        Assert.False(entry.ItemsClaimed);
    }

    [Fact]
    public async Task ReadAsync_ReturnsAttachedItems()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 0,
            [new MailItemAttachment(100, 2)]);

        var read = await service.ReadAsync("char-b", sent.Sent!.Id);
        Assert.Equal(MailResultCode.Ok, read.Code);
        var item = Assert.Single(read.Items ?? []);
        Assert.Equal(100, item.Entry);
        Assert.Equal(2, item.Count);
    }

    [Fact]
    public async Task ClaimAsync_WithGoldAndItems_ClaimsBothAtOnce()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 25,
            [new MailItemAttachment(100, 1)]);

        var claim = await service.ClaimAsync("char-b", sent.Sent!.Id, freeSlots: 5);
        Assert.Equal(MailResultCode.Ok, claim.Code);
        Assert.Equal(25, claim.GoldClaimed);
        var item = Assert.Single(claim.Items ?? []);
        Assert.Equal(100, item.Entry);

        var secondClaim = await service.ClaimAsync("char-b", sent.Sent.Id, freeSlots: 5);
        Assert.Equal(MailResultCode.AlreadyClaimed, secondClaim.Code);
    }

    [Fact]
    public async Task ClaimAsync_WithNoRoom_RefusesWithBagFull_AndLosesNothing()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 25,
            [new MailItemAttachment(100, 1)]);

        var refused = await service.ClaimAsync("char-b", sent.Sent!.Id, freeSlots: 0);
        Assert.Equal(MailResultCode.BagFull, refused.Code);

        var retry = await service.ClaimAsync("char-b", sent.Sent.Id, freeSlots: 1);
        Assert.Equal(MailResultCode.Ok, retry.Code);
        Assert.Equal(25, retry.GoldClaimed);
        Assert.Single(retry.Items ?? []);
    }

    [Fact]
    public async Task Claim_OnItemCarryingMail_StillGrantsEverythingAtOnce()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 25,
            [new MailItemAttachment(100, 1)]);

        var claim = await service.ClaimAsync("char-b", sent.Sent!.Id, freeSlots: 12);
        Assert.Equal(MailResultCode.Ok, claim.Code);
        Assert.Equal(25, claim.GoldClaimed);
        Assert.Single(claim.Items ?? []);

        var done = await service.ClaimAsync("char-b", sent.Sent.Id, freeSlots: 1);
        Assert.Equal(MailResultCode.AlreadyClaimed, done.Code);
    }

    [Fact]
    public async Task ClaimAsync_MessageCarryingNothing_RefusesWithNothingToClaim()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 0);

        var claim = await service.ClaimAsync("char-b", sent.Sent!.Id, freeSlots: 5);
        Assert.Equal(MailResultCode.NothingToClaim, claim.Code);
    }

    [Fact]
    public async Task ClaimAsync_OneSlot_TakesThatItemAndLeavesTheRestAndTheMoney()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 25,
            [new MailItemAttachment(100, 1), new MailItemAttachment(200, 3)]);

        var taken = await service.ClaimAsync("char-b", sent.Sent!.Id, freeSlots: 5, new MailTake(Slot: 1));

        Assert.Equal(MailResultCode.Ok, taken.Code);
        Assert.Equal(0, taken.GoldClaimed);
        Assert.Equal(200, Assert.Single(taken.Items!).Entry);
        var left = await service.ReadAsync("char-b", sent.Sent.Id);
        Assert.Equal(100, Assert.Single(left.Items!).Entry);
        Assert.False(left.Summary!.GoldClaimed);
        Assert.False(left.Summary.ItemsClaimed);
    }

    [Fact]
    public async Task ClaimAsync_TakingTheLastItem_MarksTheItemsClaimed_AndTheLetterStays()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 0, [new MailItemAttachment(100, 1)]);

        await service.ClaimAsync("char-b", sent.Sent!.Id, freeSlots: 5, new MailTake(Slot: 0));

        var inbox = await service.ListInboxAsync("char-b");
        Assert.True(Assert.Single(inbox).ItemsClaimed);
        Assert.Equal(MailResultCode.AlreadyClaimed, (await service.ClaimAsync("char-b", sent.Sent.Id, freeSlots: 5)).Code);
    }

    [Fact]
    public async Task ClaimAsync_ASlotTheLetterDoesNotHave_IsRefused()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 0, [new MailItemAttachment(100, 1)]);

        var refused = await service.ClaimAsync("char-b", sent.Sent!.Id, freeSlots: 5, new MailTake(Slot: 3));

        Assert.Equal(MailResultCode.NotInMail, refused.Code);
    }

    [Fact]
    public async Task ClaimAsync_OneSlotWithNoRoom_RefusesWithBagFull()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 0, [new MailItemAttachment(100, 1)]);

        var refused = await service.ClaimAsync("char-b", sent.Sent!.Id, freeSlots: 0, new MailTake(Slot: 0));

        Assert.Equal(MailResultCode.BagFull, refused.Code);
    }

    [Fact]
    public async Task ClaimAsync_MoneyOnly_TakesTheMoneyOnceAndLeavesTheItems()
    {
        var service = NewService(out _);
        var sent = await service.SendAsync("Ayla", "char-a", "char-b", "Hello", "Hi", 25, [new MailItemAttachment(100, 1)]);

        var money = await service.ClaimAsync("char-b", sent.Sent!.Id, freeSlots: 0, new MailTake(MoneyOnly: true));
        var again = await service.ClaimAsync("char-b", sent.Sent.Id, freeSlots: 0, new MailTake(MoneyOnly: true));

        Assert.Equal(25, money.GoldClaimed);
        Assert.Empty(money.Items!);
        Assert.Equal(MailResultCode.AlreadyClaimed, again.Code);
        Assert.Single((await service.ReadAsync("char-b", sent.Sent.Id)).Items!);
    }
}
