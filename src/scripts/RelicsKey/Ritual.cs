namespace Ubiquitous.Scripts;

public sealed record RitualBlock(string Opening, IReadOnlyList<IReadOnlyList<uint>> Waves, string Warning, uint Boss);

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
    TimeSpan waveEvery,
    TimeSpan beforeFirstWave,
    TimeSpan beforeBoss,
    TimeSpan beforeFarewell,
    TimeSpan beforeRetry)
{
    public RitualStage Stage { get; private set; } = RitualStage.Waiting;

    public int Block { get; private set; }

    private int _wavesSent;

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

            case RitualStage.Summoning when view.InStage >= beforeFirstWave:
                return OpenBlock(0);

            case RitualStage.Waves when _wavesSent < blocks[Block].Waves.Count && view.InStage >= waveEvery * _wavesSent:
                return new RitualOrders(Wave: blocks[Block].Waves[_wavesSent++]);

            case RitualStage.Waves when _wavesSent == blocks[Block].Waves.Count && view.LivingWaveCreatures == 0:
                Stage = RitualStage.BossComing;
                return new RitualOrders(Say: blocks[Block].Warning);

            case RitualStage.BossComing when view.InStage >= beforeBoss:
                Stage = RitualStage.Boss;
                return new RitualOrders(Boss: blocks[Block].Boss);

            case RitualStage.Boss when view.BossDead:
                if (Block + 1 < blocks.Count) return OpenBlock(Block + 1);
                Stage = RitualStage.Finishing;
                return new RitualOrders(Say: lines.Done);

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

    private RitualOrders OpenBlock(int block)
    {
        Block = block;
        Stage = RitualStage.Waves;
        _wavesSent = 1;
        return new RitualOrders(Say: blocks[block].Opening, Wave: blocks[block].Waves[0]);
    }
}

public sealed record RitualLines(string Follow, string Starting, string Done, string Farewell, string Failed);
