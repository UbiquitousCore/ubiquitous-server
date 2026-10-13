namespace Ubiquitous.Server;

public sealed record PlaySessionOptions(
    bool DevelopmentEntry = false,
    Func<double>? SpellRoll = null,
    Func<double>? CombatRoll = null,
    IReadOnlySet<string>? DevelopmentUserIds = null);
