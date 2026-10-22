using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class SpellStacksTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly SpellDefinition PoisonTeeth =
        new(2641, "Poison Teeth", 0, 0, 0f, AuraSlowPercent: 10, DurationMs: 20000);

    [Fact]
    public void EachBite_AddsAStack_UpToTheLimit()
    {
        var stacks = new SpellStacks(new Dictionary<uint, int> { [2641] = 5 });

        var counts = Enumerable.Range(0, 7).Select(i => stacks.Land(2641, PoisonTeeth, adds: true, Now + TimeSpan.FromSeconds(i))).ToArray();

        Assert.Equal([1, 2, 3, 4, 5, 5, 5], counts);
    }

    [Fact]
    public void AStackThatHasRunOut_StartsAgainFromOne()
    {
        var stacks = new SpellStacks(new Dictionary<uint, int> { [2641] = 5 });
        stacks.Land(2641, PoisonTeeth, adds: true, Now);
        stacks.Land(2641, PoisonTeeth, adds: true, Now);

        Assert.Equal(1, stacks.Land(2641, PoisonTeeth, adds: true, Now + TimeSpan.FromSeconds(21)));
    }

    [Fact]
    public void ASpellsOtherEffects_ReadTheStackWithoutAddingToIt()
    {
        var stacks = new SpellStacks(new Dictionary<uint, int> { [2641] = 5 });
        stacks.Land(2641, PoisonTeeth, adds: true, Now);
        stacks.Land(2641, PoisonTeeth, adds: true, Now);

        Assert.Equal(2, stacks.Land(2641, PoisonTeeth, adds: false, Now));
        Assert.Equal(3, stacks.Land(2641, PoisonTeeth, adds: true, Now));
    }

    [Fact]
    public void ASpellWithNoLimit_NeverStacks()
    {
        var stacks = new SpellStacks(new Dictionary<uint, int>());

        Assert.Equal(1, stacks.Land(2641, PoisonTeeth, adds: true, Now));
        Assert.Equal(1, stacks.Land(2641, PoisonTeeth, adds: true, Now));
    }

    [Fact]
    public void Scaling_MultipliesWhatTheEffectDoes_AndASlowNeverPassesAHundred()
    {
        var five = SpellStacks.Scaled(PoisonTeeth with { AuraMeleeAttackSpeedPercent = -10 }, 5);
        Assert.Equal(50, five.AuraSlowPercent);
        Assert.Equal(-50, five.AuraMeleeAttackSpeedPercent);

        Assert.Equal(100, SpellStacks.Scaled(PoisonTeeth with { AuraSlowPercent = 40 }, 5).AuraSlowPercent);
    }

    [Fact]
    public void ThePrisonHoundsPoison_StacksToFiveInBothModes()
    {
        Assert.Equal(5, GameContent.Development.SpellStackLimits[2641]);
        Assert.Equal(5, GameContent.Development.SpellStackLimits[2722]);
    }
}
