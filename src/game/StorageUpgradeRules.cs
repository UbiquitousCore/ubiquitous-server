using Ubiquitous.Data;

namespace Ubiquitous.Game;

public enum StorageUpgradeVerdict
{
    NoSuchRung,

    CannotAfford,

    Bought,
}

public static class StorageUpgradeRules
{
    public static (StorageUpgradeVerdict Verdict, StorageUpgradeDefinition? Rung) Decide(
        IEnumerable<StorageUpgradeDefinition> rungs, string area, int? currentSlots, uint requestedSlots, int copper)
    {
        var rung = currentSlots is int from
            ? rungs.FirstOrDefault(candidate => candidate.PriceCurrency == 0
                && candidate.FromSlotCount == from
                && candidate.SlotCount == requestedSlots
                && string.Equals(candidate.Area, area, StringComparison.Ordinal))
            : null;
        if (rung is null) return (StorageUpgradeVerdict.NoSuchRung, null);
        return rung.PriceAmount <= (uint)Math.Max(0, copper)
            ? (StorageUpgradeVerdict.Bought, rung)
            : (StorageUpgradeVerdict.CannotAfford, rung);
    }
}
