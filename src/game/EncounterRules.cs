using Ubiquitous.Data;

namespace Ubiquitous.Game;

public static class EncounterRules
{
    public const double NoticeRangePerLevel = 0.5;

    public const double MinNoticeRange = 4.0;

    public const double MaxNoticeRange = 18.0;

    public static double NoticeBonus(CreatureRank rank, bool fightsAtRange) =>
        RankBonus(rank) + (fightsAtRange ? RangedBonus : 0.0);

    public const double RangedBonus = 2.0;

    private static double RankBonus(CreatureRank rank) => rank switch
    {
        CreatureRank.Boss or CreatureRank.WorldBoss => 4.0,
        CreatureRank.Elite => 3.0,
        CreatureRank.Rare => 2.0,
        _ => 0.0,
    };

    public static int NoticeRange(
        int baseRange, int creatureLevel, int playerLevel, CreatureRank rank, bool fightsAtRange = false)
    {
        var levelGap = Math.Min(creatureLevel, LevelingService.MaxLevel) - playerLevel;
        var range = baseRange + NoticeRangePerLevel * levelGap + NoticeBonus(rank, fightsAtRange);
        return (int)Math.Round(Math.Clamp(range, MinNoticeRange, MaxNoticeRange), MidpointRounding.AwayFromZero);
    }

    public static int NoticeRange(int baseRange, CreatureDefinition? creature, int playerLevel) =>
        NoticeRange(
            baseRange,
            creature?.Level ?? playerLevel,
            playerLevel,
            creature?.Rank ?? CreatureRank.Ordinary,
            creature?.FightsAtRange ?? false);
}
