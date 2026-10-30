namespace Ubiquitous.Game;

public sealed record ChatCommand(string Channel, string Text);
public sealed record ChatEvent(string Channel, string Sender, string Text);

/// <summary>Why a line was not sent; <see cref="None"/> when it was.</summary>
public enum ChatRefusal
{
    None,
    Invalid,
    TooFrequent,
}

public sealed record ChatOutcome(ChatEvent? Event, ChatRefusal Refusal);

/// <summary>
/// Channels and their audiences, as the field guide records the game's chat:
/// Speak reaches 20 m, Yell 100 m, Team the party, Guild the guild, Local the
/// region (this server's map) and Global everyone online.
/// </summary>
public sealed class ChatService
{
    public const string Say = "say";
    public const string Yell = "yell";
    public const string Party = "party";
    public const string Guild = "guild";
    public const string Local = "local";
    public const string Global = "global";
    public const string Trade = "trade";

    /// <summary>The name older clients used for <see cref="Local"/>; read as it.</summary>
    public const string General = "general";

    // Sent by the server only, so it is not a known channel a player may send on.
    public const string Notice = "notice";

    public const float SayRange = 20f;
    public const float YellRange = 100f;

    /// <summary>The game's longest chat line.</summary>
    public const int MaxLength = 255;

    /// <summary>The game's flood limits: so many lines in each window.</summary>
    public static readonly TimeSpan FloodWindow = TimeSpan.FromSeconds(10);
    public const int WideChannelLines = 3;
    public const int NearbyLines = 10;

    private static readonly HashSet<string> KnownChannels =
        new(StringComparer.OrdinalIgnoreCase) { Say, Yell, Party, Guild, Local, Global, Trade };

    private static readonly HashSet<string> AlwaysJoined =
        new(StringComparer.OrdinalIgnoreCase) { Say, Yell, Party, Guild };

    private readonly HashSet<string> _joined = new(StringComparer.OrdinalIgnoreCase) { Local, Global };

    private readonly FloodCounter _wide = new(WideChannelLines);
    private readonly FloodCounter _nearby = new(NearbyLines);

    /// <summary>A channel name as sent, trimmed, lowered and with the old alias resolved.</summary>
    public static string Normalize(string? channel)
    {
        var trimmed = (channel ?? string.Empty).Trim().ToLowerInvariant();
        return trimmed == General ? Local : trimmed;
    }

    public static bool IsKnownChannel(string? channel) =>
        !string.IsNullOrWhiteSpace(channel) && KnownChannels.Contains(Normalize(channel));

    /// <summary>How far the channel carries, or null when distance does not matter.</summary>
    public static float? RangeOf(string channel) => Normalize(channel) switch
    {
        Say => SayRange,
        Yell => YellRange,
        _ => null,
    };

    public static bool IsRangeLimited(string channel) => RangeOf(channel) is not null;

    public static bool IsPartyChannel(string channel) => Normalize(channel) == Party;

    public static bool IsGuildChannel(string channel) => Normalize(channel) == Guild;

    /// <summary>Reaches every map, not only the sender's.</summary>
    public static bool IsServerWide(string channel) => Normalize(channel) == Global;

    public bool IsJoined(string channel)
    {
        var normalized = Normalize(channel);
        return AlwaysJoined.Contains(normalized) || _joined.Contains(normalized);
    }

    public bool Join(string channel)
    {
        var normalized = Normalize(channel);
        if (!IsKnownChannel(normalized) || AlwaysJoined.Contains(normalized)) return false;
        _joined.Add(normalized);
        return true;
    }

    public bool Leave(string channel)
    {
        var normalized = Normalize(channel);
        if (!IsKnownChannel(normalized) || AlwaysJoined.Contains(normalized)) return false;
        _joined.Remove(normalized);
        return true;
    }

    /// <summary>The line to deliver with no flood limit applied, or null.</summary>
    public ChatEvent? Create(ChatCommand command, string sender) =>
        Send(command, sender, DateTimeOffset.UtcNow, exemptFromLimits: true).Event;

    /// <summary>
    /// The line to deliver, or why not. Local, Global and Trade allow three
    /// lines in ten seconds and Say and Yell ten; party and guild chat are not
    /// limited, and nor are admins.
    /// </summary>
    public ChatOutcome Send(ChatCommand command, string sender, DateTimeOffset now, bool exemptFromLimits = false)
    {
        if (string.IsNullOrWhiteSpace(command.Channel)
            || string.IsNullOrWhiteSpace(command.Text)
            || string.IsNullOrWhiteSpace(sender)) return new ChatOutcome(null, ChatRefusal.Invalid);

        var channel = Normalize(command.Channel);
        if (!IsKnownChannel(channel) || !IsJoined(channel)) return new ChatOutcome(null, ChatRefusal.Invalid);

        if (!exemptFromLimits && LimitFor(channel) is { } limit && !limit.TryTake(now))
            return new ChatOutcome(null, ChatRefusal.TooFrequent);

        var text = command.Text.Trim();
        if (text.Length > MaxLength) text = text[..MaxLength];
        return new ChatOutcome(new ChatEvent(channel, sender, text), ChatRefusal.None);
    }

    private FloodCounter? LimitFor(string channel) => channel switch
    {
        Local or Global or Trade => _wide,
        Say or Yell => _nearby,
        _ => null,
    };

    /// <summary>
    /// The game's counter: a window opens on the first line and allows so many
    /// lines before it closes; a line after it has closed opens a new one.
    /// </summary>
    private sealed class FloodCounter(int lines)
    {
        private DateTimeOffset _opened = DateTimeOffset.MinValue;
        private int _count;

        public bool TryTake(DateTimeOffset now)
        {
            if (now - _opened > FloodWindow)
            {
                _opened = now;
                _count = 1;
                return true;
            }

            if (_count >= lines) return false;
            _count++;
            return true;
        }
    }
}
