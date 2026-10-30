namespace Ubiquitous.Game.Tests;

public sealed class FallDamageTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(9f, 18f)]
    [InlineData(25f, 30f)]
    public void LandingSpeed_FollowsTheFallingPull(float height, float speed)
    {
        Assert.Equal(speed, FallDamage.LandingSpeed(height), 3);
    }

    [Theory]
    [InlineData(17.9f, 0)]
    [InlineData(18f, 0)]
    [InlineData(24f, 500)]
    [InlineData(30f, 1000)]
    [InlineData(40f, 1833)]
    public void DamageFor_GrowsFromTheHarmSpeedToAllOfMaximumHealthAtTheDeathSpeed(float speed, int damage)
    {
        Assert.Equal(damage, FallDamage.DamageFor(speed, maxHealth: 1000));
    }

    [Fact]
    public void Tracker_MeasuresAJumpFromItsHighestPoint()
    {
        var fall = new FallTracker();
        fall.Jumping(10f);
        fall.Jumping(11f);
        fall.Falling(5f);

        Assert.Equal(11f - -4f, fall.Landed(-4f));
    }

    [Fact]
    public void Tracker_DoesNotFollowADropThatDidNotStartWithAJump()
    {
        var fall = new FallTracker();
        fall.Falling(50f);

        Assert.Null(fall.Landed(0f));
    }

    [Fact]
    public void Tracker_ForgetsAFallThatEndsAnyOtherWay()
    {
        var fall = new FallTracker();
        fall.Jumping(50f);
        fall.Cancel();

        Assert.Null(fall.Landed(0f));
    }

    [Fact]
    public void Tracker_ALandingEndsTheFall()
    {
        var fall = new FallTracker();
        fall.Jumping(50f);
        fall.Landed(0f);

        Assert.Null(fall.Landed(-10f));
    }
}
