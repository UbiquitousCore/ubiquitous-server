namespace Ubiquitous.Data;

public static class InventoryTypes
{
    public const uint None = 0;
    public const uint Head = 1;
    public const uint Neck = 2;
    public const uint Shoulder = 3;
    public const uint Shirt = 4;
    public const uint Chest = 5;
    public const uint Legs = 6;
    public const uint Feet = 7;
    public const uint Hands = 8;
    public const uint Finger = 9;
    public const uint Trinket = 10;
    public const uint OneHand = 11;
    public const uint Ranged = 13;
    public const uint Back = 14;
    public const uint TwoHand = 15;
    public const uint MainHand = 18;
    public const uint OffHand = 19;
}

public static class EquipSlotRules
{
    public const int NoSlot = -1;

    private static readonly IReadOnlyDictionary<uint, int[]> HomesByType = new Dictionary<uint, int[]>
    {
        [InventoryTypes.Head] = [EquipmentSlots.Head],
        [InventoryTypes.Neck] = [EquipmentSlots.Neck],
        [InventoryTypes.Shoulder] = [EquipmentSlots.Shoulder],
        [InventoryTypes.Shirt] = [EquipmentSlots.Shirt],
        [InventoryTypes.Chest] = [EquipmentSlots.Chest],
        [InventoryTypes.Legs] = [EquipmentSlots.Legs],
        [InventoryTypes.Feet] = [EquipmentSlots.Feet],
        [InventoryTypes.Hands] = [EquipmentSlots.Hands],
        [InventoryTypes.Finger] = [EquipmentSlots.Ring1, EquipmentSlots.Ring2],
        [InventoryTypes.Trinket] = [EquipmentSlots.Trinket1, EquipmentSlots.Trinket2],
        [InventoryTypes.Back] = [EquipmentSlots.Back],
        [InventoryTypes.OneHand] = [EquipmentSlots.MainHand],
        [InventoryTypes.MainHand] = [EquipmentSlots.MainHand],
        [InventoryTypes.TwoHand] = [EquipmentSlots.MainHand],
        [InventoryTypes.OffHand] = [EquipmentSlots.OffHand],
        [InventoryTypes.Ranged] = [EquipmentSlots.Ranged],
    };

    private static int[] Homes(uint inventoryType) =>
        HomesByType.TryGetValue(inventoryType, out var homes) ? homes : [];

    public static int SlotFor(uint inventoryType, Func<int, uint?> wornTypeAt)
    {
        var homes = Homes(inventoryType);
        if (homes.Length == 0) return NoSlot;

        foreach (var home in homes)
        {
            if (wornTypeAt(home) is null) return home;
        }

        return homes[0];
    }

    public static bool IsValidSlot(uint inventoryType, int slot) =>
        Homes(inventoryType).Contains(slot);
}
