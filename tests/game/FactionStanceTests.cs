using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class FactionStanceTests
{
    private const int Hostile = UnitFactionTypes.Hostile;
    private const int Townsfolk = UnitFactionTypes.Townsfolk;
    private const int Neutral = UnitFactionTypes.Neutral;

    [Theory]
    [InlineData(Townsfolk, Townsfolk)]
    [InlineData(Hostile, Hostile)]
    [InlineData(Neutral, Neutral)]
    public void ATownNpcOfYourOwnFactionIsFriendly(int self, int target) =>
        Assert.Equal(Stance.Friendly, FactionStance.Between(self, target));

    [Fact]
    public void OnePlayerIsFriendlyToAnotherByFactionAlone() =>
        Assert.Equal(Stance.Friendly, FactionStance.Between(UnitFactionTypes.Townsfolk, UnitFactionTypes.Townsfolk));

    [Theory]
    [InlineData(Townsfolk, Neutral)]
    [InlineData(Neutral, Townsfolk)]
    [InlineData(Hostile, Neutral)]
    [InlineData(Neutral, Hostile)]
    public void ANeutralNobodyHasHitIsWary(int self, int target) =>
        Assert.Equal(Stance.Wary, FactionStance.Between(self, target));

    [Theory]
    [InlineData(Townsfolk, Hostile)]
    [InlineData(Hostile, Townsfolk)]
    public void AHostileCreatureAttacks(int self, int target) =>
        Assert.Equal(Stance.Hostile, FactionStance.Between(self, target));

    [Fact]
    public void Resolve_TreatsAnUnknownFactionAsHostile()
    {
        Assert.Equal(UnitFactionTypes.Hostile, FactionStance.Resolve(null));
        Assert.Equal(UnitFactionTypes.Neutral, FactionStance.Resolve(UnitFactionTypes.Neutral));
    }
}
