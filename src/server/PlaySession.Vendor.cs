using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private string AuctioneerOpen(JsonElement payload)
    {
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null || !_sight.Entries.TryGetValue(networkId, out var entry))
            return Error(WireCodes.ProtocolError.InvalidAuctioneer, "Auctioneer is not visible.");
        if (!_content.Npcs.TryGetValue(entry, out var npc) || (npc.Flags & NpcFlags.Auctioneer) == 0)
            return Error(WireCodes.ProtocolError.InvalidAuctioneer, "NPC does not stand an auction house.");
        return Reply(WebMessageTypes.AuctionOpened, new AuctionOpenedPayload(networkId));
    }

    private string VendorOpen(JsonElement payload)
    {
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null || !_sight.Entries.TryGetValue(networkId, out var entry))
            return Error(WireCodes.ProtocolError.InvalidVendor, "Vendor is not visible.");
        var result = _vendors.Open(networkId, entry, _content.Npcs, TabsFor(entry));
        return result is null ? Error(WireCodes.ProtocolError.InvalidVendor, "NPC is not a vendor.")
            : Reply(WebMessageTypes.VendorItems, new VendorItemsPayload(result.Tabs.Select(tab =>
                new VendorTabPayload(tab.TabIndex, tab.Currency, tab.Count, tab.ItemDataDecoded,
                    tab.Items.Select(item => new VendorItemPayload(item.Slot, item.Entry,
                        item.NameTextId, item.DisplayInfoId, item.BuyPrice, null,
                        item.StackCount, LessonStatusOf(item.Entry))).ToArray())).ToArray()));
    }

    // What a vendor card says of a spell lesson: already known, or not takeable by this character (another class, too
    // high a level, a skill missing). The rules are those of reading the lesson, so the card never promises otherwise.
    private string? LessonStatusOf(int entry)
    {
        if (!_content.Items.TryGetValue(checked((uint)entry), out var proto)
            || proto.OnUseSpellId is not uint lessonId
            || !_content.Spells.TryGetValue(lessonId, out var lesson))
            return null;
        var taught = SpellLessons.Taught(lesson).Where(_content.Spells.ContainsKey).ToArray();
        if (taught.Length == 0) return null;
        if (taught.All(_spells.Known.Contains)) return WireCodes.Lesson.Learned;
        var takeable = MeetsItemRequirements(proto)
            && SpellLessons.KnowsRequiredSpell(proto, _spells.Known, _content.Items.Values, _content.Spells, MeetsItemRequirements);
        return takeable ? null : WireCodes.Lesson.Cannot;
    }

    private IReadOnlyList<string> VendorBuy(JsonElement payload)
    {
        if (_world is null) return [Error(WireCodes.ProtocolError.InvalidVendor, "Character selection is required before trading.")];
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null || !_sight.Entries.TryGetValue(networkId, out var entryId))
            return [BuyRefused(VendorService.ResultNotVendor)];
        if (!payload.TryGetProperty("slot", out var slotValue) || slotValue.ValueKind != JsonValueKind.Number
            || !payload.TryGetProperty("entry", out var entryValue) || entryValue.ValueKind != JsonValueKind.Number
            || !payload.TryGetProperty("count", out var countValue) || countValue.ValueKind != JsonValueKind.Number)
            return [Error(WireCodes.ProtocolError.InvalidVendor, "Vendor slot, entry, and count are required.")];
        var result = _vendors.Buy(networkId, entryId, _content.Npcs, TabsFor(entryId),
            slotValue.GetInt32(), entryValue.GetInt32(), countValue.GetInt32(), _belongings.Currency);
        if (result.Code != VendorService.ResultOk) return [BuyRefused(result.Code)];
        var added = _belongings.Receive(result.Entry, result.Count);
        if (added is null)
            return [BuyRefused(VendorService.ResultBagsFull), ItemRefused(ItemErrors.InventoryFull)];
        _belongings.Currency -= result.TotalPrice;
        return
        [
            Reply(WebMessageTypes.BuyResult, new BuyResultPayload(VendorService.ResultOk)),
            .. added.Select(item => Reply(WebMessageTypes.InventoryItemAdded, _belongings.PayloadFor(item))),
            Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
                new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = _belongings.Currency })),
            .. HeldObjectiveUpdates(),
        ];
    }

    private string BuyRefused(string code) =>
        Reply(WebMessageTypes.BuyResult, new BuyResultPayload(code));

    private IReadOnlyList<string> VendorSell(JsonElement payload)
    {
        if (_world is null) return [SellRefused(VendorService.ResultNotVendor)];
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null || !_sight.Entries.TryGetValue(networkId, out var entryId))
            return [SellRefused(VendorService.ResultNotVendor)];
        var request = WebMessageJson.DeserializePayload<VendorSellPayload>(payload);
        var item = _belongings.Items.Items.FirstOrDefault(candidate => candidate.Id == request.ItemId);
        if (item is null || !string.Equals(request.From.Area, "bag", StringComparison.Ordinal)
            || request.From.Index != item.Slot)
            return [SellRefused(ItemErrors.ItemNotInSlot)];

        var result = _vendors.Sell(networkId, entryId, _content.Npcs, TabsFor(entryId), _content.Items,
            item.Entry, item.Count);
        if (result.Code != VendorService.ResultOk) return [SellRefused(result.Code)];

        var removed = _belongings.Items.Destroy(item.Id, new StorageSlotState(request.From.Area, request.From.Index), item.Count);
        if (!removed.Accepted) return [SellRefused(ItemErrors.ItemNotInSlot)];
        _belongings.Currency += result.Price;
        return
        [
            Reply(WebMessageTypes.SellResult, new SellResultPayload(VendorService.ResultOk)),
            Reply(WebMessageTypes.InventoryItemRemoved, new InventoryItemRemovedPayload(item.Id)),
            Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
                new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = _belongings.Currency })),
            .. HeldObjectiveUpdates(),
        ];
    }

    private string SellRefused(string code) =>
        Reply(WebMessageTypes.SellResult, new SellResultPayload(code));

    private IReadOnlyList<string> StaticObjectInteract(JsonElement payload)
    {
        if (_world is null)
            return [Error(WireCodes.ProtocolError.InvalidStaticObject, "Character selection is required before interacting.")];
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null || !_visibleStaticObjectEntries.TryGetValue(networkId, out var entry))
            return [Error(WireCodes.ProtocolError.InvalidStaticObject, "That is not a visible static object.")];

        if (MiningOres.ByNodeEntry(entry) is not null) return BeginGather(networkId);

        var itemBinding = _content.QuestItemBindingsByStaticObject
            .FirstOrDefault(b => b.StaticObjectEntry == entry);
        if (itemBinding is not null)
            return StaticObjectItemInteract(itemBinding);

        var triggeredQuests = _content.Quests
            .Where(quest => quest.Objectives.Any(objective =>
                objective.Kind == QuestObjectiveKind.Triggered && objective.ObjectiveObject == entry))
            .Select(quest => quest.Id)
            .ToHashSet();
        if (triggeredQuests.Count > 0)
            return StaticObjectTriggerInteract(entry, triggeredQuests);

        return [Error(WireCodes.ProtocolError.InvalidStaticObject, "This object has nothing to give.")];
    }

    private IReadOnlyList<string> StaticObjectItemInteract(StaticObjectItemBindingDefinition binding)
    {
        if (!_quests.Accepted.Contains(binding.QuestId))
            return [Error(WireCodes.ProtocolError.InvalidStaticObject, "You do not hold the quest this requires.")];

        var objective = _content.Quests.FirstOrDefault(q => q.Id == binding.QuestId)?.Objectives
            .FirstOrDefault(o => o.Kind == QuestObjectiveKind.Held && o.ObjectiveItem == binding.ItemEntry);
        if (objective is null || HeldCountFor(binding.ItemEntry) >= objective.GoalCount)
            return [Error(WireCodes.ProtocolError.InvalidStaticObject, "You already have everything this gives.")];

        var added = _belongings.Receive((int)binding.ItemEntry, 1);
        if (added is null)
            return [Error(WireCodes.ProtocolError.InvalidStaticObject, "Your bags are full.")];

        return
        [
            .. added.Select(item => Reply(WebMessageTypes.InventoryItemAdded, _belongings.PayloadFor(item))),
            .. HeldObjectiveUpdates(),
        ];
    }

    private IReadOnlyList<string> StaticObjectTriggerInteract(uint entry, IReadOnlySet<uint> questIds)
    {
        if (!_quests.Accepted.Any(questIds.Contains))
            return [Error(WireCodes.ProtocolError.InvalidStaticObject, "You do not hold the quest this requires.")];

        var updates = _quests.RecordInteraction(entry, _content.Quests);
        if (updates.Count == 0)
            return [Error(WireCodes.ProtocolError.InvalidStaticObject, "You already have everything this gives.")];

        return updates.Select(update => Reply(WebMessageTypes.QuestUpdated, QuestUpdatePayloadFor(update))).ToArray();
    }

    private IReadOnlyList<VendorTab> TabsFor(uint entryId) =>
        _vendorTabs.TryGetValue(entryId, out var tabs) ? tabs : [];
}
