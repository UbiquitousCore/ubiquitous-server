namespace Ubiquitous.Game.Tests;

public sealed class ChatServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

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
    public void Create_AllowsLocalAndGlobal_JoinedByDefault()
    {
        Assert.Equal(new ChatEvent("local", "Ayla", "hello"),
            new ChatService().Create(new ChatCommand("local", "hello"), "Ayla"));
        Assert.Equal(new ChatEvent("global", "Ayla", "hello"),
            new ChatService().Create(new ChatCommand("global", "hello"), "Ayla"));
    }

    [Fact]
    public void Create_ReadsTheOldGeneralNameAsLocal()
    {
        Assert.Equal(new ChatEvent("local", "Ayla", "hello"),
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
    public void Create_CutsALineAtTheGamesLongest()
    {
        var chat = new ChatService().Create(new ChatCommand("say", new string('a', 300)), "Ayla");
        Assert.Equal(ChatService.MaxLength, chat!.Text.Length);
    }

    [Fact]
    public void Join_RejectsAnUnknownChannel()
    {
        Assert.False(new ChatService().Join("not-a-real-channel"));
    }

    [Theory]
    [InlineData("say")]
    [InlineData("yell")]
    [InlineData("party")]
    [InlineData("guild")]
    public void AlwaysOnChannels_CanBeNeitherJoinedNorLeft(string channel)
    {
        var chat = new ChatService();
        Assert.True(chat.IsJoined(channel));
        Assert.False(chat.Join(channel));
        Assert.False(chat.Leave(channel));
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
        Assert.True(chat.IsJoined("global"));
        Assert.True(chat.Leave("global"));
        Assert.False(chat.IsJoined("global"));
        Assert.Null(chat.Create(new ChatCommand("global", "hello"), "Ayla"));
    }

    [Fact]
    public void Leave_RejectsAnUnknownChannel()
    {
        Assert.False(new ChatService().Leave("not-a-real-channel"));
    }

    [Fact]
    public void RangeOf_SpeakCarries20_YellCarries100_OthersAnyDistance()
    {
        Assert.Equal(20f, ChatService.RangeOf("say"));
        Assert.Equal(100f, ChatService.RangeOf("YELL"));
        Assert.Null(ChatService.RangeOf("local"));
        Assert.Null(ChatService.RangeOf("global"));
        Assert.Null(ChatService.RangeOf("party"));
        Assert.Null(ChatService.RangeOf("guild"));
    }

    [Fact]
    public void IsKnownChannel_RecognizesTheGamesChannels()
    {
        foreach (var channel in new[] { "say", "yell", "party", "guild", "local", "global", "trade", "general" })
            Assert.True(ChatService.IsKnownChannel(channel), channel);
        Assert.False(ChatService.IsKnownChannel("notice"));
        Assert.False(ChatService.IsKnownChannel(""));
        Assert.False(ChatService.IsKnownChannel(null));
    }

    [Fact]
    public void OnlyGlobalReachesEveryMap()
    {
        Assert.True(ChatService.IsServerWide("global"));
        Assert.False(ChatService.IsServerWide("local"));
        Assert.False(ChatService.IsServerWide("say"));
    }

    [Fact]
    public void Party_IsAlwaysJoined_LikeSay()
    {
        Assert.Equal(new ChatEvent("party", "Ayla", "form up"),
            new ChatService().Create(new ChatCommand("party", "form up"), "Ayla"));
    }

    [Theory]
    [InlineData("local")]
    [InlineData("global")]
    public void WideChannels_AllowThreeLinesInTenSeconds(string channel)
    {
        var chat = new ChatService();
        for (var line = 0; line < 3; line++)
            Assert.Equal(ChatRefusal.None, chat.Send(new ChatCommand(channel, "hi"), "Ayla", Start.AddSeconds(line)).Refusal);

        Assert.Equal(ChatRefusal.TooFrequent, chat.Send(new ChatCommand(channel, "hi"), "Ayla", Start.AddSeconds(5)).Refusal);
        Assert.Equal(ChatRefusal.None, chat.Send(new ChatCommand(channel, "hi"), "Ayla", Start.AddSeconds(11)).Refusal);
    }

    [Fact]
    public void NearbyChannels_AllowTenLinesInTenSeconds_SharedBetweenSayAndYell()
    {
        var chat = new ChatService();
        for (var line = 0; line < 10; line++)
            Assert.Equal(ChatRefusal.None,
                chat.Send(new ChatCommand(line % 2 == 0 ? "say" : "yell", "hi"), "Ayla", Start.AddSeconds(line * 0.5)).Refusal);

        Assert.Equal(ChatRefusal.TooFrequent, chat.Send(new ChatCommand("say", "hi"), "Ayla", Start.AddSeconds(6)).Refusal);
    }

    [Fact]
    public void PartyAndGuildChat_AreNotLimited()
    {
        var chat = new ChatService();
        for (var line = 0; line < 30; line++)
        {
            Assert.Equal(ChatRefusal.None, chat.Send(new ChatCommand("party", "hi"), "Ayla", Start).Refusal);
            Assert.Equal(ChatRefusal.None, chat.Send(new ChatCommand("guild", "hi"), "Ayla", Start).Refusal);
        }
    }

    [Fact]
    public void AnExemptSender_IsNotLimited()
    {
        var chat = new ChatService();
        for (var line = 0; line < 10; line++)
            Assert.Equal(ChatRefusal.None,
                chat.Send(new ChatCommand("global", "hi"), "Ayla", Start, exemptFromLimits: true).Refusal);
    }
}
