using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ubiquitous.Data;

namespace Ubiquitous.Modules.Playerbots;

public sealed class BotManager(IModuleHost host, PlayerbotSettings settings)
{
    private readonly List<Bot> _bots = [];
    private readonly Dictionary<int, Shift> _shifts = [];
    private readonly ConcurrentDictionary<string, byte> _names = new(StringComparer.Ordinal);
    private readonly BotKnowledge _knowledge = new(host.Content, host.CreatureSpawns,
        (map, at) => !host.HasWalkableGround(map) || host.WalkableAreaUnder(map, at) >= Bot.MainGroundArea);
    private int _started;
    private long _turn;

    // A little past a client's view range, so a bot is back to every turn before a player can see it.
    public const float WatchedWithin = 80f;

    // Half the bots start in the human and elf lands, half in the orc and undead ones.
    private static readonly IReadOnlySet<int>[] RaceHalves =
    [
        new HashSet<int> { PlayerRaceIds.Human, PlayerRaceIds.Elf },
        new HashSet<int> { PlayerRaceIds.Orc, PlayerRaceIds.Undead },
    ];

    public IReadOnlyList<Bot> Bots => _bots;

    // When a random bot logs off, and when it comes back. Its first stint is any part of a whole one,
    // so bots that all logged in at startup leave at different times.
    private sealed class Shift(int index, ModuleLogin login, Random random, int startLevel,
        (ushort MapId, Ubiquitous.Common.Position At)? startPlace)
    {
        public (ushort MapId, Ubiquitous.Common.Position At)? StartPlace { get; } = startPlace;
        public int Index { get; } = index;
        public ModuleLogin Login { get; } = login;
        public Random Random { get; } = random;
        public int StartLevel { get; } = startLevel;
        public Bot? Bot { get; set; }
        public DateTimeOffset? LeaveAt { get; set; }
        public DateTimeOffset? BackAt { get; set; }
        public bool FirstStint { get; set; } = true;
    }

    public bool IsBot(string name) => _names.ContainsKey(name);

    public async Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var logins = 0; logins < settings.LoginsPerTick && _started < settings.RandomBotCount; logins++)
            await StartAsync(_started++, cancellationToken);

        if (settings.LogInAndOut) await KeepHoursAsync(now, cancellationToken);

        var turn = _turn++;
        for (var index = 0; index < _bots.Count; index++)
        {
            var bot = _bots[index];
            if (bot.Failed) continue;
            if (!PlaysThisTurn(turn, index, settings.UnwatchedTurnEvery, bot.Playing,
                    () => host.IsPlayerNear(bot.View.MapId, bot.View.Position, WatchedWithin)))
                continue;
            try
            {
                await bot.TickAsync(now, cancellationToken);
                if (bot.Name is { } name) _names.TryAdd(name, 0);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                host.Logger.LogError(ex, "Bot {Name} turn failed", bot.Name);
            }
        }
    }

    // A bot no player is near plays every few turns, each on its own beat so the work spreads out.
    internal static bool PlaysThisTurn(long turn, int index, int unwatchedEvery, bool inWorld, Func<bool> watched) =>
        unwatchedEvery <= 1
        || !inWorld
        || (turn + index) % unwatchedEvery == 0
        || watched();

    private async Task StartAsync(int index, CancellationToken cancellationToken)
    {
        var login = await host.ReserveLoginAsync($"random{index}", cancellationToken);
        var random = new Random(unchecked(settings.Seed + index));
        var level = random.Next(settings.MinLevel, settings.MaxLevel + 1);
        var shift = new Shift(index, login, random, level, _knowledge.StartFor(level, random, BotBrain.AreaRadius, BotBrain.FitSlack));
        _shifts[index] = shift;
        LogIn(shift);
    }

    private void LogIn(Shift shift)
    {
        shift.Bot = new Bot(host, host.Connect(), shift.Login, new BotBrain(_knowledge, settings, shift.Random, IsBot), shift.Random,
            RaceHalves[shift.Index % RaceHalves.Length], shift.StartLevel, shift.StartPlace);
        _bots.Add(shift.Bot);
    }

    // A bot plays a stint and logs off for a while, as players come and go over a day. One in a
    // fight or a group stays until it is out of it, so nobody's party loses a member mid-pull.
    private async Task KeepHoursAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var shift in _shifts.Values)
        {
            if (shift.Bot is { } bot)
            {
                if (!bot.Playing) continue;
                if (shift.LeaveAt is null)
                {
                    var stint = Minutes(shift.Random, settings.OnlineMinutesMin, settings.OnlineMinutesMax);
                    shift.LeaveAt = now + (shift.FirstStint ? stint * shift.Random.NextDouble() : stint);
                    shift.FirstStint = false;
                }

                if (now < shift.LeaveAt || bot.View.InCombat || bot.View.InParty) continue;
                await bot.DisconnectAsync();
                _bots.Remove(bot);
                shift.Bot = null;
                shift.LeaveAt = null;
                shift.BackAt = now + Minutes(shift.Random, settings.OfflineMinutesMin, settings.OfflineMinutesMax);
                host.Logger.LogInformation("Bot {Name} logged off", bot.Name);
            }
            else if (shift.BackAt is { } back && now >= back)
            {
                shift.BackAt = null;
                LogIn(shift);
            }
        }
    }

    private static TimeSpan Minutes(Random random, int min, int max) =>
        TimeSpan.FromMinutes(min + random.NextDouble() * (max - min));

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        foreach (var bot in _bots) await bot.DisconnectAsync();
        _bots.Clear();
    }
}
