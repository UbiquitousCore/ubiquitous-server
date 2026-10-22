namespace Ubiquitous.Scripts;

public sealed class TharrathScript : CreatureScript
{
    public static readonly TharrathScript Instance = new();

    private TharrathScript() { }

    public override string Name => "npc.tharrath";

    public override bool NoticesEnemies => false;

    public override bool FightsBack => false;

    public override bool Chases => false;

    public override bool DrawsOthersIn => false;

    public override bool GivesUp(GiveUpContext context) => false;
}

// Which creature uses which move is the dungeon field guide's; the spell ids are the operator's rows of those
// names in the swamp's block. Own choice: every first-use and repeat time below, read from the guide's
// "every so often", "a few seconds in" and "every few seconds".
public static class RelicsKeyCreatures
{
    private const uint AnkleBite = 2401;
    private const uint TailLash = 2402;
    private const uint WolfHowl = 2403;
    private const uint EarthHealing = 2404;
    private const uint WaveCharge = 2405;
    private const uint SlowPoison = 2406;
    private const uint BreakArmor = 2408;
    private const uint EnergyField = 2409;
    private const uint FrostBolt = 2410;
    private const uint ShadowPunish = 2412;
    private const uint Healing = 2413;
    private const uint PoundingGlow = 2414;
    private const uint FireBarrier = 2415;
    private const uint ClawStrike = 2416;
    private const uint MirrorWaterBall = 2374;

    private const uint GhostWolf = 2650;

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    public static readonly InmateScript SwampGiantTortoise = new("swamp.giantTortoise",
    [
        new(AnkleBite, CastTarget.Enemy, S(4), S(10), InMeleeOnly: true),
    ]);

    public static readonly InmateScript GiantTailCrocodile = new("swamp.giantTailCrocodile",
    [
        new(TailLash, CastTarget.Enemy, S(5), S(12), InMeleeOnly: true),
    ]);

    public static readonly InmateScript SwampGuardian = new("swamp.guardian",
    [
        new(WolfHowl, CastTarget.Self, S(2), S(10)),
    ]);

    public static readonly InmateScript SonOfTheEarth = new("swamp.sonOfTheEarth",
    [
        new(EarthHealing, CastTarget.Self, S(0), BelowHealth: 0.5),
    ]);

    public static readonly InmateScript SonOfTheLake = new("swamp.sonOfTheLake",
    [
        new(WaveCharge, CastTarget.AnyEnemy, S(3), S(10)),
    ]);

    public static readonly InmateScript RottenSonOfTheLake = new("swamp.rottenSonOfTheLake",
    [
        new(SlowPoison, CastTarget.Enemy, S(3), S(10)),
    ]);

    public static readonly InmateScript GhostlyHunter = new("swamp.ghostlyHunter",
    [
        new(0, CastTarget.Self, S(3), SummonEntry: GhostWolf),
    ]);

    public static readonly InmateScript GhostlyGladiator = new("swamp.ghostlyGladiator",
    [
        new(BreakArmor, CastTarget.Enemy, S(3), S(6), InMeleeOnly: true),
    ]);

    public static readonly InmateScript GhostlyMage = new("swamp.ghostlyMage",
    [
        new(EnergyField, CastTarget.Self, S(0), S(60)),
        new(FrostBolt, CastTarget.Enemy, S(1), S(4)),
    ]);

    public static readonly InmateScript GhostlyPriest = new("swamp.ghostlyPriest",
    [
        new(Healing, CastTarget.WoundedAlly, S(2), S(12), NeedsWoundedAlly: true),
        new(ShadowPunish, CastTarget.Enemy, S(4), S(8)),
    ]);

    public static readonly InmateScript LavaSkinBurner = new("swamp.lavaSkinBurner",
    [
        new(FireBarrier, CastTarget.Self, S(2), S(3)),
    ]);

    public static readonly InmateScript FlameStriker = new("swamp.flameStriker",
    [
        new(PoundingGlow, CastTarget.Self, S(3), S(6)),
    ]);

    public static readonly InmateScript RockWingsAssassin = new("swamp.rockWingsAssassin",
    [
        new(ClawStrike, CastTarget.Enemy, S(3), S(6), InMeleeOnly: true),
    ]);

    public static readonly InmateScript HathraadMirror = new("swamp.hathraadMirror",
    [
        new(MirrorWaterBall, CastTarget.Enemy, S(1), S(4)),
    ]);

    public static IReadOnlyList<InmateScript> All { get; } =
    [
        SwampGiantTortoise, GiantTailCrocodile, SwampGuardian, SonOfTheEarth, SonOfTheLake, RottenSonOfTheLake,
        GhostlyHunter, GhostlyGladiator, GhostlyMage, GhostlyPriest, LavaSkinBurner, FlameStriker,
        RockWingsAssassin, HathraadMirror,
    ];
}
