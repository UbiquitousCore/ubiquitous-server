using Ubiquitous.Data;

namespace Ubiquitous.Game;

/// <summary>
/// The skills every character has: gathering, switching target, attacking and
/// shooting. Builds number them differently, so they are named here once and
/// take the numbering of whichever build the server loaded.
/// </summary>
public static class BasicSpells
{
    private sealed record Numbering(
        uint GatherNormal, uint GatherMine, uint GatherChest, uint SwitchTarget,
        uint AutoAttack, uint AutoShootPhysical, uint AutoShootMagic);

    // 1.1.4 and earlier.
    private static readonly Numbering Early = new(57, 23, 1822, 111, 108, 58, 89);

    // 2.2.0 on: the same skills, by the working name each row carries, as spells 1 to 7.
    private static readonly Numbering Late = new(1, 2, 3, 4, 5, 6, 7);

    private static Numbering current = Early;

    public static uint GatherNormal => current.GatherNormal;
    public static uint GatherMine => current.GatherMine;
    public static uint GatherChest => current.GatherChest;
    public static uint SwitchTarget => current.SwitchTarget;
    public static uint AutoAttack => current.AutoAttack;
    public static uint AutoShootPhysical => current.AutoShootPhysical;
    public static uint AutoShootMagic => current.AutoShootMagic;

    /// <summary>
    /// Takes the later numbering when the loaded spells lack the early auto
    /// attack and hold the later one; anything else keeps the early numbering.
    /// </summary>
    public static void UseNumberingOf(IReadOnlyDictionary<uint, SpellDefinition> spells) =>
        current = !spells.ContainsKey(Early.AutoAttack) && spells.ContainsKey(Late.AutoAttack) ? Late : Early;
}
