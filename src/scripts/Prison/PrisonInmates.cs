namespace Ubiquitous.Scripts;

// Which inmate uses which move is the dungeon field guide's; the spell ids are the operator's rows of those
// names. Own choice: every first-use and repeat time below, picked to read as the guide's "every few seconds".
public static class PrisonInmates
{
    private const uint PoisonTeeth = 2641;
    private const uint CrazyDance = 2646;
    private const uint ThroatSeal = 2649;
    private const uint BloodLust = 2650;
    private const uint FleshSiphon = 2653;
    private const uint FleshSiphonHeal = 2655;
    private const uint BoneThorn = 2658;
    private const uint KnockThrow = 2659;
    private const uint Bleeding = 2662;
    private const uint EnticeFromHell = 2663;
    private const uint RottenFleshThrowing = 2664;
    private const uint SiphonLife = 2666;
    private const uint SiphonLifeHeal = 2667;
    private const uint RottenCloud = 2669;
    private const uint BurningBlood = 2682;

    private const uint OrcPrisonerEntry = 2676;
    private const uint InferiorBoneGolem = 2692;

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    public static readonly InmateScript HumanPrisoner = new("inmate.humanPrisoner",
    [
        new(KnockThrow, CastTarget.AnyEnemy, S(4), S(10)),
    ]);

    public static readonly InmateScript ViciousElfPrisoner = new("inmate.viciousElfPrisoner",
    [
        new(CrazyDance, CastTarget.Self, S(3), S(15)),
        new(ThroatSeal, CastTarget.Enemy, S(6), S(12), InMeleeOnly: true),
    ]);

    public static readonly InmateScript ElfPrisoner = new("inmate.elfPrisoner",
    [
        new(Bleeding, CastTarget.Enemy, S(3), S(8), InMeleeOnly: true),
    ]);

    public static readonly InmateScript ViciousOrcPrisoner = new("inmate.viciousOrcPrisoner",
    [
        new(BloodLust, CastTarget.Self, S(0), AtHealth: [0.8, 0.6, 0.4, 0.2]),
    ]);

    public static readonly InmateScript ViciousHumanPrisoner = new("inmate.viciousHumanPrisoner",
    [
        new(SiphonLife, CastTarget.Ally, S(0), S(10), BelowHealth: 0.5, ThenSelf: SiphonLifeHeal,
            NeedsAlly: OrcPrisonerEntry),
        new(BurningBlood, CastTarget.AnyEnemy, S(5), S(12)),
    ]);

    public static readonly InmateScript HumanPrisonerCapo = new("inmate.humanPrisonerCapo",
    [
        new(EnticeFromHell, CastTarget.Self, S(0)),
        new(BurningBlood, CastTarget.AnyEnemy, S(5), S(12)),
    ]);

    public static readonly InmateScript FleshGolem = new("inmate.fleshGolem",
    [
        new(FleshSiphon, CastTarget.Enemy, S(4), S(9), InMeleeOnly: true, ThenSelf: FleshSiphonHeal),
        new(RottenCloud, CastTarget.Self, S(6), S(8)),
    ]);

    public static readonly InmateScript RottenFleshGolem = new("inmate.rottenFleshGolem",
    [
        new(FleshSiphon, CastTarget.Enemy, S(4), S(9), InMeleeOnly: true, ThenSelf: FleshSiphonHeal),
        new(RottenCloud, CastTarget.Self, S(6), S(8)),
        new(RottenFleshThrowing, CastTarget.AnyEnemy, S(8), S(15)),
    ]);

    public static readonly InmateScript RottenJailHound = new("inmate.rottenJailHound",
    [
        new(PoisonTeeth, CastTarget.Enemy, S(3), S(6), InMeleeOnly: true),
    ]);

    public static readonly InmateScript BoneGolem = new("inmate.boneGolem",
    [
        new(BoneThorn, CastTarget.Self, S(0)),
    ],
    diesInto: [InferiorBoneGolem, InferiorBoneGolem]);

    public static IReadOnlyList<InmateScript> All { get; } =
    [
        HumanPrisoner, ViciousElfPrisoner, ElfPrisoner, ViciousOrcPrisoner, ViciousHumanPrisoner,
        HumanPrisonerCapo, FleshGolem, RottenFleshGolem, RottenJailHound, BoneGolem,
    ];
}
