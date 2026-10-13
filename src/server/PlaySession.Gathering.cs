using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private readonly GatherCast _gathering = new();
    private readonly HashSet<string> _gatheredLoot = new(StringComparer.Ordinal);
    private long _miningSeen;

    private IReadOnlyList<string> BeginGather(string networkId)
    {
        if (_world is null || _character is null || _shared is null) return [];
        var casterId = WebNetworkId.FromString(PlayerUnitId);
        if (_gathering.InProgress) return [];
        if (_combat.IsPlayerDead || _spells.IsCasting)
            return [Reply(WebMessageTypes.SpellFailed, new SpellFailedPayload(
                casterId, MiningRules.GatherSpellId, WireCodes.SpellRefusal.AlreadyCasting))];
        if (!_staticObjectPositions.TryGetValue(networkId, out var at)
            || !InteractionRange.Within(_world.PlayerPosition, at))
            return [Error(WireCodes.ProtocolError.InvalidStaticObject, "Too far away.")];
        if (_shared.ActiveOreAt(networkId) is null)
            return [Error(WireCodes.ProtocolError.InvalidStaticObject, "This object has nothing to give.")];

        _content.Spells.TryGetValue(MiningRules.GatherSpellId, out var gatherSpell);
        var castTime = MiningRules.CastTime(gatherSpell, _character.Race);
        _gathering.Begin(networkId, Now, castTime);
        return [Reply(WebMessageTypes.SpellCasting, new SpellCastingPayload(
            MiningRules.GatherSpellId, casterId, (int)castTime.TotalMilliseconds))];
    }

    private void TickGathering(DateTimeOffset now, List<string> responses)
    {
        if (_world is null || _shared is null || !_gathering.InProgress) return;
        if (_combat.IsPlayerDead || _playerMoving || _combat.CurrentTargetOf(PlayerUnitId) is not null)
        {
            InterruptGather(responses);
            return;
        }

        if (_gathering.TakeFinished(now) is not { } finished) return;
        var casterId = WebNetworkId.FromString(PlayerUnitId);
        if (!_staticObjectPositions.TryGetValue(finished.NetworkId, out var at)
            || !InteractionRange.Within(_world.PlayerPosition, at)
            || _shared.ActiveOreAt(finished.NetworkId) is not { } ore)
        {
            responses.Add(Reply(WebMessageTypes.SpellInterrupted, new SpellInterruptedPayload(casterId)));
            return;
        }

        var target = WebNetworkId.FromString(finished.NetworkId);
        var loot = LootService.Gathered(ore.ItemId, MiningRules.OrePerNode);
        _loot[finished.NetworkId] = loot;
        _gatheredLoot.Add(finished.NetworkId);
        responses.Add(Reply(WebMessageTypes.SpellReleased,
            new SpellReleasedPayload(MiningRules.GatherSpellId, casterId, [target], [], 0f, Aimed: false)));
        responses.AddRange(LootWindowReplies(target, loot));
    }

    private void InterruptGather(List<string> responses)
    {
        if (!_gathering.Cancel()) return;
        responses.Add(Reply(WebMessageTypes.SpellInterrupted,
            new SpellInterruptedPayload(WebNetworkId.FromString(PlayerUnitId))));
    }

    private bool TakeGatheredOre(string networkId, List<string> refusal)
    {
        if (_belongings.Items.FreeSlots(_belongings.BagSlots) <= 0)
        {
            refusal.Add(ItemRefused(ItemErrors.InventoryFull));
            return false;
        }

        if (_shared?.Mine(networkId, Now) is not null) return true;
        _loot.Remove(networkId);
        _gatheredLoot.Remove(networkId);
        refusal.Add(Reply(WebMessageTypes.LootBusy, new LootBusyPayload(WebNetworkId.FromString(networkId), false)));
        return false;
    }

    private void LetGoOfGatherLoot(string networkId)
    {
        if (!_gatheredLoot.Remove(networkId)) return;
        _loot.Remove(networkId);
    }

    private void TickMiningView(List<string> responses)
    {
        if (_shared is null || _world is null || _instanceWorld is not null) return;
        var version = _shared.MiningVersion;
        if (version == _miningSeen) return;
        _miningSeen = version;
        RefreshView(responses);
    }

    private bool IsEmptiedGatherLoot(string networkId, CorpseLoot remaining, List<string> responses)
    {
        if (!_gatheredLoot.Contains(networkId) || !LootService.IsEmpty(remaining)) return false;
        _loot.Remove(networkId);
        _gatheredLoot.Remove(networkId);
        _visibleStaticObjectEntries.Remove(networkId);
        _staticObjectPositions.Remove(networkId);
        responses.Add(Reply(WebMessageTypes.EntityRemoved, new EntityRemovedPayload(WebNetworkId.FromString(networkId))));
        return true;
    }
}
