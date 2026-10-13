namespace Ubiquitous.Game.Tests;

public sealed class ChatWhisperServiceTests
{
    [Fact]
    public void Validate_RefusesWhisperingYourself()
    {
        var whispers = new ChatWhisperService();

        var result = whispers.Validate("player:a", "player:a", "hello");

        Assert.Equal(ChatWhisperService.ValidateOutcome.SelfWhisper, result.Outcome);
        Assert.Null(result.Event);
    }

    [Fact]
    public void Validate_RefusesAMissingTarget()
    {
        var whispers = new ChatWhisperService();

        Assert.Equal(ChatWhisperService.ValidateOutcome.MissingTarget, whispers.Validate("player:a", "", "hello").Outcome);
        Assert.Equal(ChatWhisperService.ValidateOutcome.MissingTarget, whispers.Validate("player:a", null, "hello").Outcome);
        Assert.Equal(ChatWhisperService.ValidateOutcome.MissingTarget, whispers.Validate("player:a", "   ", "hello").Outcome);
    }

    [Fact]
    public void Validate_MissingTargetIsCheckedBeforeSelfWhisper()
    {
        var whispers = new ChatWhisperService();

        Assert.Equal(ChatWhisperService.ValidateOutcome.MissingTarget, whispers.Validate("", "", "hello").Outcome);
    }

    [Fact]
    public void Validate_RefusesEmptyText()
    {
        var whispers = new ChatWhisperService();

        Assert.Equal(ChatWhisperService.ValidateOutcome.EmptyText, whispers.Validate("player:a", "player:b", "").Outcome);
        Assert.Equal(ChatWhisperService.ValidateOutcome.EmptyText, whispers.Validate("player:a", "player:b", "   ").Outcome);
        Assert.Equal(ChatWhisperService.ValidateOutcome.EmptyText, whispers.Validate("player:a", "player:b", null).Outcome);
    }

    [Fact]
    public void Validate_TrimsText()
    {
        var whispers = new ChatWhisperService();

        var result = whispers.Validate("player:a", "player:b", "  hello  ");

        Assert.Equal(ChatWhisperService.ValidateOutcome.Ok, result.Outcome);
        Assert.Equal(new WhisperEvent("player:b", "hello"), result.Event);
    }

    [Fact]
    public void Validate_ClampsTextToTwoHundredFiftySixCharacters()
    {
        var whispers = new ChatWhisperService();
        var longText = new string('a', 300);

        var result = whispers.Validate("player:a", "player:b", longText);

        Assert.Equal(256, result.Event!.Text.Length);
    }
}
