namespace Ubiquitous.Game;

public static class CorpseLifetime
{
    public static readonly TimeSpan WithLoot = TimeSpan.FromSeconds(150);
    public static readonly TimeSpan Empty = TimeSpan.FromSeconds(30);
    public static readonly DateTimeOffset UntilInstanceEnds = DateTimeOffset.MaxValue;

    public static DateTimeOffset Until(DateTimeOffset now, bool carriesLoot, bool inDungeon) =>
        inDungeon ? UntilInstanceEnds : now + (carriesLoot ? WithLoot : Empty);

    public static DateTimeOffset Sooner(DateTimeOffset? held, DateTimeOffset wanted) =>
        held is { } current && current < wanted ? current : wanted;
}
