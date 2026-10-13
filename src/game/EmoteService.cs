namespace Ubiquitous.Game;

public sealed record EmoteDefinition(int Id, string Name);

public sealed record EmoteRequest(string Name);

public sealed record EmoteEvent(string SenderId, int Id, string Name);

public sealed class EmoteService
{
    public static readonly IReadOnlyList<EmoteDefinition> All =
    [
        new(120, "applaud"),
        new(121, "beg"),
        new(122, "bow"),
        new(123, "cheer"),
        new(124, "cry"),
        new(125, "dance"),
        new(129, "flex"),
        new(130, "kiss"),
        new(131, "laugh"),
        new(132, "no"),
        new(133, "point"),
        new(134, "rude"),
        new(135, "salute"),
        new(136, "shout"),
        new(137, "shy"),
        new(138, "talk"),
        new(141, "wave"),
        new(142, "work"),
        new(143, "yes"),
        new(165, "roar"),
        new(169, "whistle"),
    ];

    private static readonly Dictionary<string, EmoteDefinition> ByName =
        All.ToDictionary(emote => emote.Name, StringComparer.OrdinalIgnoreCase);

    public EmoteEvent? Create(EmoteRequest request, string senderId)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(senderId)) return null;
        return ByName.TryGetValue(request.Name.Trim(), out var definition)
            ? new EmoteEvent(senderId, definition.Id, definition.Name)
            : null;
    }
}
