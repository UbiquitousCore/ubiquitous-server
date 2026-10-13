using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class CreatureHuntingTests
{
    private static CreatureDefinition Wolf(int faction = UnitFactionTypes.Hostile) =>
        new(1, "wolf", 3, 150, FactionType: faction, Family: 2);

    private static CreatureDefinition Rabbit(int faction = UnitFactionTypes.Neutral, int? family = CreatureFamilies.Critter) =>
        new(2, "rabbit", 1, 1, FactionType: faction, Family: family);

    [Fact]
    public void AHostileCreatureHuntsANeutralSmallAnimal() =>
        Assert.True(CreatureHunting.Hunts(Wolf(), Rabbit()));

    [Theory]
    [InlineData(UnitFactionTypes.Neutral)]
    [InlineData(UnitFactionTypes.Townsfolk)]
    public void OnlyAHostileCreatureHunts(int faction) =>
        Assert.False(CreatureHunting.Hunts(Wolf(faction), Rabbit()));

    [Theory]
    [InlineData(UnitFactionTypes.Hostile)]
    [InlineData(UnitFactionTypes.Townsfolk)]
    public void OnlyANeutralCreatureIsHunted(int faction) =>
        Assert.False(CreatureHunting.Hunts(Wolf(), Rabbit(faction)));

    [Theory]
    [InlineData(2)]
    [InlineData(null)]
    public void ANeutralThatIsNotASmallAnimalIsLeftAlone(int? family) =>
        Assert.False(CreatureHunting.Hunts(Wolf(), Rabbit(family: family)));

    [Fact]
    public void ACreatureWithNoFactionIsNeverHunted() =>
        Assert.False(CreatureHunting.Hunts(Wolf(), new CreatureDefinition(2, "rabbit", 1, 1, Family: CreatureFamilies.Critter)));

    [Fact]
    public void ATrainingDummyIsNeverHunted() =>
        Assert.False(CreatureHunting.Hunts(Wolf(), Rabbit() with { RawExtraFlags = 128 }));

    [Fact]
    public void ASmallAnimalHuntsNothing() =>
        Assert.False(CreatureHunting.Hunts(Wolf() with { Family = CreatureFamilies.Critter }, Rabbit()));

    [Fact]
    public void NothingIsHuntedWithoutATemplate()
    {
        Assert.False(CreatureHunting.Hunts(null, Rabbit()));
        Assert.False(CreatureHunting.Hunts(Wolf(), null));
    }

    [Fact]
    public void OnlyAHostileCreatureGoesAfterANeutralOneOnSight()
    {
        int[] factions = [UnitFactionTypes.Hostile, UnitFactionTypes.Townsfolk, UnitFactionTypes.Neutral];
        foreach (var self in factions)
            foreach (var other in factions)
                Assert.Equal(self == UnitFactionTypes.Hostile && other == UnitFactionTypes.Neutral,
                    FactionStance.AttacksCreatureOnSight(self, other));
    }
}
