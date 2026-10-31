using Ubiquitous.Data;

namespace Ubiquitous.Game;

public static class EncounterRules
{
    public const double NoticeRangePerLevel = 0.05;

    public const double MinNoticeRange = 3.0;

    public const double MaxNoticeRange = 15.0;

    public const double RangedFactor = 1.1;

    // Own choice: two multipliers cover the four tougher ranks.
    public static double RankFactor(CreatureRank rank) => rank switch
    {
        CreatureRank.Boss or CreatureRank.WorldBoss => 1.3,
        CreatureRank.Elite or CreatureRank.Rare => 1.2,
        _ => 1.0,
    };

    public static int NoticeRange(
        int baseRange, int creatureLevel, int playerLevel, CreatureRank rank, bool fightsAtRange = false)
    {
        var levelGap = Math.Min(creatureLevel, LevelingService.MaxLevel) - playerLevel;
        var range = baseRange
            * Math.Max(0.0, 1.0 + NoticeRangePerLevel * levelGap)
            * (fightsAtRange ? RangedFactor : 1.0)
            * RankFactor(rank);
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
