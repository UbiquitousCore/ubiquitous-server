namespace Ubiquitous.Game.Tests;

public sealed class AmbientServiceTests
{
    [Fact]
    public void StateFor_ReturnsTheInventedDefaultForAMapNothingHasSetOneOn()
    {
        var service = new AmbientService();

        Assert.Equal(AmbientService.Default, service.StateFor(13));
    }

    [Fact]
    public void SetState_ChangesWhatStateForReturnsForThatMapOnly()
    {
        var service = new AmbientService();

        service.SetState(13, new AmbientState("rain", "Harvest Festival"));

        Assert.Equal(new AmbientState("rain", "Harvest Festival"), service.StateFor(13));
        Assert.Equal(AmbientService.Default, service.StateFor(20));
    }

    [Fact]
    public void SetState_CanClearAFestivalBackToNull()
    {
        var service = new AmbientService();
        service.SetState(13, new AmbientState("rain", "Harvest Festival"));

        service.SetState(13, new AmbientState("rain"));

        Assert.Null(service.StateFor(13).Festival);
    }
}
