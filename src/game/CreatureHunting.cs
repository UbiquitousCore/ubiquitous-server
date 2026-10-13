using Ubiquitous.Data;

namespace Ubiquitous.Game;

public static class CreatureHunting
{
    public static bool Hunts(CreatureDefinition? hunter, CreatureDefinition? prey)
    {
        if (hunter is null || prey?.FactionType is not int preyFaction) return false;
        if (hunter.Family == CreatureFamilies.Critter) return false;
        if (prey.Family != CreatureFamilies.Critter || CreatureExtraFlags.IsTrainingDummy(prey)) return false;
        return FactionStance.AttacksCreatureOnSight(FactionStance.Resolve(hunter.FactionType), preyFaction);
    }

    public static int NoticeRange(CreatureDefinition hunter, CreatureDefinition prey, int fallbackRange) =>
        EncounterRules.NoticeRange(
            hunter.NoticeDistance is int sight && sight > 0 ? sight : fallbackRange, hunter, prey.Level);
}
