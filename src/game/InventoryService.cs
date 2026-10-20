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
public sealed record InventoryCombineResult(InventoryItemState Into, InventoryItemState? Left, string? EmptiedId);
public sealed record InventoryDestroyResult(bool Accepted, string? ItemId, string? Reason = null);
public sealed record InventorySplitResult(bool Accepted, InventoryItemState? Added, InventoryItemState? Source, string? Reason = null);
public sealed record InventorySortResult(
    bool Accepted, IReadOnlyList<InventoryItemState> Items, string? Reason = null, IReadOnlyList<string>? RemovedIds = null);

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

    // Tops up the bag's part-stacks of the item before opening new slots, and
    // takes nothing unless all of it fits. The stacks touched come back, new or not.
    // An item with no stack limit of more than one arrives whole, in one slot,
    // as it did before limits were read.
    public IReadOnlyList<InventoryItemState>? Add(int entry, int count, int bagCapacity, int stackLimit = 0)
    {
        var limit = stackLimit;
        if (stackLimit <= 1) return FirstFreeBagSlot(bagCapacity) is { } slot ? [Place(entry, count, slot)] : null;
        var partial = _items
            .Where(item => item.Entry == entry && item.Count < limit && string.Equals(item.Area, "bag", StringComparison.Ordinal))
            .OrderBy(item => item.Slot).ToList();
        var leftover = Math.Max(0, count - partial.Sum(item => limit - item.Count));
        var used = _items.Where(item => string.Equals(item.Area, "bag", StringComparison.Ordinal))
            .Select(item => item.Slot).ToHashSet();
        var free = Enumerable.Range(0, bagCapacity).Where(slot => !used.Contains(slot)).ToList();
        if ((leftover + limit - 1) / limit > free.Count) return null;

        var touched = new List<InventoryItemState>();
        var remaining = count;
        foreach (var stack in partial)
        {
            if (remaining <= 0) break;
            var poured = Math.Min(limit - stack.Count, remaining);
            remaining -= poured;
            var topped = stack with { Count = stack.Count + poured };
            Replace(stack, topped);
            touched.Add(topped);
        }

        for (var at = 0; remaining > 0; at++)
        {
            var size = Math.Min(limit, remaining);
            remaining -= size;
            touched.Add(Place(entry, size, free[at]));
        }

        return touched;
    }

    private InventoryItemState Place(int entry, int count, int slot)
    {
        var added = new InventoryItemState(FreshId(entry), entry, count, slot);
        _items.Add(added);
        return added;
    }

    private string FreshId(int entry)
    {
        for (var n = _items.Count + 1; ; n++)
        {
            var id = $"item-{entry}-{n}";
            if (_items.All(item => item.Id != id)) return id;
        }
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

    // A stack dropped on a part-stack of the same item pours into it; what
    // does not fit stays where it was. Nothing to pour into answers null,
    // and the drop is an ordinary move.
    public InventoryCombineResult? Combine(StorageSlotState from, StorageSlotState to, int stackLimit)
    {
        var source = StoredAt(from);
        var target = StoredAt(to);
        if (source is null || target is null || source.Id == target.Id || source.Entry != target.Entry) return null;
        if (stackLimit <= 1 || target.Count >= stackLimit) return null;

        var poured = Math.Min(stackLimit - target.Count, source.Count);
        var into = target with { Count = target.Count + poured };
        Replace(target, into);
        if (poured == source.Count)
        {
            _items.Remove(source);
            return new InventoryCombineResult(into, null, source.Id);
        }

        var left = source with { Count = source.Count - poured };
        Replace(source, left);
        return new InventoryCombineResult(into, left, null);
    }

    private InventoryItemState? StoredAt(StorageSlotState at) => IsStorageArea(at.Area)
        ? _items.FirstOrDefault(item => string.Equals(item.Area, at.Area, StringComparison.Ordinal) && item.Slot == at.Index)
        : null;

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

    // The new stack lands in the slot asked for while it is empty, otherwise
    // in the first empty one: a split never shares a slot.
    public InventorySplitResult Split(string itemId, StorageSlotState from, int count, StorageSlotState to, int bagCapacity)
    {
        var item = _items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null || !IsBagItemAt(item, from) || count <= 0 || count >= item.Count
            || !string.Equals(to.Area, "bag", StringComparison.Ordinal))
            return new InventorySplitResult(false, null, null, "Item, source, count, or destination is invalid.");
        var slot = to.Index >= 0 && to.Index < bagCapacity && BagItemAt(to.Index) is null
            ? to.Index
            : FirstFreeBagSlot(bagCapacity);
        if (slot is null) return new InventorySplitResult(false, null, null, "There is no empty slot to split into.");
        var added = item with { Id = FreshId(item.Entry), Count = count, Slot = slot.Value };
        var source = item with { Count = item.Count - count };
        _items[_items.IndexOf(item)] = source;
        _items.Add(added);
        return new InventorySplitResult(true, added, source);
    }

    public InventorySortResult Sort(string area, Func<int, ItemDefinition?> describe)
    {
        if (!IsStorageArea(area))
            return new InventorySortResult(false, [], "Unrecognized storage area.");

        var held = _items.Where(item => string.Equals(item.Area, area, StringComparison.Ordinal))
            .OrderBy(item => item.Slot).ToList();
        var removed = new List<string>();
        var kept = new List<InventoryItemState>();
        foreach (var same in held.GroupBy(item => item.Entry))
            kept.AddRange(CombineStacks([.. same], StackLimit(describe(same.Key)), removed));

        var ordered = kept
            .OrderBy(item => item, Comparer<InventoryItemState>.Create((a, b) => CompareForSort(a, b, describe)))
            .Select((item, slot) => item with { Slot = slot })
            .ToList();

        _items.RemoveAll(item => string.Equals(item.Area, area, StringComparison.Ordinal));
        _items.AddRange(ordered);
        return new InventorySortResult(true, ordered, RemovedIds: removed);
    }

    private static int StackLimit(ItemDefinition? item) => (int)Math.Max(1u, item?.MaxStack ?? 1u);

    // Only partial stacks are poured together: a stack already at or over the
    // limit stays whole, so sorting never needs more slots than it started with.
    private static IEnumerable<InventoryItemState> CombineStacks(
        IReadOnlyList<InventoryItemState> stacks, int limit, List<string> removed)
    {
        var partial = stacks.Where(stack => stack.Count < limit).ToList();
        foreach (var full in stacks.Where(stack => stack.Count >= limit)) yield return full;
        if (partial.Count == 0) yield break;

        var total = partial.Sum(stack => Math.Max(1, stack.Count));
        var index = 0;
        for (; total > 0; index++)
        {
            var count = Math.Min(limit, total);
            total -= count;
            yield return partial[index] with { Count = count };
        }

        removed.AddRange(partial.Skip(index).Select(stack => stack.Id));
    }

    // Own choice, as asked by the owner: group, then quality, worth and item
    // level from lowest to highest, then name.
    private static int CompareForSort(InventoryItemState a, InventoryItemState b, Func<int, ItemDefinition?> describe)
    {
        var x = describe(a.Entry);
        var y = describe(b.Entry);
        int order = (x?.Class ?? byte.MaxValue).CompareTo(y?.Class ?? byte.MaxValue);
        if (order == 0) order = (x?.Quality ?? 0).CompareTo(y?.Quality ?? 0);
        if (order == 0) order = (x?.SellPrice ?? 0).CompareTo(y?.SellPrice ?? 0);
        if (order == 0) order = (x?.ItemLevel ?? 0).CompareTo(y?.ItemLevel ?? 0);
        if (order == 0) order = string.Compare(x?.NameKey, y?.NameKey, StringComparison.OrdinalIgnoreCase);
        if (order == 0) order = a.Entry.CompareTo(b.Entry);
        if (order == 0) order = b.Count.CompareTo(a.Count);
        return order != 0 ? order : a.Slot.CompareTo(b.Slot);
    }

    private static bool IsBagItemAt(InventoryItemState item, StorageSlotState from) =>
        string.Equals(from.Area, "bag", StringComparison.Ordinal)
        && string.Equals(item.Area, "bag", StringComparison.Ordinal)
        && from.Index == item.Slot;

    private static bool IsStorageArea(string area) => area is "bag" or "bank";

    private static int CapacityFor(string area, int bagCapacity, int bankCapacity) =>
        string.Equals(area, "bag", StringComparison.Ordinal) ? bagCapacity : bankCapacity;
}
