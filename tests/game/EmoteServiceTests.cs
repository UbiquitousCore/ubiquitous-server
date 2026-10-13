
namespace Ubiquitous.Game.Tests;

public sealed class EmoteServiceTests
{
    [Fact]
    public void Create_AcceptsAKnownEmoteByName()
    {
        var emote = new EmoteService().Create(new EmoteRequest("wave"), "player:a");

        Assert.NotNull(emote);
        Assert.Equal("player:a", emote!.SenderId);
        Assert.Equal("wave", emote.Name);
        Assert.Equal(141, emote.Id);
    }

    [Fact]
    public void Create_IsCaseInsensitiveAndTrims()
    {
        var emote = new EmoteService().Create(new EmoteRequest("  Dance  "), "player:a");

        Assert.NotNull(emote);
        Assert.Equal("dance", emote!.Name);
    }

    [Fact]
    public void Create_RejectsAnUnknownName()
    {
        Assert.Null(new EmoteService().Create(new EmoteRequest("moonwalk"), "player:a"));
    }

    [Fact]
    public void Create_RejectsAnEmptyName()
    {
        Assert.Null(new EmoteService().Create(new EmoteRequest(""), "player:a"));
    }

    [Fact]
    public void Create_RejectsAnEmptySender()
    {
        Assert.Null(new EmoteService().Create(new EmoteRequest("wave"), ""));
    }

    [Fact]
    public void All_NamesAreUnique()
    {
        var names = EmoteService.All.Select(e => e.Name).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void All_IdsAreUnique()
    {
        var ids = EmoteService.All.Select(e => e.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }
}
