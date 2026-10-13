using Ubiquitous.Data;
namespace Ubiquitous.Game.Tests;

public sealed class AttributeModifierServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetFlatAmount_CountsWornGearBesideAnyAuraOfTheSameKind()
    {
        var attributes = new AttributeModifierService();
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

        attributes.SetWorn("player:char-a", new Dictionary<int, int> { [AttributeIds.Armor] = 658 });
        Assert.Equal(658, attributes.GetFlatAmount("player:char-a", AttributeIds.Armor, now));

        attributes.Apply("player:char-a", spellId: 1, AttributeIds.Armor, 100, isPercent: false, durationMs: 60000, now);
        Assert.Equal(758, attributes.GetFlatAmount("player:char-a", AttributeIds.Armor, now));

        Assert.Equal(0, attributes.GetFlatAmount("player:char-b", AttributeIds.Armor, now));
    }

    [Fact]
    public void GetFlatAmount_CountsPassiveTalentsBesideGearAndAuras()
    {
        var attributes = new AttributeModifierService();
        attributes.SetWorn("player:char-a", new Dictionary<int, int> { [AttributeIds.Stamina] = 4 });
        attributes.SetPassive("player:char-a", new Dictionary<int, int> { [AttributeIds.Stamina] = 6 });
        attributes.Apply("player:char-a", spellId: 1, AttributeIds.Stamina, 10, isPercent: false, durationMs: 60000, Now);

        Assert.Equal(20, attributes.GetFlatAmount("player:char-a", AttributeIds.Stamina, Now));

        attributes.SetPassive("player:char-a", new Dictionary<int, int>());
        Assert.Equal(14, attributes.GetFlatAmount("player:char-a", AttributeIds.Stamina, Now));
    }

    [Fact]
    public void TickExpired_ReportsNothingUntilTheDurationElapses()
    {
        var mods = new AttributeModifierService();
        mods.Apply("player:char-a", spellId: 61, attributeId: 25, amount: 10, isPercent: false, durationMs: 2000, Now);

        Assert.Empty(mods.TickExpired(Now + TimeSpan.FromMilliseconds(1999)));
        Assert.Single(mods.TickExpired(Now + TimeSpan.FromMilliseconds(2000)));
    }

    [Fact]
    public void Apply_FallsBackToTheDefaultDurationWhenNoneIsGiven()
    {
        var mods = new AttributeModifierService();
        mods.Apply("player:char-a", spellId: 61, attributeId: 25, amount: 10, isPercent: false, durationMs: 0, Now);

        Assert.Empty(mods.TickExpired(Now + AttributeModifierService.DefaultDuration - TimeSpan.FromMilliseconds(1)));
        Assert.Single(mods.TickExpired(Now + AttributeModifierService.DefaultDuration));
    }

    [Fact]
    public void Apply_ReplacesRatherThanStackingASecondModifierOnTheSameTarget()
    {
        var mods = new AttributeModifierService();
        mods.Apply("player:char-a", spellId: 61, attributeId: 25, amount: 10, isPercent: false, durationMs: 5000, Now);
        mods.Apply("player:char-a", spellId: 62, attributeId: 26, amount: 5, isPercent: false, durationMs: 1000, Now + TimeSpan.FromSeconds(1));

        var expired = mods.TickExpired(Now + TimeSpan.FromSeconds(2));

        var only = Assert.Single(expired);
        Assert.Equal(62u, only.SpellId);
    }

    [Fact]
    public void Clear_DropsTheModifierWithNoFurtherEffect()
    {
        var mods = new AttributeModifierService();
        mods.Apply("player:char-a", spellId: 61, attributeId: 25, amount: 10, isPercent: false, durationMs: 5000, Now);

        mods.Clear("player:char-a");

        Assert.Empty(mods.TickExpired(Now + TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void TickExpired_CarriesTheAttributeAndAmountThrough()
    {
        var mods = new AttributeModifierService();
        mods.Apply("Creature:1", spellId: 61, attributeId: 0, amount: -21, isPercent: false, durationMs: 1000, Now);

        var expired = Assert.Single(mods.TickExpired(Now + TimeSpan.FromMilliseconds(1000)));

        Assert.Equal(0, expired.AttributeId);
        Assert.Equal(-21, expired.Amount);
        Assert.False(expired.IsPercent);
    }

    [Fact]
    public void GetFlatAmount_ReturnsTheActiveAmountForTheMatchingAttribute()
    {
        var mods = new AttributeModifierService();
        mods.Apply("player:char-a", spellId: 88, attributeId: 0, amount: -21, isPercent: false, durationMs: 5000, Now);

        Assert.Equal(-21, mods.GetFlatAmount("player:char-a", attributeId: 0, Now));
    }

    [Fact]
    public void GetFlatAmount_IsZeroForADifferentAttributeOnTheSameTarget()
    {
        var mods = new AttributeModifierService();
        mods.Apply("player:char-a", spellId: 61, attributeId: 25, amount: 10, isPercent: false, durationMs: 5000, Now);

        Assert.Equal(0, mods.GetFlatAmount("player:char-a", attributeId: 0, Now));
    }

    [Fact]
    public void GetFlatAmount_IsZeroForAPercentModifierEvenOnTheRightAttribute()
    {
        var mods = new AttributeModifierService();
        mods.Apply("player:char-a", spellId: 88, attributeId: 0, amount: -40, isPercent: true, durationMs: 5000, Now);

        Assert.Equal(0, mods.GetFlatAmount("player:char-a", attributeId: 0, Now));
    }

    [Fact]
    public void GetFlatAmount_IsZeroOnceTheModifierHasExpired()
    {
        var mods = new AttributeModifierService();
        mods.Apply("player:char-a", spellId: 88, attributeId: 0, amount: -21, isPercent: false, durationMs: 1000, Now);

        Assert.Equal(0, mods.GetFlatAmount("player:char-a", attributeId: 0, Now + TimeSpan.FromMilliseconds(1000)));
    }
}
