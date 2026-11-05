using System.Globalization;

namespace Ubiquitous.Admin;

public enum SettingKind
{
    Switch,
    Number,
    WholeNumber,
    Text,
    Choice,
    RaceList,
}

public sealed record Setting(
    string Name,
    string Label,
    SettingKind Kind,
    string Default,
    string Help,
    double Min = 0,
    double Max = double.MaxValue,
    IReadOnlyList<string>? Choices = null,
    bool NeedsRecreate = false);

// The settings a host changes from the admin page. Anything else in the file
// is left exactly as it is: the page never writes a name it does not list.
public static class SettingCatalog
{
    public static readonly IReadOnlyList<Setting> All =
    [
        new("UBIQUITOUS_SITE", "Address", SettingKind.Text, "localhost",
            "The address other devices open: this machine's LAN address, or a domain. Applies after docker compose up -d, not a restart.",
            NeedsRecreate: true),
        new("UBIQUITOUS_FESTIVAL_CONTENT_ENABLED", "Festival content", SettingKind.Switch, "false",
            "Seasonal scenery, creatures and items."),
        new("UBIQUITOUS_MODULE_PLAYERBOTS_ENABLED", "Bots", SettingKind.Switch, "false",
            "Characters the server plays itself."),
        new("UBIQUITOUS_MODULE_PLAYERBOTS_RANDOM_BOT_COUNT", "Bot count", SettingKind.WholeNumber, "5",
            "How many bots play on their own.", 0, 500),
        new("UBIQUITOUS_XP_RATE", "Experience rate", SettingKind.Number, "1",
            "Multiplies all experience gained.", 0, 100),
        new("UBIQUITOUS_PLAYER_DAMAGE_RATE", "Player damage rate", SettingKind.Number, "1",
            "Multiplies the damage players deal.", 0, 100),
        new("UBIQUITOUS_CREATURE_DAMAGE_RATE", "Creature damage rate", SettingKind.Number, "1",
            "Multiplies the damage creatures deal.", 0, 100),
        new("UBIQUITOUS_CREATURE_HEALTH_RATE", "Creature health rate", SettingKind.Number, "1",
            "Multiplies every creature's health.", 0, 100),
        new("UBIQUITOUS_EXHAUSTION_LIMIT", "Exhaustion limit", SettingKind.WholeNumber, "100",
            "Legend exhaustion a player can build up before the daily reset.", 0, 100_000),
        new("UBIQUITOUS_DISABLED_RACES", "Closed races", SettingKind.RaceList, "7",
            "Race ids that cannot be picked, comma-separated; none opens every race. 7 is Mendel, which 1.1.4 cannot play."),
        new("UBIQUITOUS_MAX_CONNECTIONS", "Most players at once", SettingKind.WholeNumber, "500",
            "Players connected at once.", 1, 100_000),
        new("UBIQUITOUS_LOG_LEVEL", "Log level", SettingKind.Choice, "Information",
            "How much the server writes to its log.",
            Choices: ["Trace", "Debug", "Information", "Warning", "Error", "Critical"]),
    ];

    public static Setting? Find(string name) =>
        All.FirstOrDefault(setting => string.Equals(setting.Name, name, StringComparison.Ordinal));

    // Null when the value is acceptable; otherwise what is wrong with it. Blank always is: it means the default.
    public static string? Problem(Setting setting, string value)
    {
        var text = value.Trim();
        if (text.Length == 0) return null;
        switch (setting.Kind)
        {
            case SettingKind.Switch:
                return text is "true" or "false" ? null : "Must be true or false.";
            case SettingKind.Number:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    && number >= setting.Min && number <= setting.Max
                    ? null
                    : $"Must be a number from {setting.Min} to {setting.Max}.";
            case SettingKind.WholeNumber:
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
                    && whole >= setting.Min && whole <= setting.Max
                    ? null
                    : $"Must be a whole number from {setting.Min} to {setting.Max}.";
            case SettingKind.Choice:
                return setting.Choices!.Contains(text, StringComparer.Ordinal)
                    ? null
                    : $"Must be one of {string.Join(", ", setting.Choices!)}.";
            case SettingKind.RaceList:
                if (text.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
                return text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .All(part => int.TryParse(part, out var race) && race >= 0)
                    ? null
                    : "Must be race ids separated by commas, or none.";
            default:
                return text.Contains('\n') || text.Contains('\r') ? "Must be one line." : null;
        }
    }
}
