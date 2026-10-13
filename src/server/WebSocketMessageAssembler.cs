namespace Ubiquitous.Server;

public sealed class MessageTooLargeException : Exception
{
    public MessageTooLargeException(string message)
        : base(message)
    {
    }
}

public sealed class WebSocketMessageAssembler
{
    public const int MaxMessageBytes = 256 * 1024;

    private readonly MemoryStream _buffer = new();
    private readonly int _maxMessageBytes;

    public WebSocketMessageAssembler(int maxMessageBytes = MaxMessageBytes)
    {
        if (maxMessageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessageBytes));
        }

        _maxMessageBytes = maxMessageBytes;
    }

    public string? Append(ReadOnlySpan<byte> chunk, bool endOfMessage)
    {
        if (_buffer.Length + chunk.Length > _maxMessageBytes)
        {
            _buffer.SetLength(0);
            throw new MessageTooLargeException(
                $"Text message exceeds the {_maxMessageBytes}-byte limit.");
        }

        _buffer.Write(chunk);
        if (!endOfMessage)
        {
            return null;
        }

        var complete = System.Text.Encoding.UTF8.GetString(_buffer.ToArray());
        _buffer.SetLength(0);
        return complete;
    }
}
