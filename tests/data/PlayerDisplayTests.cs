namespace Ubiquitous.Data.Tests;

public class PlayerDisplayTests
{
    [Fact]
    public void Validate_AcceptsZeroedAndTypicalSyntheticDisplay()
    {
        var zeroed = new PlayerDisplayDefinition(0, 0, 0, 0, 0, 0, 0, 0);
        var typical = new PlayerDisplayDefinition(1, 2, 3, 4, 5, 6, 7, 8);

        Assert.Empty(PlayerDisplayValidator.Validate(zeroed));
        Assert.Empty(PlayerDisplayValidator.Validate(typical));
    }

    [Fact]
    public void Validate_RejectsNegativeFieldsByName()
    {
        var display = new PlayerDisplayDefinition(0, -1, 0, 0, 0, 0, 0, 0);

        var errors = PlayerDisplayValidator.Validate(display);

        Assert.Single(errors);
        Assert.Contains("race", errors[0]);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(7, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(8, false)]
    public void Validate_AcceptsOnlyTheKnownRaceIdSet(int race, bool valid)
    {
        var display = new PlayerDisplayDefinition(0, race, 0, 0, 0, 0, 0, 0);

        var errors = PlayerDisplayValidator.Validate(display);

        Assert.Equal(valid, errors.Count == 0);
    }
}
