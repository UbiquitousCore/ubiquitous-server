using Ubiquitous.Common;

namespace Ubiquitous.Game.Tests;

public sealed class FriendServiceTests
{
    private sealed class FakeFriendStore : IFriendStore
    {
        private readonly Dictionary<string, PersistedSocialList> _byOwner = new(StringComparer.Ordinal);

        public Task<PersistedSocialList> LoadAsync(string ownerGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byOwner.TryGetValue(ownerGuid, out var list) ? list : PersistedSocialList.Empty);

        public Task SaveAsync(string ownerGuid, PersistedSocialList list, CancellationToken cancellationToken = default)
        {
            _byOwner[ownerGuid] = list;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AddAsync_RefusesAddingYourselfAsAFriend()
    {
        var friends = new FriendService(new FakeFriendStore());

        var result = await friends.AddAsync("char-a", SocialListKind.Friend, "char-a", "Ayla");

        Assert.Equal(SocialAddOutcome.SelfTarget, result.Outcome);
        Assert.Null(result.List);
    }

    [Fact]
    public async Task AddAsync_RefusesIgnoringYourself()
    {
        var friends = new FriendService(new FakeFriendStore());

        var result = await friends.AddAsync("char-a", SocialListKind.Ignore, "char-a", "Ayla");

        Assert.Equal(SocialAddOutcome.SelfTarget, result.Outcome);
    }

    [Fact]
    public async Task AddAsync_AddsAFriendAndItIsReturnedByListAsync()
    {
        var friends = new FriendService(new FakeFriendStore());

        var result = await friends.AddAsync("char-a", SocialListKind.Friend, "char-b", "Borin");

        Assert.Equal(SocialAddOutcome.Ok, result.Outcome);
        var entry = Assert.Single(result.List!.Friends);
        Assert.Equal("char-b", entry.CharacterGuid);
        Assert.Equal("Borin", entry.Name);
        Assert.Empty(result.List.Ignored);

        var list = await friends.ListAsync("char-a");
        Assert.Single(list.Friends);
    }

    [Fact]
    public async Task AddAsync_AddingTheSameFriendTwiceIsAHarmlessNoOp()
    {
        var friends = new FriendService(new FakeFriendStore());
        await friends.AddAsync("char-a", SocialListKind.Friend, "char-b", "Borin");

        var result = await friends.AddAsync("char-a", SocialListKind.Friend, "char-b", "Borin (renamed)");

        Assert.Equal(SocialAddOutcome.Ok, result.Outcome);
        var entry = Assert.Single(result.List!.Friends);
        Assert.Equal("Borin", entry.Name);
    }

    [Fact]
    public async Task AddAsync_FriendAndIgnoreListsAreIndependent()
    {
        var friends = new FriendService(new FakeFriendStore());

        await friends.AddAsync("char-a", SocialListKind.Friend, "char-b", "Borin");
        await friends.AddAsync("char-a", SocialListKind.Ignore, "char-c", "Cara");

        var list = await friends.ListAsync("char-a");
        Assert.Single(list.Friends);
        Assert.Single(list.Ignored);
        Assert.Equal("char-b", list.Friends[0].CharacterGuid);
        Assert.Equal("char-c", list.Ignored[0].CharacterGuid);
    }

    [Fact]
    public async Task AddAsync_IgnoringAFriendDropsThemFromFriends()
    {
        var friends = new FriendService(new FakeFriendStore());

        await friends.AddAsync("char-a", SocialListKind.Friend, "char-b", "Borin");
        var result = await friends.AddAsync("char-a", SocialListKind.Ignore, "char-b", "Borin");

        Assert.Equal(SocialAddOutcome.Ok, result.Outcome);
        Assert.Empty(result.List!.Friends);
        Assert.Single(result.List.Ignored);
    }

    [Fact]
    public async Task RemoveAsync_RemovesAFriend()
    {
        var friends = new FriendService(new FakeFriendStore());
        await friends.AddAsync("char-a", SocialListKind.Friend, "char-b", "Borin");

        var list = await friends.RemoveAsync("char-a", SocialListKind.Friend, "char-b");

        Assert.Empty(list.Friends);
    }

    [Fact]
    public async Task RemoveAsync_RemovingSomeoneNotOnTheListIsAHarmlessNoOp()
    {
        var friends = new FriendService(new FakeFriendStore());

        var list = await friends.RemoveAsync("char-a", SocialListKind.Friend, "char-nobody");

        Assert.Empty(list.Friends);
    }

    [Fact]
    public async Task RemoveAsync_OnlyRemovesFromTheNamedList()
    {
        var friends = new FriendService(new FakeFriendStore());
        await friends.AddAsync("char-a", SocialListKind.Friend, "char-b", "Borin");
        await friends.AddAsync("char-a", SocialListKind.Ignore, "char-b", "Borin");

        var list = await friends.RemoveAsync("char-a", SocialListKind.Friend, "char-b");

        Assert.Empty(list.Friends);
        Assert.Single(list.Ignored);
    }

    [Fact]
    public async Task ListAsync_ForACharacterWithNoListsYetIsEmpty()
    {
        var friends = new FriendService(new FakeFriendStore());

        var list = await friends.ListAsync("char-a");

        Assert.Empty(list.Friends);
        Assert.Empty(list.Ignored);
    }
}
