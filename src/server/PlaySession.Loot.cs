using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private IReadOnlyList<string> LootOpen(JsonElement payload)
    {
        if (_world is null || _character is null) return [];
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null || !(_combat.IsDead(networkId) || _gatheredLoot.Contains(networkId))
            || !_loot.TryGetValue(networkId, out var loot) || !InLootRange(networkId))
            return [];
        return LootWindowReplies(WebNetworkId.FromString(networkId), loot);
    }

    private IReadOnlyList<string> LootWindowReplies(WebNetworkId target, CorpseLoot loot) =>
    [
        Reply(WebMessageTypes.LootContents, new LootContentsPayload(target, loot.Money,
            loot.Rows.Select(row => new LootRowPayload(row.Slot, row.ItemId, row.Entry, row.Count)).ToArray())),
        Reply(WebMessageTypes.LootBusy, new LootBusyPayload(target, true)),
    ];

    private IReadOnlyList<string> LootTake(JsonElement payload)
    {
        if (_world is null || _character is null) return [];
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null || !_loot.TryGetValue(networkId, out var loot) || !InLootRange(networkId))
            return [];
        if (!payload.TryGetProperty("slot", out var slotValue) || slotValue.ValueKind != JsonValueKind.Number)
            return [];
        var (row, remaining) = _lootService.Take(loot, slotValue.GetInt32());
        if (row is null) return [];
        var refused = new List<string>();
        if (_gatheredLoot.Contains(networkId) && !TakeGatheredOre(networkId, refused)) return refused;
        var added = _belongings.Items.Add(row.Entry, row.Count, _belongings.BagSlots);
        if (added is null) return [ItemRefused(ItemErrors.InventoryFull)];
        _loot[networkId] = remaining;
        var target = WebNetworkId.FromString(networkId);
        var looter = WebNetworkId.FromString(PlayerUnitId);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.LootSlotEmptied,
                new LootSlotEmptiedPayload(target, looter, false, row.Slot)),
            Reply(WebMessageTypes.InventoryItemAdded, _belongings.PayloadFor(added)),
        };
        if (!IsEmptiedGatherLoot(networkId, remaining, responses) && LootService.IsEmpty(remaining))
        {
            HoldCorpse(networkId, Now, carriesLoot: false);
            responses.Add(Reply(WebMessageTypes.EntityLootable,
                new EntityLootablePayload(target, WireCodes.Loot.Emptied)));
        }

        responses.AddRange(HeldObjectiveUpdates());
        return responses;
    }

    private IReadOnlyList<string> LootTakeMoney(JsonElement payload)
    {
        if (_world is null || _character is null) return [];
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null || !_loot.TryGetValue(networkId, out var loot) || !InLootRange(networkId))
            return [];
        var (amount, remaining) = _lootService.TakeMoney(loot);
        if (amount <= 0) return [];
        _loot[networkId] = remaining;
        _belongings.Currency += amount;
        var target = WebNetworkId.FromString(networkId);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.LootSlotEmptied, new LootSlotEmptiedPayload(
                target, WebNetworkId.FromString(PlayerUnitId), true)),
            Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
                new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = _belongings.Currency })),
        };
        if (LootService.IsEmpty(remaining))
        {
            HoldCorpse(networkId, Now, carriesLoot: false);
            responses.Add(Reply(WebMessageTypes.EntityLootable,
                new EntityLootablePayload(target, WireCodes.Loot.Emptied)));
        }

        return responses;
    }

    private IReadOnlyList<string> LootClose(JsonElement payload)
    {
        if (_world is null) return [];
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null) return [];
        LetGoOfGatherLoot(networkId);
        return [Reply(WebMessageTypes.LootBusy,
            new LootBusyPayload(WebNetworkId.FromString(networkId), false))];
    }

    private bool InLootRange(string networkId)
    {
        if (_shared is null || !_shared.TryGetPosition(networkId, out var corpse) || _world is null) return false;
        var player = _world.PlayerPosition;
        var distance = Math.Sqrt(
            Math.Pow(player.X - corpse.X, 2) + Math.Pow(player.Y - corpse.Y, 2) + Math.Pow(player.Z - corpse.Z, 2));
        return distance <= LootRange;
    }
}
