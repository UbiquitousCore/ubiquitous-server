using Ubiquitous.Game;

namespace Ubiquitous.Modules.Playerbots;

public sealed record PlayerbotSettings(
    bool Enabled,
    int RandomBotCount,
    int LoginsPerTick,
    bool AcceptPartyInvites,
    bool GroupWithBots,
    bool AcceptDuels,
    int DoubleUpPercent,
    bool IdleBreaks,
    LootStyle? LootStyle,
    int GrindLevelsAbove,
    int Seed,
    int UnwatchedTurnEvery,
    int MinLevel = 1,
    int MaxLevel = 1,
    bool LogInAndOut = false,
    int OnlineMinutesMin = 40,
    int OnlineMinutesMax = 240,
    int OfflineMinutesMin = 10,
    int OfflineMinutesMax = 90)
{
    public const int MostRandomBots = 500;

    public static PlayerbotSettings From(ModuleSettings settings)
    {
        var count = settings.Number("randomBotCount", 5);
        if (count is < 0 or > MostRandomBots)
            throw new InvalidDataException($"playerbots randomBotCount must be between 0 and {MostRandomBots}.");
        var minLevel = Math.Clamp(settings.Number("minLevel", 1), 1, LevelingService.MaxLevel);
        var onlineMin = Math.Max(1, settings.Number("onlineMinutesMin", 40));
        var offlineMin = Math.Max(1, settings.Number("offlineMinutesMin", 10));
        return new PlayerbotSettings(
            Enabled: settings.Flag("enabled", false),
            RandomBotCount: count,
            LoginsPerTick: Math.Max(1, settings.Number("loginsPerTick", 1)),
            AcceptPartyInvites: settings.Flag("acceptPartyInvites", true),
            GroupWithBots: settings.Flag("groupWithBots", false),
            AcceptDuels: settings.Flag("acceptDuels", true),
            DoubleUpPercent: Math.Clamp(settings.Number("doubleUpPercent", 10), 0, 100),
            IdleBreaks: settings.Flag("idleBreaks", true),
            LootStyle: settings.Text("lootStyle") switch
            {
                null or "mixed" => null,
                var named when Enum.TryParse<LootStyle>(named, ignoreCase: true, out var style) => style,
                var other => throw new InvalidDataException($"playerbots lootStyle must be mixed, hoarder, ordinary or picky, not '{other}'."),
            },
            GrindLevelsAbove: settings.Number("grindLevelsAbove", 1),
            Seed: settings.Number("seed", Environment.TickCount),
            UnwatchedTurnEvery: Math.Clamp(settings.Number("unwatchedTurnEvery", 2), 1, 2),
            MinLevel: minLevel,
            MaxLevel: Math.Max(minLevel, Math.Clamp(settings.Number("maxLevel", 20), 1, LevelingService.MaxLevel)),
            LogInAndOut: settings.Flag("logInAndOut", true),
            OnlineMinutesMin: onlineMin,
            OnlineMinutesMax: Math.Max(onlineMin, settings.Number("onlineMinutesMax", 240)),
            OfflineMinutesMin: offlineMin,
            OfflineMinutesMax: Math.Max(offlineMin, settings.Number("offlineMinutesMax", 90)));
    }
}
