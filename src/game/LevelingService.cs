namespace Ubiquitous.Game;

public static class LevelingService
{
    /// <summary>The cap through 1.1.5, where every level's values were read in play.</summary>
    public const int FirstCap = 60;

    /// <summary>The loaded build's level cap (`UseCapOf`).</summary>
    public static int MaxLevel { get; private set; } = FirstCap;

    /// <summary>
    /// The cap each build's content reaches: quests stop and gear gathers at
    /// 60 through 1.1.5, 70 in 2.2.0 and 2.5.0, 75 in 4.2.21.
    /// </summary>
    public static void UseCapOf(string gameVersion) =>
        MaxLevel = gameVersion.Split('.')[0] switch
        {
            "2" => 70,
            "4" => 75,
            _ => FirstCap,
        };

    private static readonly int[] XpBarByLevel =
    [
        250, 600, 1050, 1600, 2250, 3000, 3850, 4800, 5850, 7000,
        8150, 9400, 10750, 12100, 13500, 14900, 16350, 17800, 19300, 20800,
        22250, 23700, 25150, 26600, 27950, 29300, 30650, 31850, 33050, 34200,
        41300, 48700, 56350, 64250, 72450, 80850, 89550, 98500, 107700, 117200,
        126900, 136900, 147150, 157650, 168450, 179450, 190750, 202300, 214100, 226200,
        238500, 251100, 263950, 277050, 290450, 304050, 317950, 332100, 346500,
        // Own choice: past 60 each bar grows by 300 more than the one before,
        // continuing the last steps read in play.
        361200, 376200, 391500, 407100, 423000, 439200, 455700, 472500, 489600, 507000,
        524700, 542700, 561000, 579600, 598500,
    ];

    public static int XpToNextLevel(int level) =>
        level >= 1 && level < MaxLevel ? XpBarByLevel[level - 1] : 0;

    private static readonly (int MaxGap, int Percent)[] QuestRewardBands =
    [
        (-9, 10),
        (-5, 50),
        (-2, 80),
        (2, 100),
        (5, 110),
    ];

    private const int QuestRewardPercentWellAbove = 120;

    public static int QuestXp(int rewardXp, int questOwnLevel, int playerLevel, bool isRepeatable)
    {
        if (rewardXp <= 0) return 0;

        var percent = isRepeatable ? 100 : QuestRewardPercent(questOwnLevel - playerLevel);
        return (int)((long)rewardXp * percent / 100);
    }

    private static int QuestRewardPercent(int gap)
    {
        foreach (var (maxGap, percent) in QuestRewardBands)
        {
            if (gap <= maxGap) return percent;
        }

        return QuestRewardPercentWellAbove;
    }

    public static int KillXp(int playerLevel, int creatureLevel, bool elite = false) =>
        KillXp(playerLevel, creatureLevel) * (elite ? 2 : 1);

    public static int KillXp(int playerLevel, int creatureLevel)
    {
        var baseXp = 5 * playerLevel + 50;
        var levelsAbove = creatureLevel - playerLevel;

        if (levelsAbove >= 0)
        {
            return RoundedShare(baseXp, 100 + 5 * Math.Min(levelsAbove, 10), 100);
        }

        var levelsBelow = -levelsAbove;
        if (levelsBelow <= 2)
        {
            return RoundedShare(baseXp, 100 - 10 * levelsBelow, 100);
        }

        var greyWindow = Math.Max(5, (playerLevel + 1) / 5);
        if (levelsBelow >= greyWindow) return 0;
        return RoundedShare(baseXp, greyWindow - levelsBelow, greyWindow);
    }

    private static int RoundedShare(int amount, int numerator, int denominator) =>
        (int)((2L * amount * numerator + denominator) / (2L * denominator));

    public readonly record struct GrantResult(int Level, int Xp, int LevelsGained);

    public static int HeldXp(int level, int xp) => level >= MaxLevel ? 0 : Math.Max(0, xp);

    public static GrantResult ApplyXp(int level, int xp, int gained)
    {
        if (level >= MaxLevel) return new GrantResult(level, 0, 0);

        xp += gained;
        var levelsGained = 0;
        while (level < MaxLevel && xp >= XpToNextLevel(level))
        {
            xp -= XpToNextLevel(level);
            level++;
            levelsGained++;
        }

        if (level >= MaxLevel) xp = 0;
        return new GrantResult(level, xp, levelsGained);
    }
}
