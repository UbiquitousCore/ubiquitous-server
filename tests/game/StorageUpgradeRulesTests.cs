using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class StorageUpgradeRulesTests
{
    private static readonly StorageUpgradeDefinition[] Rungs =
    [
        new(1, "bag", SlotCount: 18, FromSlotCount: 12, PriceCurrency: 0, PriceAmount: 100),
        new(2, "bag", SlotCount: 24, FromSlotCount: 18, PriceCurrency: 0, PriceAmount: 250),
        new(3, "bank", SlotCount: 36, FromSlotCount: 24, PriceCurrency: 1, PriceAmount: 5),
    ];

    [Fact]
    public void TheNextRungUpIsBoughtWhenThePlayerCanPay()
    {
        var (verdict, rung) = StorageUpgradeRules.Decide(Rungs, "bag", currentSlots: 12, requestedSlots: 18, copper: 100);

        Assert.Equal(StorageUpgradeVerdict.Bought, verdict);
        Assert.Equal(1u, rung!.Id);
    }

    [Fact]
    public void ARungTheFirstCannotReachIsNoRung() =>
        Assert.Equal(StorageUpgradeVerdict.NoSuchRung,
            StorageUpgradeRules.Decide(Rungs, "bag", currentSlots: 12, requestedSlots: 24, copper: 1000).Verdict);

    [Fact]
    public void ARungPricedInAnotherCurrencyIsNoRung() =>
        Assert.Equal(StorageUpgradeVerdict.NoSuchRung,
            StorageUpgradeRules.Decide(Rungs, "bank", currentSlots: 24, requestedSlots: 36, copper: 1000).Verdict);

    [Fact]
    public void AnUnknownAreaIsNoRung() =>
        Assert.Equal(StorageUpgradeVerdict.NoSuchRung,
            StorageUpgradeRules.Decide(Rungs, "pocket", currentSlots: null, requestedSlots: 18, copper: 1000).Verdict);

    [Fact]
    public void ARungThePlayerCannotPayForIsRefusedAsUnaffordable() =>
        Assert.Equal(StorageUpgradeVerdict.CannotAfford,
            StorageUpgradeRules.Decide(Rungs, "bag", currentSlots: 18, requestedSlots: 24, copper: 249).Verdict);
}
