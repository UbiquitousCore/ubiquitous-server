namespace Ubiquitous.Game;

using Ubiquitous.Common;
using Ubiquitous.Data;

public sealed record InventoryItemState(string Id, int Entry, int Count, int Slot, string Area = "bag");
public sealed record StorageSlotState(string Area, int Index);
public sealed record InventoryMoveResult(bool Accepted, InventoryItemState? Item, StorageSlotState? From, StorageSlotState? To, string? Reason = null, InventoryItemState? Displaced = null);

public sealed record InventoryEquipResult(
    bool Accepted,
    string Code,
    InventoryItemState? Item,
    InventoryItemState? Displaced,
    IReadOnlyList<InventoryRelocation> AlsoRemoved,
    IReadOnlyList<int> Equipment)
{
    public static InventoryEquipResult Refused(string code) => new(false, code, null, null, [], []);
}

public sealed record InventoryRelocation(InventoryItemState Item, StorageSlotState From);
public sealed record InventoryDestroyResult(bool Accepted, string? ItemId, string? Reason = null);
public sealed record InventorySplitResult(bool Accepted, InventoryItemState? Added, InventoryItemState? Source, string? Reason = null);
public sealed record InventorySortResult(bool Accepted, IReadOnlyList<InventoryItemState> Items, string? Reason = null);

public sealed record InventoryConsumeResult(
    bool Accepted,
    IReadOnlyList<string> RemovedItemIds,
    IReadOnlyList<InventoryItemState> ReducedItems);

public sealed class InventoryService
{
    private readonly List<InventoryItemState> _items = new();

    public IReadOnlyList<InventoryItemState> Items => _items;

    public void Load(IEnumerable<PersistedInventoryItem> items)
    {
        _items.Clear();
        foreach (var item in items) _items.Add(new InventoryItemState(item.Id, item.Entry, item.Count, item.Slot, item.Area));
    }

    public IReadOnlyList<PersistedInventoryItem> ToPersisted() =>
        _items.Select(item => new PersistedInventoryItem(item.Id, item.Entry, item.Count, item.Area, item.Slot)).ToArray();

    public void Seed(InventoryItemState item)
    {
        if (_items.All(existing => existing.Id != item.Id)) _items.Add(item);
    }

    public int FreeSlots(int bagCapacity) => FreeSlots("bag", bagCapacity);

    public int FreeSlots(string area, int capacity) =>
        Math.Max(0, capacity - _items.Count(item =>
            string.Equals(item.Area, area, StringComparison.Ordinal) && item.Slot >= 0 && item.Slot < capacity));

    public InventoryItemState? Add(int entry, int count, int bagCapacity)
    {
        var used = _items.Where(item => string.Equals(item.Area, "bag", StringComparison.Ordinal))
            .Select(item => item.Slot).ToHashSet();
        var slot = Enumerable.Range(0, bagCapacity).FirstOrDefault(free => !used.Contains(free), -1);
        if (slot < 0) return null;
        var added = new InventoryItemState($"item-{entry}-{_items.Count + 1}", entry, count, slot);
        _items.Add(added);
        return added;
    }

    public InventoryMoveResult Move(StorageSlotState from, StorageSlotState to, int bagCapacity, int bankCapacity)
    {
        var item = _items.FirstOrDefault(candidate =>
            string.Equals(candidate.Area, from.Area, StringComparison.Ordinal) && candidate.Slot == from.Index);
        if (item is null)
            return new InventoryMoveResult(false, null, null, null, "No item at the source slot.");

        if (!IsStorageArea(to.Area) || to.Index < 0 || to.Index >= CapacityFor(to.Area, bagCapacity, bankCapacity))
            return new InventoryMoveResult(false, null, null, null, "Destination slot is out of range.");

        var sitting = _items.FirstOrDefault(other => other.Id != item.Id
            && string.Equals(other.Area, to.Area, StringComparison.Ordinal) && other.Slot == to.Index);

        var moved = item with { Area = to.Area, Slot = to.Index };
        _items[_items.IndexOf(item)] = moved;
        if (sitting is null)
            return new InventoryMoveResult(true, moved, from, to);

        var displaced = sitting with { Area = from.Area, Slot = from.Index };
        _items[_items.IndexOf(sitting)] = displaced;
        return new InventoryMoveResult(true, moved, from, to, null, displaced);
    }

    public const string EquipmentArea = "equipment";

    public IReadOnlyList<int> WornEquipment() => EquipmentSlots.Build(
        [.. _items.Where(IsWorn).Select(item => (item.Slot, item.Entry))]);

    public void SeedWorn(IReadOnlyList<int> equipment)
    {
        for (var slot = 0; slot < Math.Min(equipment.Count, EquipmentSlots.Count); slot++)
        {
            var entry = equipment[slot];
            if (entry <= 0 || WornAt(slot) is not null) continue;
            _items.Add(new InventoryItemState($"worn-{slot}-{entry}", entry, 0, slot, EquipmentArea));
        }
    }

    public void ResettleWorn(Func<int, uint> inventoryTypeOf)
    {
        var misplaced = _items
            .Where(item => IsWorn(item)
                && inventoryTypeOf(item.Entry) != InventoryTypes.None
                && !EquipSlotRules.IsValidSlot(inventoryTypeOf(item.Entry), item.Slot))
            .ToList();
        foreach (var item in misplaced) _items.Remove(item);

        foreach (var item in misplaced)
        {
            var home = EquipSlotRules.SlotFor(
                inventoryTypeOf(item.Entry), slot => WornAt(slot) is { } worn ? inventoryTypeOf(worn.Entry) : null);
            var occupant = WornAt(home);
            if (occupant is null) _items.Add(item with { Slot = home });
            else if (occupant.Entry != item.Entry) _items.Add(item);
        }
    }

    public InventoryItemState? WornAt(int slot) =>
        _items.FirstOrDefault(item => IsWorn(item) && item.Slot == slot);

    public InventoryEquipResult Equip(
        string itemId,
        StorageSlotState from,
        int? toSlot,
        Func<int, uint> inventoryTypeOf,
        int bagCapacity)
    {
        var item = _items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null || !IsBagItemAt(item, from))
            return InventoryEquipResult.Refused(ItemErrors.ItemNotInSlot);

        var inventoryType = inventoryTypeOf(item.Entry);
        var destination = toSlot switch
        {
            { } named when EquipSlotRules.IsValidSlot(inventoryType, named) => named,
            { } => EquipSlotRules.NoSlot,
            null => EquipSlotRules.SlotFor(
                inventoryType, slot => WornAt(slot) is { } worn ? inventoryTypeOf(worn.Entry) : null),
        };
        if (destination == EquipSlotRules.NoSlot)
            return InventoryEquipResult.Refused(ItemErrors.WrongSlot);

        var occupant = WornAt(destination);
        var knockedOff = ConflictingHand(inventoryType, destination, inventoryTypeOf);
        int? knockedOffTo = null;
        if (knockedOff is not null)
        {
            knockedOffTo = FirstFreeBagSlot(bagCapacity, excluding: occupant is null ? null : from.Index, vacated: from.Index);
            if (knockedOffTo is null) return InventoryEquipResult.Refused(ItemErrors.InventoryFull);
        }

        var equipped = item with { Area = EquipmentArea, Slot = destination };
        Replace(item, equipped);

        InventoryItemState? displaced = null;
        if (occupant is not null)
        {
            displaced = occupant with { Area = "bag", Slot = from.Index };
            Replace(occupant, displaced);
        }

        var alsoRemoved = new List<InventoryRelocation>();
        if (knockedOff is not null)
        {
            var stowed = knockedOff with { Area = "bag", Slot = knockedOffTo!.Value };
            Replace(knockedOff, stowed);
            alsoRemoved.Add(new InventoryRelocation(stowed, new StorageSlotState(EquipmentArea, knockedOff.Slot)));
        }

        return new InventoryEquipResult(true, ItemErrors.Ok, equipped, displaced, alsoRemoved, WornEquipment());
    }

    private InventoryItemState? ConflictingHand(uint inventoryType, int destination, Func<int, uint> inventoryTypeOf)
    {
        if (inventoryType == InventoryTypes.TwoHand && destination != EquipmentSlots.OffHand)
            return WornAt(EquipmentSlots.OffHand);

        if (destination == EquipmentSlots.OffHand
            && WornAt(EquipmentSlots.MainHand) is { } mainhand
            && inventoryTypeOf(mainhand.Entry) == InventoryTypes.TwoHand)
            return mainhand;

        return null;
    }

    public InventoryEquipResult Unequip(
        string itemId, StorageSlotState from, StorageSlotState? to, int bagCapacity)
    {
        var item = _items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null || !IsWorn(item)
            || !string.Equals(from.Area, EquipmentArea, StringComparison.Ordinal)
            || from.Index != item.Slot)
            return InventoryEquipResult.Refused(ItemErrors.ItemNotInSlot);

        int destination;
        if (to is not null)
        {
            if (!string.Equals(to.Area, "bag", StringComparison.Ordinal)
                || to.Index < 0 || to.Index >= bagCapacity)
                return InventoryEquipResult.Refused(ItemErrors.WrongSlot);
            if (BagItemAt(to.Index) is not null)
                return InventoryEquipResult.Refused(ItemErrors.DestinationOccupied);
            destination = to.Index;
        }
        else
        {
            var free = FirstFreeBagSlot(bagCapacity);
            if (free is null) return InventoryEquipResult.Refused(ItemErrors.InventoryFull);
            destination = free.Value;
        }

        var removed = item with { Area = "bag", Slot = destination };
        Replace(item, removed);
        return new InventoryEquipResult(true, ItemErrors.Ok, removed, null, [], WornEquipment());
    }

    private int? FirstFreeBagSlot(int bagCapacity, int? excluding = null, int? vacated = null)
    {
        for (var slot = 0; slot < bagCapacity; slot++)
        {
            if (slot == excluding) continue;
            if (slot != vacated && BagItemAt(slot) is not null) continue;
            return slot;
        }

        return null;
    }

    private InventoryItemState? BagItemAt(int slot) => _items.FirstOrDefault(item =>
        string.Equals(item.Area, "bag", StringComparison.Ordinal) && item.Slot == slot);

    private static bool IsWorn(InventoryItemState item) =>
        string.Equals(item.Area, EquipmentArea, StringComparison.Ordinal);

    private void Replace(InventoryItemState old, InventoryItemState updated) =>
        _items[_items.IndexOf(old)] = updated;

    public InventoryDestroyResult Destroy(string itemId, StorageSlotState from, int count)
    {
        var item = _items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null || !IsBagItemAt(item, from) || count != item.Count)
            return new InventoryDestroyResult(false, null, "Item, source slot, or count is stale.");
        _items.Remove(item);
        return new InventoryDestroyResult(true, item.Id);
    }

    public InventoryConsumeResult Consume(int entry, int count)
    {
        if (count <= 0) return new InventoryConsumeResult(true, [], []);
        var matching = _items.Where(item => item.Entry == entry
            && string.Equals(item.Area, "bag", StringComparison.Ordinal)).OrderBy(item => item.Slot).ToArray();
        if (matching.Sum(item => item.Count) < count) return new InventoryConsumeResult(false, [], []);

        var removed = new List<string>();
        var reduced = new List<InventoryItemState>();
        var remaining = count;
        foreach (var item in matching)
        {
            if (remaining <= 0) break;
            var take = Math.Min(item.Count, remaining);
            remaining -= take;
            if (take == item.Count)
            {
                _items.Remove(item);
                removed.Add(item.Id);
            }
            else
            {
                var updated = item with { Count = item.Count - take };
                _items[_items.IndexOf(item)] = updated;
                reduced.Add(updated);
            }
        }

        return new InventoryConsumeResult(true, removed, reduced);
    }

    public InventoryConsumeResult ConsumeOneOf(string itemId)
    {
        var item = _items.FirstOrDefault(candidate =>
            candidate.Id == itemId && string.Equals(candidate.Area, "bag", StringComparison.Ordinal));
        if (item is null) return new InventoryConsumeResult(false, [], []);
        if (item.Count <= 1)
        {
            _items.Remove(item);
            return new InventoryConsumeResult(true, [item.Id], []);
        }

        var updated = item with { Count = item.Count - 1 };
        _items[_items.IndexOf(item)] = updated;
        return new InventoryConsumeResult(true, [], [updated]);
    }

    public InventoryItemState? TakeOutBagItem(string itemId)
    {
        var item = _items.FirstOrDefault(candidate =>
            candidate.Id == itemId && string.Equals(candidate.Area, "bag", StringComparison.Ordinal));
        if (item is null) return null;
        _items.Remove(item);
        return item;
    }

    public InventorySplitResult Split(string itemId, StorageSlotState from, int count, StorageSlotState to)
    {
        var item = _items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null || !IsBagItemAt(item, from) || count <= 0 || count >= item.Count
            || !string.Equals(to.Area, "bag", StringComparison.Ordinal))
            return new InventorySplitResult(false, null, null, "Item, source, count, or destination is invalid.");
        var added = item with { Id = $"{item.Id}-split-{_items.Count + 1}", Count = count, Slot = to.Index };
        var source = item with { Count = item.Count - count };
        _items[_items.IndexOf(item)] = source;
        _items.Add(added);
        return new InventorySplitResult(true, added, source);
    }

    public InventorySortResult Sort(string area)
    {
        if (!IsStorageArea(area))
            return new InventorySortResult(false, [], "Unrecognized storage area.");

        var ordered = _items.Where(item => string.Equals(item.Area, area, StringComparison.Ordinal))
            .OrderBy(item => item.Entry).ThenBy(item => item.Slot).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var original = ordered[i];
            var reslotted = original with { Slot = i };
            _items[_items.IndexOf(original)] = reslotted;
            ordered[i] = reslotted;
        }

        return new InventorySortResult(true, ordered);
    }

    private static bool IsBagItemAt(InventoryItemState item, StorageSlotState from) =>
        string.Equals(from.Area, "bag", StringComparison.Ordinal)
        && string.Equals(item.Area, "bag", StringComparison.Ordinal)
        && from.Index == item.Slot;

    private static bool IsStorageArea(string area) => area is "bag" or "bank";

    private static int CapacityFor(string area, int bagCapacity, int bankCapacity) =>
        string.Equals(area, "bag", StringComparison.Ordinal) ? bagCapacity : bankCapacity;
}
