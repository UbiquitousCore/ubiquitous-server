using Ubiquitous.Data;

namespace Ubiquitous.Game;

public enum TalentRefusal
{
    UnknownTalent,
    WrongClass,
    MaxRank,
    NoPoints,
    TierLocked,
    Prerequisite,
}

public sealed class TalentBook
{
    public const int FirstPointLevel = 10;

    private readonly Dictionary<uint, (TalentTreeDefinition Tree, TalentDefinition Talent)> _talents = new();
    private readonly Dictionary<uint, int> _ranks = new();

    public TalentBook(IReadOnlyList<TalentTreeDefinition> trees)
    {
        foreach (var tree in trees)
        {
            foreach (var talent in tree.Talents) _talents[talent.Id] = (tree, talent);
        }
    }

    public static int PointsForLevel(int level) =>
        Math.Max(0, Math.Min(level, LevelingService.MaxLevel) - FirstPointLevel + 1);

    public IReadOnlyDictionary<uint, int> Ranks => _ranks;

    public int Spent => _ranks.Values.Sum();

    public int Unspent(int level) => Math.Max(0, PointsForLevel(level) - Spent);

    public IReadOnlyList<uint> CurrentSpells =>
        [.. _ranks.Select(pair => _talents[pair.Key].Talent.RankSpellIds[pair.Value - 1])];

    public TalentRefusal? Learn(uint talentId, int classId, int level)
    {
        if (!_talents.TryGetValue(talentId, out var found)) return TalentRefusal.UnknownTalent;
        var (tree, talent) = found;
        if (tree.ClassId != classId) return TalentRefusal.WrongClass;

        var rank = _ranks.GetValueOrDefault(talentId);
        if (rank >= talent.MaxRank) return TalentRefusal.MaxRank;
        if (Unspent(level) <= 0) return TalentRefusal.NoPoints;
        if (SpentIn(tree) < talent.PointsInTreeRequired) return TalentRefusal.TierLocked;
        if (talent.Prerequisites.Any(needed => _ranks.GetValueOrDefault(needed) < _talents[needed].Talent.MaxRank))
            return TalentRefusal.Prerequisite;

        _ranks[talentId] = rank + 1;
        return null;
    }

    public void Reset() => _ranks.Clear();

    public void Restore(IEnumerable<KeyValuePair<uint, int>> saved, int classId, int level)
    {
        _ranks.Clear();
        var wanted = saved
            .Where(pair => _talents.ContainsKey(pair.Key) && pair.Value > 0)
            .OrderBy(pair => _talents[pair.Key].Talent.Tier)
            .ThenBy(pair => _talents[pair.Key].Talent.Column)
            .ToList();
        bool progressed;
        do
        {
            progressed = false;
            foreach (var (talentId, rank) in wanted)
            {
                while (_ranks.GetValueOrDefault(talentId) < rank && Learn(talentId, classId, level) is null)
                    progressed = true;
            }
        }
        while (progressed);
    }

    private int SpentIn(TalentTreeDefinition tree) =>
        tree.Talents.Sum(talent => _ranks.GetValueOrDefault(talent.Id));
}
