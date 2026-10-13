using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed class PlayerBelongings(GameContent content)
{
    public const int DefaultBagCapacity = 12;
    public const int DefaultBankCapacity = 24;

    public InventoryService Items { get; } = new();
    public int Currency { get; set; }
    public int BagSlots { get; set; } = DefaultBagCapacity;
    public int BankSlots { get; set; } = DefaultBankCapacity;

    public InventoryItemPayload PayloadFor(InventoryItemState item) => new(
        item.Id, item.Entry, item.Count, new StorageSlotPayload(item.Area, item.Slot),
        PrototypeFor(item.Entry));

    public ItemPrototypePayload? PrototypeFor(int entry)
    {
        if (!content.Items.TryGetValue(checked((uint)entry), out var proto)) return null;
        var spells = proto.OnUseSpellId is uint spellId
            ? new[] { new ItemSpellPayload((int)spellId, 1, (int)(proto.OnUseCooldownSeconds ?? 0)) }
            : [];
        return new ItemPrototypePayload(
            (int)proto.Id, proto.Class, proto.SubClass, (int)proto.DisplayInfoId,
            (int)proto.InventoryType, (int)proto.RequiredLevel,
            (int)(proto.BuyPrice ?? 0), (int)(proto.SellPrice ?? 0), spells);
    }
}
