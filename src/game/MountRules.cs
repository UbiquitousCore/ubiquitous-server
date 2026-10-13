using Ubiquitous.Data;

namespace Ubiquitous.Game;

public static class MountRules
{
    // The same ratio ubiquitous-client runs a rider at.
    // TODO: Verify whether a mount's own speed-up (spell_effect type 1 subtype 52) changes it.
    public const float SpeedRatio = 1.6f;

    public static bool IsMount(SpellDefinition? spell) => spell?.AuraMountId is not null;
}
