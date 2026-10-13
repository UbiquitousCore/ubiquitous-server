using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class TalentEffectsTests
{
    private static readonly IReadOnlyDictionary<uint, SpellDefinition> Spells = new Dictionary<uint, SpellDefinition>
    {
        [1] = new(1, "crit-passive", 0, 0, 0, AuraAttributeId: AttributeIds.CriticalStrike, AuraAttributeAmount: 2),
        [2] = new(2, "stamina-passive", 0, 0, 0, AuraAttributeId: AttributeIds.Stamina, AuraAttributeAmount: 5),
        [3] = new(3, "more-stamina", 0, 0, 0, AuraAttributeId: AttributeIds.Stamina, AuraAttributeAmount: 3),
        [4] = new(4, "percent-passive", 0, 0, 0, AuraAttributeId: AttributeIds.Strength, AuraAttributeAmount: 10, AuraAttributeIsPercent: true),
        [5] = new(5, "shout", 0, 60000, 0, PowerCost: 10, AuraAttributeId: AttributeIds.Strength, AuraAttributeAmount: 50),
        [6] = new(6, "slow-strike", 1500, 0, 5, EffectDamage: 20),
    };

    [Fact]
    public void Castable_KeepsSpellsWithACostACooldownOrACastTime()
    {
        Assert.Equal([5u, 6u], TalentEffects.Castable([1, 5, 6, 99], Spells));
    }

    [Fact]
    public void PassiveAttributes_AddsUpFlatBonusesOfPassiveSpellsOnly()
    {
        var totals = TalentEffects.PassiveAttributes([1, 2, 3, 4, 5, 99], Spells);

        Assert.Equal(2, totals[AttributeIds.CriticalStrike]);
        Assert.Equal(8, totals[AttributeIds.Stamina]);
        Assert.False(totals.ContainsKey(AttributeIds.Strength));
    }

    [Fact]
    public void PassiveAttributes_CountsEveryEffectAPassiveSpellCarries()
    {
        var spell = new SpellDefinition(20, "spell-20", 0, 0, 0f,
            AuraAttributeId: AttributeIds.Strength, AuraAttributeAmount: 3,
            ExtraEffects:
            [
                new SpellDefinition(20, "spell-20", 0, 0, 0f, AuraAttributeId: AttributeIds.Stamina, AuraAttributeAmount: 4),
                new SpellDefinition(20, "spell-20", 0, 0, 0f, AuraAttributeId: AttributeIds.Agility, AuraAttributeAmount: 10,
                    AuraAttributeIsPercent: true),
            ]);

        var totals = TalentEffects.PassiveAttributes([20], new Dictionary<uint, SpellDefinition> { [20] = spell });

        Assert.Equal(3, totals[AttributeIds.Strength]);
        Assert.Equal(4, totals[AttributeIds.Stamina]);
        Assert.False(totals.ContainsKey(AttributeIds.Agility));
    }
}
