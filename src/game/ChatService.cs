namespace Ubiquitous.Game;

public sealed record ChatCommand(string Channel, string Text);
public sealed record ChatEvent(string Channel, string Sender, string Text);

public sealed class ChatService
{
    public const string Say = "say";
    public const string General = "general";
    public const string Trade = "trade";
    public const string Party = "party";

    public const float SayRange = 20f;

    private static readonly HashSet<string> KnownChannels =
        new(StringComparer.OrdinalIgnoreCase) { Say, General, Trade, Party };

    private static readonly HashSet<string> AlwaysJoined =
        new(StringComparer.OrdinalIgnoreCase) { Say, Party };

    private readonly HashSet<string> _joined = new(StringComparer.OrdinalIgnoreCase) { General };

    public static bool IsKnownChannel(string? channel) =>
        !string.IsNullOrWhiteSpace(channel) && KnownChannels.Contains(channel.Trim());

    public static bool IsRangeLimited(string channel) =>
        string.Equals(channel.Trim(), Say, StringComparison.OrdinalIgnoreCase);

    public static bool IsPartyChannel(string channel) =>
        string.Equals(channel.Trim(), Party, StringComparison.OrdinalIgnoreCase);

    public bool IsJoined(string channel)
    {
        var normalized = channel.Trim();
        return AlwaysJoined.Contains(normalized) || _joined.Contains(normalized);
    }

    public bool Join(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return false;
        var normalized = channel.Trim();
        if (!IsKnownChannel(normalized) || AlwaysJoined.Contains(normalized)) return false;
        _joined.Add(normalized);
        return true;
    }

    public bool Leave(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return false;
        var normalized = channel.Trim();
        if (!IsKnownChannel(normalized) || AlwaysJoined.Contains(normalized)) return false;
        _joined.Remove(normalized);
        return true;
    }

    public ChatEvent? Create(ChatCommand command, string sender)
    {
        if (string.IsNullOrWhiteSpace(command.Channel)
            || string.IsNullOrWhiteSpace(command.Text)
            || string.IsNullOrWhiteSpace(sender)) return null;

        var channel = command.Channel.Trim();
        if (!IsKnownChannel(channel) || !IsJoined(channel)) return null;

        var text = command.Text.Trim();
        if (text.Length > 256) text = text[..256];
        return new ChatEvent(channel.ToLowerInvariant(), sender, text);
    }
}
