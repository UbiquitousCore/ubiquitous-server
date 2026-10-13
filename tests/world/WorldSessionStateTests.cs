using Ubiquitous.Common;
using Ubiquitous.Data;

namespace Ubiquitous.World.Tests;

public class WorldSessionStateTests
{
    [Fact]
    public void ApplyPlayerMovement_AcceptsAMoveWellWithinTheUnslowedCap()
    {
        var world = new WorldSessionState(13, new Position(0, 0, 0), 0);

        var decision = world.ApplyPlayerMovement(new Position(10, 0, 0), 0);

        Assert.True(decision.Accepted);
        Assert.False(decision.Corrected);
    }

    [Fact]
    public void ApplyPlayerMovement_RejectsTheSameMoveOnceASlowTightensTheCap()
    {
        var world = new WorldSessionState(13, new Position(0, 0, 0), 0);

        var decision = world.ApplyPlayerMovement(new Position(10, 0, 0), 0, speedMultiplier: 0.5f);

        Assert.False(decision.Accepted);
        Assert.True(decision.Corrected);
        Assert.Equal(0, decision.Position.X);
    }

    [Fact]
    public void ApplyPlayerMovement_FullyRootedAtZeroSpeedRejectsAnyMoveAtAll()
    {
        var world = new WorldSessionState(13, new Position(0, 0, 0), 0);

        var decision = world.ApplyPlayerMovement(new Position(0.01f, 0, 0), 0, speedMultiplier: 0f);

        Assert.False(decision.Accepted);
    }

    [Fact]
    public void ApplyPlayerMovement_AcceptsAnyFinitePositionWhenNoBoundsAreKnown()
    {
        var world = new WorldSessionState(13, new Position(0, 0, 0), 0);

        var decision = world.ApplyPlayerMovement(new Position(100_000, 0, 0), 0, speedMultiplier: 1_000_000f);

        Assert.True(decision.Accepted);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ApplyPlayerMovement_RejectsANonFiniteCoordinateEvenWithNoBoundsKnown(float badValue)
    {
        var world = new WorldSessionState(13, new Position(0, 0, 0), 0);

        var decision = world.ApplyPlayerMovement(new Position(badValue, 0, 0), 0, speedMultiplier: 1_000_000f);

        Assert.False(decision.Accepted);
        Assert.True(decision.Corrected);
    }

    [Fact]
    public void ApplyPlayerMovement_AcceptsAMoveInsideTheMapsOwnBounds()
    {
        var map = new MapDefinition(13, "test-city", "world/city.wld", MinX: -704, MaxX: 320, MinZ: -448, MaxZ: 576);
        var world = new WorldSessionState(13, new Position(0, 0, 0), 0, map);

        var decision = world.ApplyPlayerMovement(new Position(100, 0, 100), 0, speedMultiplier: 1_000_000f);

        Assert.True(decision.Accepted);
    }

    [Fact]
    public void ApplyPlayerMovement_RejectsAMoveOutsideTheMapsOwnBounds()
    {
        var map = new MapDefinition(13, "test-city", "world/city.wld", MinX: -704, MaxX: 320, MinZ: -448, MaxZ: 576);
        var world = new WorldSessionState(13, new Position(0, 0, 0), 0, map);

        var decision = world.ApplyPlayerMovement(new Position(321, 0, 0), 0, speedMultiplier: 1_000_000f);

        Assert.False(decision.Accepted);
        Assert.True(decision.Corrected);
        Assert.Equal(0, decision.Position.X);
    }

    [Fact]
    public void ApplyPlayerMovement_ManySmallInCapStepsNeverCrossTheMapsOwnBounds()
    {
        var map = new MapDefinition(13, "test-city", "world/city.wld", MinX: -704, MaxX: 320, MinZ: -448, MaxZ: 576);
        var world = new WorldSessionState(13, new Position(300, 0, 0), 0, map);

        MovementDecision? lastRejected = null;
        var x = 300f;
        for (var i = 0; i < 10; i++)
        {
            x += 10;
            var decision = world.ApplyPlayerMovement(new Position(x, 0, 0), 0);
            if (!decision.Accepted) lastRejected = decision;
        }

        Assert.NotNull(lastRejected);
        Assert.True(world.PlayerPosition.X <= 320);
        Assert.True(lastRejected!.Position.X <= 320);
    }

    [Fact]
    public void ApplyPlayerMovement_AMapWithNoBoundsAtAllNeverRejectsOnPosition()
    {
        var map = new MapDefinition(13, "test-city", "world/city.wld");
        var world = new WorldSessionState(13, new Position(0, 0, 0), 0, map);

        var decision = world.ApplyPlayerMovement(new Position(100_000, 0, 0), 0, speedMultiplier: 1_000_000f);

        Assert.True(decision.Accepted);
    }

    [Fact]
    public void ApplyPlayerMovement_BypassValidation_AcceptsAMoveThatWouldOtherwiseFailTheDistanceCap()
    {
        var world = new WorldSessionState(13, new Position(0, 0, 0), 0);

        var decision = world.ApplyPlayerMovement(new Position(10_000, 0, 0), 0, bypassValidation: true);

        Assert.True(decision.Accepted);
        Assert.False(decision.Corrected);
        Assert.Equal(10_000, decision.Position.X);
    }

    [Fact]
    public void ApplyPlayerMovement_BypassValidation_AcceptsAMoveOutsideTheMapsOwnBounds()
    {
        var map = new MapDefinition(13, "test-city", "world/city.wld", MinX: -704, MaxX: 320, MinZ: -448, MaxZ: 576);
        var world = new WorldSessionState(13, new Position(0, 0, 0), 0, map);

        var decision = world.ApplyPlayerMovement(new Position(321, 0, 0), 0, bypassValidation: true);

        Assert.True(decision.Accepted);
    }

    [Fact]
    public void ApplyPlayerMovement_BypassValidationFalse_StillEnforcesTheCap()
    {
        var world = new WorldSessionState(13, new Position(0, 0, 0), 0);

        var decision = world.ApplyPlayerMovement(new Position(10_000, 0, 0), 0);

        Assert.False(decision.Accepted);
    }
}
