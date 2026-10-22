namespace Ubiquitous.Scripts;

// The blocks, their order, the bosses and Tharrath's lines are the dungeon field guide's (the lines are also the
// operator's String_Script rows). Own choice: how many creatures make up each wave, and which kinds, within what
// the guide says each block mixes; the pauses before the first wave, a boss, the farewell and a retry.
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

    public static readonly TimeSpan WaveEvery = TimeSpan.FromSeconds(30);

    public static readonly IReadOnlyList<RitualBlock> Blocks =
    [
        new("Here they come, right on time!",
        [
            [SonOfTheEarth, SonOfTheLake, SonOfTheLake],
            [SonOfTheEarth, SonOfTheLake, RottenSonOfTheLake],
            [SonOfTheEarth, SonOfTheEarth, SonOfTheLake, RottenSonOfTheLake],
        ],
        "The air is humming... Must be a giant one!", Hathraad),
        new("More of them are coming!",
        [
            [GhostlyGladiator, GhostlyHunter, GhostlyPriest],
            [GhostlyGladiator, GhostlyMage, GhostlyPriest],
            [GhostlyGladiator, GhostlyHunter, GhostlyMage, GhostlyPriest],
        ],
        "An angry spirit approaches!", Kermode),
        new("You've got to hold them off!",
        [
            [SonOfTheLake, GhostlyGladiator, FlameStriker, LavaSkinBurner],
            [RottenSonOfTheLake, GhostlyHunter, RockWingsAssassin, LavaSkinBurner],
            [SonOfTheLake, GhostlyMage, FlameStriker, RockWingsAssassin],
            [GhostlyGladiator, GhostlyPriest, FlameStriker, LavaSkinBurner, RockWingsAssassin],
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
        Blocks, Lines, Summoning, WaveEvery,
        beforeFirstWave: TimeSpan.FromSeconds(3),
        beforeBoss: TimeSpan.FromSeconds(8),
        beforeFarewell: TimeSpan.FromSeconds(4),
        beforeRetry: TimeSpan.FromSeconds(15));
}
