using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class MiningPoolTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<MiningOreWeight> TwoOres =
        [new(MiningOres.Bronze, 70), new(MiningOres.Marcasite, 30)];

    private static MiningPool PoolOf(int locations, params double[] rolls)
    {
        var next = 0;
        double Roll() => rolls[Math.Min(next++, rolls.Length - 1)];
        return new MiningPool(Enumerable.Range(1, locations).Select(id => (uint)id), TwoOres, Roll);
    }

    private static uint[] ActiveIds(MiningPool pool, int locations) =>
        Enumerable.Range(1, locations).Select(id => (uint)id).Where(id => pool.TryGetActive(id, out _)).ToArray();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(100, 25)]
    public void AQuarterOfTheLocationsAreActive_AtLeastOneWhenThereAreAny(int locations, int expected) =>
        Assert.Equal(expected, MiningRules.ActiveCount(locations));

    [Fact]
    public void ThePoolStartsWithItsShareOfLocationsActive()
    {
        var pool = PoolOf(8, 0.0);

        Assert.Equal(2, pool.ActiveCount);
    }

    [Fact]
    public void TheOreIsRolledApartFromTheLocation()
    {
        var pool = PoolOf(1, 0.0, 0.9);

        Assert.True(pool.TryGetActive(1, out var ore));
        Assert.Equal(MiningOres.Marcasite, ore);
    }

    [Fact]
    public void MiningAnActiveLocationReturnsItsOreAndDeactivatesIt()
    {
        var pool = PoolOf(8, 0.0);
        var location = ActiveIds(pool, 8)[0];
        pool.TryGetActive(location, out var shown);

        var mined = pool.Mine(location, Start);

        Assert.Equal(shown, mined);
        Assert.False(pool.TryGetActive(location, out _));
    }

    [Fact]
    public void MiningAnInactiveLocationGivesNothing()
    {
        var pool = PoolOf(8, 0.0);
        var idle = Enumerable.Range(1, 8).Select(id => (uint)id).First(id => !pool.TryGetActive(id, out _));

        Assert.Null(pool.Mine(idle, Start));
        Assert.Equal(2, pool.ActiveCount);
    }

    [Fact]
    public void AnotherLocationBecomesActiveAtOnce_SoTheActiveCountHolds()
    {
        var pool = PoolOf(8, 0.0);
        var location = ActiveIds(pool, 8)[0];

        pool.Mine(location, Start);

        Assert.Equal(2, pool.ActiveCount);
    }

    [Fact]
    public void TheMinedLocationDoesNotComeBackWhileOthersAreAvailable()
    {
        var pool = PoolOf(8, 0.0);
        var location = ActiveIds(pool, 8)[0];

        pool.Mine(location, Start);

        Assert.False(pool.TryGetActive(location, out _));
    }

    [Fact]
    public void WhenEveryOtherLocationIsStillCoolingTheLongestIdleOneIsReactivated()
    {
        var pool = PoolOf(2, 0.0);
        Assert.Equal([1u], ActiveIds(pool, 2));

        pool.Mine(1, Start);
        Assert.Equal([2u], ActiveIds(pool, 2));

        pool.Mine(2, Start.AddSeconds(30));
        Assert.Equal([1u], ActiveIds(pool, 2));
    }

    [Fact]
    public void ALocationBecomesEligibleOnceItsThreeMinuteCooldownEnds()
    {
        var pool = PoolOf(3, 0.0);
        pool.Mine(1, Start);
        Assert.Equal([2u], ActiveIds(pool, 3));

        pool.Mine(2, Start.AddMinutes(3));

        Assert.Equal([1u], ActiveIds(pool, 3));
    }

    [Fact]
    public void ALocationStillCoolingIsPassedOverForOneThatNeverCooled()
    {
        var pool = PoolOf(3, 0.0);
        pool.Mine(1, Start);

        pool.Mine(2, Start.AddMinutes(1));

        Assert.Equal([3u], ActiveIds(pool, 3));
    }

    [Fact]
    public void EveryChangeBumpsTheVersionSoViewersKnowToRefresh()
    {
        var pool = PoolOf(8, 0.0);
        var before = pool.Version;

        pool.Mine(ActiveIds(pool, 8)[0], Start);

        Assert.True(pool.Version > before);
    }

    [Fact]
    public void ALocationWithNoOresInTheZoneStaysInactive()
    {
        var pool = new MiningPool([1u, 2u], [], () => 0.0);

        Assert.Equal(0, pool.ActiveCount);
    }

    [Fact]
    public void TheMendelRaceGathersInHalfTheTime()
    {
        var spell = new SpellDefinition(MiningRules.GatherSpellId, "gather", CastTimeMs: 3500, CooldownMs: 0, MaxRange: 0f);

        Assert.Equal(TimeSpan.FromMilliseconds(3500), MiningRules.CastTime(spell, PlayerRaceIds.Orc));
        Assert.Equal(TimeSpan.FromMilliseconds(1750), MiningRules.CastTime(spell, PlayerRaceIds.Mendel));
    }

    [Fact]
    public void WithoutTheSpellRowTheCastTakesTheDefault() =>
        Assert.Equal(TimeSpan.FromMilliseconds(MiningRules.DefaultCastTimeMs), MiningRules.CastTime(null, PlayerRaceIds.Human));

    [Fact]
    public void EveryOreMapsFromItsNodeEntry()
    {
        foreach (var ore in MiningOres.All) Assert.Equal(ore, MiningOres.ByNodeEntry(ore.NodeEntry));
        Assert.Null(MiningOres.ByNodeEntry(MiningRules.LocationEntry));
    }

    [Fact]
    public void ASharedMapShowsActiveLocationsAsOreAndHidesTheRest()
    {
        var state = new SharedMapState(13, rollUnit: () => 0.0);
        var spawns = Enumerable.Range(1, 4)
            .Select(id => new StaticObjectSpawnDefinition((uint)id, 13, MiningRules.LocationEntry, id, 2, 4, 0f))
            .ToArray();

        var shown = state.VisibleStaticObjects(new Position(0, 2, 0), 58f, spawns);

        var only = Assert.Single(shown);
        Assert.Equal(MiningOres.Bronze.NodeEntry, only.Entry);
        Assert.Equal(1u, only.SpawnId);
    }

    [Fact]
    public void MiningOnASharedMapHidesTheNodeAndShowsAnother()
    {
        var state = new SharedMapState(13, rollUnit: () => 0.0);
        var spawns = Enumerable.Range(1, 4)
            .Select(id => new StaticObjectSpawnDefinition((uint)id, 13, MiningRules.LocationEntry, id, 2, 4, 0f))
            .ToArray();
        var player = new Position(0, 2, 0);
        var node = state.VisibleStaticObjects(player, 58f, spawns).Single();
        var before = state.MiningVersion;

        var mined = state.Mine(node.Id.ToString(), Start);

        Assert.Equal(MiningOres.Bronze, mined);
        Assert.True(state.MiningVersion > before);
        var after = Assert.Single(state.VisibleStaticObjects(player, 58f, spawns));
        Assert.NotEqual(node.SpawnId, after.SpawnId);
        Assert.Null(state.Mine(node.Id.ToString(), Start));
    }

    [Fact]
    public void OrdinaryStaticObjectsAreUntouchedByMining()
    {
        var state = new SharedMapState(13, rollUnit: () => 0.0);
        var spawns = new[] { new StaticObjectSpawnDefinition(9, 13, 7001, 2, 2, 4, 0f) };

        var shown = Assert.Single(state.VisibleStaticObjects(new Position(0, 2, 0), 58f, spawns));

        Assert.Equal(7001u, shown.Entry);
        Assert.Null(state.ActiveOreAt(shown.Id.ToString()));
    }
}
