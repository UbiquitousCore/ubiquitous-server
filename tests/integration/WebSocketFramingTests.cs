using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class WebSocketFramingTests
{
    [Fact]
    public void SingleFrame_ReturnsCompleteMessage()
    {
        var assembler = new WebSocketMessageAssembler();
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");

        var complete = assembler.Append(bytes, endOfMessage: true);

        Assert.Equal("{\"type\":\"ping\"}", complete);
    }

    [Fact]
    public void FragmentedFrames_AssembleBeforeReturningMessage()
    {
        var assembler = new WebSocketMessageAssembler();
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"type\":\"player.move\"}");

        var first = assembler.Append(bytes.AsSpan(0, 8), endOfMessage: false);
        var second = assembler.Append(bytes.AsSpan(8), endOfMessage: true);

        Assert.Null(first);
        Assert.Equal("{\"type\":\"player.move\"}", second);
    }

    [Fact]
    public void MultibyteCharacterSplitAcrossFrames_DecodesCorrectly()
    {
        var assembler = new WebSocketMessageAssembler();
        var bytes = System.Text.Encoding.UTF8.GetBytes("h\u00e9llo");
        var split = 2;

        Assert.Null(assembler.Append(bytes.AsSpan(0, split), endOfMessage: false));
        Assert.Equal("h\u00e9llo", assembler.Append(bytes.AsSpan(split), endOfMessage: true));
    }

    [Fact]
    public void OversizeMessage_ThrowsAndResets()
    {
        var assembler = new WebSocketMessageAssembler(maxMessageBytes: 8);
        var bytes = System.Text.Encoding.UTF8.GetBytes("123456789");

        Assert.Throws<MessageTooLargeException>(() => assembler.Append(bytes, endOfMessage: true));

        Assert.Equal("ok", assembler.Append(System.Text.Encoding.UTF8.GetBytes("ok"), endOfMessage: true));
    }
}
