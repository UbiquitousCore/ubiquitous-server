using Ubiquitous.Common;

namespace Ubiquitous.Game.Tests;

public sealed class TileActivationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Position Player = new(1, 2, 3);
    private static readonly Position NearHome = new(1, 2, 33);
    private static readonly Position FarHome = new(50, 2, 3);

    [Fact]
    public void IsSimulating_AwakeTileSimulates()
    {
        var state = new SharedMapState(13);

        Assert.True(state.IsSimulating("Creature:1", NearHome, Player, Now));
    }

    [Fact]
    public void IsSimulating_QuietTileSleeps()
    {
        var state = new SharedMapState(13);

        Assert.False(state.IsSimulating("Creature:1", FarHome, Player, Now));
    }

    [Fact]
    public void IsSimulating_GraceCoversAQuietTileBriefly()
    {
        var state = new SharedMapState(13);
        Assert.True(state.IsSimulating("Creature:1", NearHome, Player, Now));

        Assert.True(state.IsSimulating("Creature:1", FarHome, Player, Now + TimeSpan.FromMinutes(2)));
        Assert.False(state.IsSimulating("Creature:1", FarHome, Player, Now + TimeSpan.FromMinutes(4)));
    }

    [Fact]
    public void IsSimulating_FightingBodySimulatesAnywhere()
    {
        var state = new SharedMapState(13);
        state.MarkEngaged("Creature:1");

        Assert.True(state.IsSimulating("Creature:1", FarHome, Player, Now));
    }
}
