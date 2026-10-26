namespace Ubiquitous.Modules.Playerbots;

public enum BotCommandKind
{
    None,
    Follow,
    Stay,
    Free,
    Attack,
    Leave,
    Report,
    Help,
}

public static class BotCommand
{
    private static readonly (BotCommandKind Kind, string Word, string Usage)[] Commands =
    [
        (BotCommandKind.Follow, "follow", "follow the leader"),
        (BotCommandKind.Stay, "stay", "stand still"),
        (BotCommandKind.Free, "free", "play on your own again"),
        (BotCommandKind.Attack, "attack", "attack the leader's target"),
        (BotCommandKind.Leave, "leave", "leave the group"),
        (BotCommandKind.Report, "report", "say level, health and quest count"),
        (BotCommandKind.Help, "help", "list these commands"),
    ];

    private static readonly Dictionary<string, BotCommandKind> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["grind"] = BotCommandKind.Free,
        ["quest"] = BotCommandKind.Free,
        ["stats"] = BotCommandKind.Report,
        ["commands"] = BotCommandKind.Help,
    };

    static BotCommand()
    {
        foreach (var (kind, word, _) in Commands) Words[word] = kind;
    }

    public static string Help() =>
        "Here are the commands you can use: "
        + string.Join(", ", Commands.Select(command => $"{command.Word} ({command.Usage})"));

    public static BotCommandKind Parse(string text)
    {
        var word = text.Trim().TrimStart('!', '.', '/').Split(' ', 2)[0];
        return Words.GetValueOrDefault(word, BotCommandKind.None);
    }
}
