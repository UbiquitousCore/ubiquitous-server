namespace Ubiquitous.Scripts;

// The blocks, their waves and when each comes, the bosses, Tharrath's lines and when he says them are the game as it
// behaves in play, recorded in docs/guides/dungeon-field-guide.md (the lines are also the operator's String_Script
// rows). Own choice: the pause before a retry.
public static class RelicsKeyRitual
{
    public const uint Hathraad = 2619;
    public const uint Kermode = 2620;
    public const uint AlrRahjil = 2621;

    private const uint SonOfTheEarth = 2627;
    private const uint SonOfTheLake = 2628;
    private const uint RottenSonOfTheLake = 2629;
    private const uint GhostlyHunter = 2630;
    private const uint GhostlyGladiator = 2631;
    private const uint GhostlyMage = 2632;
    private const uint GhostlyPriest = 2633;
    private const uint LavaSkinBurner = 2634;
    private const uint RockWingsAssassin = 2635;
    private const uint FlameStriker = 2648;

    public const uint Summoning = 2387;

    private static RitualWave After(int seconds, params uint[] creatures) =>
        new(TimeSpan.FromSeconds(seconds), creatures);

    public static readonly IReadOnlyList<RitualBlock> Blocks =
    [
        new("Here they come, right on time!", TimeSpan.FromSeconds(5),
        [
            After(0, SonOfTheEarth, SonOfTheLake),
            After(35, SonOfTheEarth, SonOfTheLake, RottenSonOfTheLake),
            After(30, SonOfTheEarth, SonOfTheEarth, RottenSonOfTheLake),
        ],
        "The air is humming... Must be a giant one!", Hathraad),
        new("More of them are coming!", TimeSpan.FromSeconds(10),
        [
            After(25, GhostlyHunter, GhostlyGladiator, GhostlyGladiator),
            After(30, GhostlyHunter, GhostlyGladiator, GhostlyMage),
            After(30, GhostlyHunter, GhostlyGladiator, GhostlyPriest),
        ],
        "An angry spirit approaches!", Kermode),
        new("You've got to hold them off!", TimeSpan.FromSeconds(10),
        [
            After(20, SonOfTheLake, GhostlyGladiator, RockWingsAssassin),
            After(30, SonOfTheLake, GhostlyHunter, FlameStriker),
            After(40, RottenSonOfTheLake, GhostlyGladiator, LavaSkinBurner),
            After(45, GhostlyMage, LavaSkinBurner, RockWingsAssassin, FlameStriker),
            After(50, GhostlyMage, GhostlyPriest, RockWingsAssassin, FlameStriker),
        ],
        "I'm almost finished!", AlrRahjil),
    ];

    public static readonly RitualLines Lines = new(
        Follow: "Follow me!",
        Starting: "I'm starting!",
        Done: "It's done!",
        Farewell: "Woo! That was rather exhausting. I need to wait here, recover my strength... Find me later in the Wyrgast's Library.",
        Failed: "My spell... failed.");

    public static Ritual Create() => new(
        Blocks, Lines, Summoning,
        beforeBoss: TimeSpan.FromSeconds(20),
        warningAfterBoss: TimeSpan.FromSeconds(5),
        beforeFarewell: TimeSpan.FromSeconds(10),
        beforeRetry: TimeSpan.FromSeconds(15));
}
