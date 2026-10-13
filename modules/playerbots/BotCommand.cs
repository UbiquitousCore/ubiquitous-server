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
}

public static class BotCommand
{
    private static readonly Dictionary<string, BotCommandKind> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["follow"] = BotCommandKind.Follow,
        ["stay"] = BotCommandKind.Stay,
        ["free"] = BotCommandKind.Free,
        ["grind"] = BotCommandKind.Free,
        ["quest"] = BotCommandKind.Free,
        ["attack"] = BotCommandKind.Attack,
        ["leave"] = BotCommandKind.Leave,
        ["report"] = BotCommandKind.Report,
        ["stats"] = BotCommandKind.Report,
    };

    public static BotCommandKind Parse(string text)
    {
        var word = text.Trim().TrimStart('!', '.').Split(' ', 2)[0];
        return Words.GetValueOrDefault(word, BotCommandKind.None);
    }
}
