using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Modules.Playerbots;

// Players do not act every moment, but they do finish what they started: a bot
// only stops between tasks. After a kill, a hand-in or the like it sometimes
// pauses a moment (reading its quest log), now and then stands talking for a
// while, and rarely steps away from the keyboard.
public sealed partial class BotBrain
{
    private const double ShortPauseChance = 0.35;
    private const double ChatBreakChance = 0.10;
    private const double AwayChance = 0.03;
    private static readonly TimeSpan LongBreaksApart = TimeSpan.FromMinutes(3);
    private static readonly HashSet<string> FinishingMessages =
    [
        WebMessageTypes.QuestComplete, WebMessageTypes.QuestAccept, WebMessageTypes.LootClose,
        WebMessageTypes.VendorSell, WebMessageTypes.BusConfirm,
    ];

    private DateTimeOffset _restingUntil;
    private DateTimeOffset _lastLongBreak;
    private DateTimeOffset _nextTalk;
    private bool _chatting;
    private bool _wasFighting;

    public bool OnABreak(DateTimeOffset now) => now < _restingUntil;

    private void NoteFinished(BotDecision decision, bool fighting, DateTimeOffset now)
    {
        var finishedFight = _wasFighting && !fighting;
        _wasFighting = fighting;
        if (finishedFight || decision is SendMessage sent && FinishingMessages.Contains(sent.Type)) BetweenTasks(now);
    }

    private void BetweenTasks(DateTimeOffset now)
    {
        if (!settings.IdleBreaks || now < _restingUntil) return;
        var roll = random.NextDouble();
        var longBreakAllowed = now - _lastLongBreak >= LongBreaksApart;
        _chatting = false;
        if (longBreakAllowed && roll < AwayChance)
        {
            _lastLongBreak = now;
            _restingUntil = now + Seconds(60, 180);
        }
        else if (longBreakAllowed && roll < AwayChance + ChatBreakChance)
        {
            _lastLongBreak = now;
            _chatting = true;
            _nextTalk = now;
            _restingUntil = now + Seconds(8, 40);
        }
        else if (roll < AwayChance + ChatBreakChance + ShortPauseChance)
        {
            _restingUntil = now + Seconds(1, 4);
        }
    }

    private BotDecision? TakeBreak(BotView view, DateTimeOffset now)
    {
        if (!settings.IdleBreaks || view.InCombat || now >= _restingUntil) return null;
        return _chatting && now >= _nextTalk ? Talk(now) : Hold.Instance;
    }

    // Standing talking to someone off-screen: the talk emote every so often.
    private SendMessage Talk(DateTimeOffset now)
    {
        _nextTalk = now + Seconds(6, 15);
        return new SendMessage(WebMessageTypes.PlayerEmote, new EmoteRequestPayload("talk"));
    }

    private TimeSpan Seconds(double least, double most) => TimeSpan.FromSeconds(least + random.NextDouble() * (most - least));
}
