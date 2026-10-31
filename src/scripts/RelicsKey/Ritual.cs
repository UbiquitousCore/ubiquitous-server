namespace Ubiquitous.Scripts;

// A block of waves and the boss that ends it. The opening line and the first wave are timed from the block's
// start, each later wave from the one before; the warning comes once the boss is out.
public sealed record RitualBlock(
    string Opening,
    TimeSpan OpeningAfter,
    IReadOnlyList<RitualWave> Waves,
    string Warning,
    uint Boss);

public sealed record RitualWave(TimeSpan After, IReadOnlyList<uint> Creatures);

public enum RitualStage
{
    Waiting,
    Escort,
    Summoning,
    Waves,
    BossComing,
    Boss,
    Finishing,
    Done,
    Failed,
}

public sealed record RitualView(TimeSpan InStage, bool KeeperAlive, bool KeeperArrived, int LivingWaveCreatures, bool BossDead);

public sealed record RitualOrders(
    string? Say = null,
    IReadOnlyList<uint>? Wave = null,
    uint? Boss = null,
    uint? KeeperCasts = null,
    bool Completes = false,
    bool Fails = false,
    bool Resets = false);

// The keeper's escort and the ritual it guards, as a sequence of stages. It answers what to do next and
// does nothing itself; the server tells it how long it has been in a stage and what is still standing.
public sealed class Ritual(
    IReadOnlyList<RitualBlock> blocks,
    RitualLines lines,
    uint keeperSpell,
    TimeSpan beforeBoss,
    TimeSpan warningAfterBoss,
    TimeSpan beforeFarewell,
    TimeSpan beforeRetry)
{
    public RitualStage Stage { get; private set; } = RitualStage.Waiting;

    public int Block { get; private set; }

    private int _wavesSent;
    private TimeSpan _nextWaveAt;
    private bool _openingSaid;
    private bool _warningSaid;

    public RitualOrders? Start()
    {
        if (Stage != RitualStage.Waiting) return null;
        Stage = RitualStage.Escort;
        return new RitualOrders(Say: lines.Follow);
    }

    public RitualOrders? Advance(RitualView view)
    {
        if (!view.KeeperAlive
            && Stage is RitualStage.Escort or RitualStage.Summoning or RitualStage.Waves
                or RitualStage.BossComing or RitualStage.Boss)
        {
            Stage = RitualStage.Failed;
            return new RitualOrders(Say: lines.Failed, Fails: true);
        }

        switch (Stage)
        {
            case RitualStage.Escort when view.KeeperArrived:
                Stage = RitualStage.Summoning;
                return new RitualOrders(Say: lines.Starting, KeeperCasts: keeperSpell);

            case RitualStage.Summoning:
                return OpenBlock(0, view);

            case RitualStage.Waves:
                return WavesDue(view);

            case RitualStage.BossComing when view.InStage >= beforeBoss:
                Stage = RitualStage.Boss;
                _warningSaid = false;
                return new RitualOrders(Boss: blocks[Block].Boss);

            case RitualStage.Boss when view.BossDead:
                if (Block + 1 < blocks.Count) return OpenBlock(Block + 1, view with { InStage = TimeSpan.Zero });
                Stage = RitualStage.Finishing;
                return new RitualOrders(Say: lines.Done);

            case RitualStage.Boss when !_warningSaid && view.InStage >= warningAfterBoss:
                _warningSaid = true;
                return new RitualOrders(Say: blocks[Block].Warning);

            case RitualStage.Finishing when view.InStage >= beforeFarewell:
                Stage = RitualStage.Done;
                return new RitualOrders(Say: lines.Farewell, Completes: true);

            case RitualStage.Failed when view.InStage >= beforeRetry:
                Stage = RitualStage.Waiting;
                Block = 0;
                _wavesSent = 0;
                return new RitualOrders(Resets: true);
        }

        return null;
    }

    // A change of stage always answers, even with nothing to do: the server starts the stage's clock on an answer.
    private RitualOrders OpenBlock(int block, RitualView view)
    {
        Block = block;
        Stage = RitualStage.Waves;
        _wavesSent = 0;
        _nextWaveAt = blocks[block].Waves[0].After;
        _openingSaid = false;
        return WavesDue(view) ?? new RitualOrders();
    }

    // The next wave goes out on its timer whether or not the last is still standing; the boss is only called
    // once every wave is out and every creature in them is dead.
    private RitualOrders? WavesDue(RitualView view)
    {
        var current = blocks[Block];
        string? say = null;
        if (!_openingSaid && view.InStage >= current.OpeningAfter)
        {
            _openingSaid = true;
            say = current.Opening;
        }

        IReadOnlyList<uint>? wave = null;
        if (_wavesSent < current.Waves.Count && view.InStage >= _nextWaveAt)
        {
            wave = current.Waves[_wavesSent++].Creatures;
            if (_wavesSent < current.Waves.Count) _nextWaveAt += current.Waves[_wavesSent].After;
        }

        if (say is not null || wave is not null) return new RitualOrders(Say: say, Wave: wave);
        if (_wavesSent < current.Waves.Count || view.LivingWaveCreatures > 0) return null;
        Stage = RitualStage.BossComing;
        return new RitualOrders();
    }
}

public sealed record RitualLines(string Follow, string Starting, string Done, string Farewell, string Failed);
