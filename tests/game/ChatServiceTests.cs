
namespace Ubiquitous.Game.Tests;

public sealed class ChatServiceTests
{
    [Fact]
    public void Create_TrimsText()
    {
        Assert.Equal(new ChatEvent("say", "Ayla", "hello"),
            new ChatService().Create(new ChatCommand("say", "  hello  "), "Ayla"));
    }

    [Fact]
    public void Create_RejectsMissingText()
    {
        Assert.Null(new ChatService().Create(new ChatCommand("say", ""), "Ayla"));
    }

    [Fact]
    public void Create_RejectsAnUnknownChannel()
    {
        Assert.Null(new ChatService().Create(new ChatCommand("shout-into-the-void", "hello"), "Ayla"));
    }

    [Fact]
    public void Create_AllowsGeneral_JoinedByDefault()
    {
        Assert.Equal(new ChatEvent("general", "Ayla", "hello"),
            new ChatService().Create(new ChatCommand("general", "hello"), "Ayla"));
    }

    [Fact]
    public void Create_RejectsTrade_UntilJoined()
    {
        var chat = new ChatService();
        Assert.Null(chat.Create(new ChatCommand("trade", "wts sword"), "Ayla"));

        Assert.True(chat.Join("trade"));
        Assert.Equal(new ChatEvent("trade", "Ayla", "wts sword"), chat.Create(new ChatCommand("trade", "wts sword"), "Ayla"));
    }

    [Fact]
    public void Create_ChannelNameIsCaseInsensitiveAndNormalizedToLowercase()
    {
        Assert.Equal(new ChatEvent("say", "Ayla", "hello"),
            new ChatService().Create(new ChatCommand("SAY", "hello"), "Ayla"));
    }

    [Fact]
    public void Join_RejectsAnUnknownChannel()
    {
        Assert.False(new ChatService().Join("not-a-real-channel"));
    }

    [Fact]
    public void Join_RejectsSay_ItIsAlwaysOn()
    {
        Assert.False(new ChatService().Join("say"));
    }

    [Fact]
    public void Join_IsIdempotent()
    {
        var chat = new ChatService();
        Assert.True(chat.Join("trade"));
        Assert.True(chat.Join("trade"));
        Assert.True(chat.IsJoined("trade"));
    }

    [Fact]
    public void Leave_RemovesMembership_SoFurtherMessagesAreRejected()
    {
        var chat = new ChatService();
        Assert.True(chat.IsJoined("general"));
        Assert.True(chat.Leave("general"));
        Assert.False(chat.IsJoined("general"));
        Assert.Null(chat.Create(new ChatCommand("general", "hello"), "Ayla"));
    }

    [Fact]
    public void Leave_RejectsSay_ItCanNeverBeLeft()
    {
        Assert.False(new ChatService().Leave("say"));
    }

    [Fact]
    public void Leave_RejectsAnUnknownChannel()
    {
        Assert.False(new ChatService().Leave("not-a-real-channel"));
    }

    [Fact]
    public void IsRangeLimited_OnlyTrueForSay()
    {
        Assert.True(ChatService.IsRangeLimited("say"));
        Assert.True(ChatService.IsRangeLimited("SAY"));
        Assert.False(ChatService.IsRangeLimited("general"));
        Assert.False(ChatService.IsRangeLimited("trade"));
    }

    [Fact]
    public void IsKnownChannel_RecognizesExactlyTheFourNamedChannels()
    {
        Assert.True(ChatService.IsKnownChannel("say"));
        Assert.True(ChatService.IsKnownChannel("general"));
        Assert.True(ChatService.IsKnownChannel("trade"));
        Assert.True(ChatService.IsKnownChannel("party"));
        Assert.False(ChatService.IsKnownChannel("guild"));
        Assert.False(ChatService.IsKnownChannel(""));
        Assert.False(ChatService.IsKnownChannel(null));
    }

    [Fact]
    public void Party_IsAlwaysJoined_LikeSay()
    {
        Assert.True(new ChatService().IsJoined("party"));
        Assert.Equal(new ChatEvent("party", "Ayla", "form up"),
            new ChatService().Create(new ChatCommand("party", "form up"), "Ayla"));
    }

    [Fact]
    public void Join_RejectsParty_ItIsAlwaysOn()
    {
        Assert.False(new ChatService().Join("party"));
    }

    [Fact]
    public void Leave_RejectsParty_ItCanNeverBeLeftThisWay()
    {
        Assert.False(new ChatService().Leave("party"));
    }

    [Fact]
    public void IsPartyChannel_OnlyTrueForParty()
    {
        Assert.True(ChatService.IsPartyChannel("party"));
        Assert.True(ChatService.IsPartyChannel("PARTY"));
        Assert.False(ChatService.IsPartyChannel("general"));
        Assert.False(ChatService.IsPartyChannel("say"));
        Assert.False(ChatService.IsPartyChannel("trade"));
    }
}
