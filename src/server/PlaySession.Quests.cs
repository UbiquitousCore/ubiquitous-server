using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private string QuestCheckMark(JsonElement payload)
    {
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null || !_sight.Entries.TryGetValue(networkId, out var entry))
            return Error(WireCodes.ProtocolError.InvalidQuest, "Quest giver is not visible.");
        var mark = _quests.GetStatus(networkId, entry, _content.Npcs, _content.Quests, _character?.Class, _level,
            _content.QuestFinishersByCreature, _content.QuestFinishersByStaticObject, _spells.Known)?.Status ?? QuestMark.None;
        return Reply(WebMessageTypes.QuestStatuses, new QuestStatusesPayload(
            [new QuestStatusRow(WebNetworkId.FromString(networkId), QuestService.WireStatus(mark))]));
    }

    private string QuestRefreshStatuses()
    {
        var statuses = _sight.Entries
            .Select(pair => _quests.GetStatus(pair.Key, pair.Value, _content.Npcs, _content.Quests,
                _character?.Class, _level, _content.QuestFinishersByCreature, _content.QuestFinishersByStaticObject, _spells.Known))
            .Where(status => status is not null)
            .Select(status => new QuestStatusRow(
                WebNetworkId.FromString(status!.NetworkId), QuestService.WireStatus(status.Status)))
            .ToArray();
        return Reply(WebMessageTypes.QuestStatuses, new QuestStatusesPayload(statuses));
    }

    private string QuestList(JsonElement payload)
    {
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null) return Error(WireCodes.ProtocolError.InvalidQuest, "Quest giver is not visible.");
        uint askedQuestId = payload.TryGetProperty("questId", out var askedQuestIdValue)
            && askedQuestIdValue.TryGetUInt32(out var parsedAskedQuestId)
                ? parsedAskedQuestId
                : 0;

        var handIns = QuestsFinishableAt(networkId);
        IReadOnlyList<QuestDefinition> offers;
        if (_sight.Entries.TryGetValue(networkId, out var npcEntry))
        {
            var result = _questLists.List(npcEntry, _content.Quests, _content.Npcs, _character?.Class,
                _quests.Accepted, _quests.Completed, _level, _spells.Known);
            if (result is null && handIns.Count == 0)
                return Error(WireCodes.ProtocolError.InvalidQuest, "Entity is not a quest giver.");
            offers = result?.Quests ?? [];
        }
        else if (_visibleStaticObjectEntries.TryGetValue(networkId, out var staticEntry))
        {
            var bound = QuestsStartedByStaticObject(staticEntry);
            if (bound.Count == 0 && handIns.Count == 0)
                return Error(WireCodes.ProtocolError.InvalidQuest, "Entity is not a quest giver.");
            offers = bound.Where(quest => QuestClassGate.Allows(quest, _character?.Class)
                && !_quests.Accepted.Contains(quest.Id) && !_quests.Completed.Contains(quest.Id)
                && QuestPrerequisiteGate.Allows(quest, _quests.Completed)
                && QuestLevelGate.Allows(quest, _level)
                && QuestSpellGate.Allows(quest, _spells.Known)).ToArray();
        }
        else
        {
            return Error(WireCodes.ProtocolError.InvalidQuest, "Quest giver is not visible.");
        }

        IReadOnlyList<QuestDefinition> discussed = handIns.Concat(offers).ToArray();

        if (askedQuestId != 0)
        {
            var asked = discussed.FirstOrDefault(quest => quest.Id == askedQuestId);
            return asked is null
                ? Error(WireCodes.ProtocolError.InvalidQuest, "That quest is not on offer here.")
                : QuestDetailsReply(networkId, asked, IsHandIn(handIns, asked));
        }

        if (discussed.Count == 1)
        {
            return QuestDetailsReply(networkId, discussed[0], IsHandIn(handIns, discussed[0]));
        }

        return Reply(WebMessageTypes.QuestListUpdated, new QuestListPayload(
            WebNetworkId.FromString(networkId),
            discussed.Select(quest =>
        {
            var payout = Rewards(quest.Id);
            var handingIn = IsHandIn(handIns, quest);
            return new QuestListItem(quest.Id, quest.TitleStringId, quest.DetailsStringId,
                handingIn ? QuestStates.ReadyToHandIn : QuestStates.Offered, quest.MinLevel, Name: "",
                payout?.Fixed, payout?.Choices, payout?.Money, ConversationObjectives(quest, handingIn));
        }).ToArray()));
    }

    private static bool IsHandIn(IReadOnlyList<QuestDefinition> handIns, QuestDefinition quest) =>
        handIns.Any(candidate => candidate.Id == quest.Id);

    private string QuestDetailsReply(string networkId, QuestDefinition quest, bool handingIn = false)
    {
        var payout = Rewards(quest.Id);
        return Reply(WebMessageTypes.QuestDetails, new QuestDetailsPayload(
            WebNetworkId.FromString(networkId), quest.Id, quest.TitleStringId,
            quest.DetailsStringId, handingIn ? QuestStates.ReadyToHandIn : QuestStates.Offered, quest.MinLevel,
            payout?.Fixed, payout?.Choices, payout?.Money, ConversationObjectives(quest, handingIn)));
    }

    private IReadOnlyList<QuestObjectivePayload>? ConversationObjectives(QuestDefinition quest, bool handingIn)
    {
        if (!handingIn) return OfferObjectives(quest);
        var held = _quests.Log(_content.Quests).FirstOrDefault(entry => entry.QuestId == quest.Id);
        return held is null || held.Objectives.Count == 0
            ? null
            : held.Objectives.Select(ObjectivePayloadFor).ToArray();
    }

    private IReadOnlyList<QuestDefinition> QuestsStartedByStaticObject(uint staticObjectEntry)
    {
        var ids = _content.QuestStartersByStaticObject
            .Where(b => b.StaticObjectEntry == staticObjectEntry)
            .Select(b => b.QuestId)
            .ToHashSet();
        return ids.Count == 0 ? [] : _content.Quests.Where(q => ids.Contains(q.Id)).ToArray();
    }

    private (IReadOnlyList<QuestRewardItemPayload> Fixed,
        IReadOnlyList<QuestRewardItemPayload> Choices, int Money)? Rewards(uint questId)
    {
        if (!_content.QuestRewards.TryGetValue(questId, out var reward)) return null;
        return (Map(reward.Fixed), Map(reward.Choices), reward.Money);

        static IReadOnlyList<QuestRewardItemPayload> Map(IReadOnlyList<QuestRewardItem> items) =>
            items.Select(item => new QuestRewardItemPayload(
                item.Entry, item.NameTextId, item.Quality, item.Count)).ToArray();
    }

    private QuestUpdatePayload QuestUpdatePayloadFor(QuestUpdateEvent update)
    {
        var (guideZones, handInZone) = QuestGuideFor(update.QuestId, update.Objectives, update.ReadyToHandIn);
        return new(
            update.QuestId, update.TitleStringId, update.DetailsStringId, update.Status,
            update.Slot, update.ReadyToHandIn,
            update.Objectives.Select(ObjectivePayloadFor).ToArray(),
            update.Counters.Count > 0 ? update.Counters.Select(CounterPayloadFor).ToArray() : null,
            guideZones, handInZone);
    }

    private static QuestObjectivePayload ObjectivePayloadFor(QuestObjectiveProgress objective) => new(
        objective.Index,
        Killed: objective.Kind == QuestObjectiveKind.Killed ? objective.Count : 0,
        Triggered: objective.Kind == QuestObjectiveKind.Triggered ? objective.Count : 0,
        Held: objective.Kind == QuestObjectiveKind.Held ? objective.Count : 0,
        objective.ObjectiveItem,
        ObjectiveItemCount: objective.Kind == QuestObjectiveKind.Held ? objective.GoalCount : 0,
        objective.GoalCount,
        objective.ObjectiveStringId);

    private static QuestCounterPayload CounterPayloadFor(QuestCounterDelta counter) =>
        new(counter.Index, counter.Kind, counter.Count);

    private static IReadOnlyList<QuestObjectivePayload>? OfferObjectives(QuestDefinition quest) =>
        quest.Objectives.Count == 0 ? null : quest.Objectives.Select(objective => new QuestObjectivePayload(
            objective.Index, Killed: 0, Triggered: 0, Held: 0,
            objective.ObjectiveItem,
            ObjectiveItemCount: objective.Kind == QuestObjectiveKind.Held ? objective.GoalCount : 0,
            objective.GoalCount, objective.ObjectiveStringId)).ToArray();

    private int HeldCountFor(uint entry) =>
        _belongings.Items.Items.Where(item => item.Entry == (int)entry).Sum(item => item.Count);

    private IEnumerable<string> HeldObjectiveUpdates() =>
        _quests.RefreshHeldObjectives(HeldCountFor, _content.Quests)
            .Select(update => Reply(WebMessageTypes.QuestUpdated, QuestUpdatePayloadFor(update)));

    private QuestLogEntryPayload QuestLogEntryPayloadFor(QuestLogEntry entry)
    {
        var payout = Rewards(entry.QuestId);
        var (guideZones, handInZone) = QuestGuideFor(entry.QuestId, entry.Objectives, entry.ReadyToHandIn);
        return new QuestLogEntryPayload(
            entry.QuestId, entry.TitleStringId, entry.DetailsStringId, entry.Status,
            entry.Slot, entry.ReadyToHandIn, entry.Objectives.Select(ObjectivePayloadFor).ToArray(),
            entry.MinLevel, payout?.Fixed, payout?.Choices, payout?.Money, guideZones, handInZone);
    }

    private const ushort NoGuideZoneMapId = ushort.MaxValue;

    private (IReadOnlyList<QuestZonePayload>? GuideZones, QuestZonePayload? HandInZone) QuestGuideFor(
        uint questId, IReadOnlyList<QuestObjectiveProgress> objectives, bool readyToHandIn)
    {
        if (_world is null) return (null, null);
        var mapId = _world.MapId;
        var position = _world.PlayerPosition;

        if (readyToHandIn) return (null, NearestHandInZone(questId, mapId, position));

        var current = objectives
            .Where(objective => objective.Count < objective.GoalCount)
            .OrderBy(objective => objective.Index)
            .FirstOrDefault();
        if (current is null) return (null, null);

        var quest = _content.Quests.FirstOrDefault(candidate => candidate.Id == questId);
        var definition = quest?.Objectives.FirstOrDefault(o => o.Index == current.Index);
        if (definition is null) return (null, null);

        var target = definition.Kind switch
        {
            QuestObjectiveKind.Killed => NearestCreatureZone(definition.ObjectiveCreature, mapId, position),
            QuestObjectiveKind.Triggered => NearestStaticObjectZone(definition.ObjectiveObject, mapId, position),
            _ => null,
        };
        if (target is null) return (null, null);

        var zones = new QuestZonePayload[current.Index + 1];
        for (var i = 0; i < current.Index; i++) zones[i] = new QuestZonePayload(0, NoGuideZoneMapId, 0, 0, 0, 0);
        zones[current.Index] = target;
        return (zones, null);
    }

    private const float SyntheticGuideZoneRadius = 5f;

    private QuestZonePayload? NearestCreatureZone(uint creatureEntry, ushort mapId, DomainPosition from)
    {
        CreatureSpawnDefinition? nearest = null;
        var nearestDistance = float.MaxValue;
        foreach (var spawn in _creatureSpawns)
        {
            if (spawn.MapId != mapId || spawn.CreatureEntryId != creatureEntry) continue;
            var distance = FlatDistanceSquared(spawn.X, spawn.Z, from);
            if (nearest is not null && distance >= nearestDistance) continue;
            nearest = spawn;
            nearestDistance = distance;
        }

        return nearest is null ? null
            : new QuestZonePayload(nearest.Id, nearest.MapId, nearest.X, nearest.Y, nearest.Z, SyntheticGuideZoneRadius);
    }

    private QuestZonePayload? NearestStaticObjectZone(uint staticObjectEntry, ushort mapId, DomainPosition from)
    {
        StaticObjectSpawnDefinition? nearest = null;
        var nearestDistance = float.MaxValue;
        foreach (var spawn in _staticObjectSpawns)
        {
            if (spawn.MapId != mapId || spawn.EntryId != staticObjectEntry) continue;
            var distance = FlatDistanceSquared(spawn.X, spawn.Z, from);
            if (nearest is not null && distance >= nearestDistance) continue;
            nearest = spawn;
            nearestDistance = distance;
        }

        return nearest is null ? null
            : new QuestZonePayload(nearest.Id, nearest.MapId, nearest.X, nearest.Y, nearest.Z, SyntheticGuideZoneRadius);
    }

    private QuestZonePayload? NearestHandInZone(uint questId, ushort mapId, DomainPosition from)
    {
        QuestZonePayload? nearest = null;
        var nearestDistance = float.MaxValue;

        foreach (var binding in _content.QuestFinishersByCreature)
        {
            if (binding.QuestId != questId) continue;
            var zone = NearestCreatureZone(binding.CreatureEntry, mapId, from);
            if (zone is null) continue;
            var distance = FlatDistanceSquared(zone.X, zone.Z, from);
            if (nearest is not null && distance >= nearestDistance) continue;
            nearest = zone;
            nearestDistance = distance;
        }

        foreach (var binding in _content.QuestFinishersByStaticObject)
        {
            if (binding.QuestId != questId) continue;
            var zone = NearestStaticObjectZone(binding.StaticObjectEntry, mapId, from);
            if (zone is null) continue;
            var distance = FlatDistanceSquared(zone.X, zone.Z, from);
            if (nearest is not null && distance >= nearestDistance) continue;
            nearest = zone;
            nearestDistance = distance;
        }

        return nearest;
    }

    private static float FlatDistanceSquared(float x, float z, DomainPosition from)
    {
        var dx = x - from.X;
        var dz = z - from.Z;
        return dx * dx + dz * dz;
    }

    private string QuestAccept(JsonElement payload)
    {
        var networkId = PayloadIds.NetworkId(payload);
        if (!payload.TryGetProperty("questId", out var questIdValue) || !questIdValue.TryGetUInt32(out var questId))
            return Error(WireCodes.ProtocolError.InvalidQuest, "Quest ID is required.");
        if (networkId is null || !IsValidQuestGiver(networkId, questId))
            return Error(WireCodes.ProtocolError.InvalidQuest, "Quest giver is not visible.");
        var result = _quests.Accept(questId, _content.Quests, HeldCountFor, _level, _character?.Class, _spells.Known);
        return result is null ? Error(WireCodes.ProtocolError.InvalidQuest, "Quest is unavailable, already accepted, your level is below its own, or your class cannot take it.")
            : Reply(WebMessageTypes.QuestUpdated, QuestUpdatePayloadFor(result));
    }

    private bool IsValidQuestGiver(string networkId, uint questId) =>
        _sight.Contains(networkId)
        || (TryParseItemGiverId(networkId, out var entry)
            && _content.Items.TryGetValue(entry, out var proto) && proto.QuestId == questId)
        || (_visibleStaticObjectEntries.TryGetValue(networkId, out var staticEntry)
            && _content.QuestStartersByStaticObject.Any(b => b.StaticObjectEntry == staticEntry && b.QuestId == questId));

    private static bool TryParseItemGiverId(string networkId, out uint entry)
    {
        entry = 0;
        return networkId.StartsWith("item:", StringComparison.Ordinal)
            && uint.TryParse(networkId.AsSpan(5), out entry);
    }

    private bool IsValidQuestFinisher(string networkId, uint questId)
    {
        if (_sight.Entries.TryGetValue(networkId, out var npcEntry))
        {
            return !QuestFinisherGate.HasBinding(
                    questId, _content.QuestFinishersByCreature, _content.QuestFinishersByStaticObject)
                || QuestFinisherGate.BindsCreature(questId, npcEntry, _content.QuestFinishersByCreature);
        }

        return _visibleStaticObjectEntries.TryGetValue(networkId, out var staticEntry)
            && QuestFinisherGate.BindsStaticObject(questId, staticEntry, _content.QuestFinishersByStaticObject);
    }

    private IReadOnlyList<QuestDefinition> QuestsFinishableAt(string networkId)
    {
        var finishable = _quests.Log(_content.Quests)
            .Where(entry => entry.ReadyToHandIn)
            .Select(entry => _content.Quests.FirstOrDefault(quest => quest.Id == entry.QuestId))
            .Where(quest => quest is not null)
            .Select(quest => quest!);

        if (_sight.Entries.TryGetValue(networkId, out var npcEntry))
        {
            return finishable.Where(quest => QuestFinisherGate.MarksCreature(
                quest, npcEntry, _content.QuestFinishersByCreature, _content.QuestFinishersByStaticObject)).ToArray();
        }

        return _visibleStaticObjectEntries.TryGetValue(networkId, out var staticEntry)
            ? finishable.Where(quest => QuestFinisherGate.BindsStaticObject(
                quest.Id, staticEntry, _content.QuestFinishersByStaticObject)).ToArray()
            : [];
    }

    private int RewardSlotsNeeded(uint questId, int rewardIndex)
    {
        if (!_content.QuestRewards.TryGetValue(questId, out var reward)) return 0;
        var choices = reward.Choices.Count > 0 && rewardIndex >= 0 && rewardIndex < reward.Choices.Count ? 1 : 0;
        return reward.Fixed.Count + choices;
    }

    private IReadOnlyList<string> QuestComplete(JsonElement payload)
    {
        var networkId = PayloadIds.NetworkId(payload);
        if (!payload.TryGetProperty("questId", out var questIdValue) || !questIdValue.TryGetUInt32(out var questId))
            return [Error(WireCodes.ProtocolError.InvalidQuest, "Quest ID is required.")];
        if (networkId is null || !IsValidQuestFinisher(networkId, questId))
            return [Error(WireCodes.ProtocolError.InvalidQuest, "Quest giver is not visible.")];
        var rewardIndex = payload.TryGetProperty("rewardIndex", out var rewardValue) ? rewardValue.GetInt32() : 0;
        if (RewardSlotsNeeded(questId, rewardIndex) > _belongings.Items.FreeSlots(_belongings.BagSlots))
            return [ItemRefused(ItemErrors.InventoryFull)];
        var result = _quests.Complete(questId, rewardIndex, _content.Quests, _content.QuestRewards,
            _belongings.Items.FreeSlots(_belongings.BagSlots), _belongings.Currency, _level);
        if (result is null)
            return [Error(WireCodes.ProtocolError.InvalidQuest,
                "Quest is not accepted, the reward choice is invalid, or the toll cannot be paid.")];

        var responses = new List<string>
        {
            Reply(WebMessageTypes.QuestUpdated, QuestUpdatePayloadFor(result.Update)),
            Reply(WebMessageTypes.QuestRemoved, new QuestRemovedPayload(questId)),
        };

        foreach (var item in result.Items)
        {
            var added = _belongings.Items.Add(checked((int)item.Entry), item.Count, _belongings.BagSlots)!;
            responses.Add(Reply(WebMessageTypes.InventoryItemAdded, _belongings.PayloadFor(added)));
        }

        if (result.Money != 0)
        {
            _belongings.Currency += result.Money;
            responses.Add(Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
                new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = _belongings.Currency })));
        }

        foreach (var objective in result.Update.Objectives.Where(o => o.Kind == QuestObjectiveKind.Held))
        {
            var consumed = _belongings.Items.Consume((int)objective.ObjectiveItem, objective.GoalCount);
            foreach (var itemId in consumed.RemovedItemIds)
                responses.Add(Reply(WebMessageTypes.InventoryItemRemoved, new InventoryItemRemovedPayload(itemId)));
            foreach (var item in consumed.ReducedItems)
                responses.Add(Reply(WebMessageTypes.InventoryStackChanged, new InventoryStackChangedPayload(
                    item.Id, new StorageSlotPayload("bag", item.Slot), item.Count)));
        }

        responses.AddRange(HeldObjectiveUpdates());

        responses.AddRange(GrantXp(result.Xp, Now));

        return responses;
    }

    private string QuestDestroy(JsonElement payload)
    {
        if (_character is null) return Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before quest operations.");
        if (!payload.TryGetProperty("slot", out var slotValue) || slotValue.ValueKind != JsonValueKind.Number)
            return Error(WireCodes.ProtocolError.InvalidQuest, "Quest slot is required.");
        if (!payload.TryGetProperty("questId", out var questIdValue) || !questIdValue.TryGetUInt32(out var questId))
            return Error(WireCodes.ProtocolError.InvalidQuest, "Quest ID is required.");
        return _quests.Destroy(slotValue.GetInt32(), questId)
            ? Reply(WebMessageTypes.QuestRemoved, new QuestRemovedPayload(questId))
            : Error(WireCodes.ProtocolError.InvalidQuest, "Quest slot and ID do not match an accepted quest.");
    }

    private IEnumerable<string> QuestGuideRefreshReplies() =>
        _quests.Log(_content.Quests).Select(entry => Reply(WebMessageTypes.QuestUpdated,
            QuestUpdatePayloadFor(new QuestUpdateEvent(
                entry.QuestId, entry.TitleStringId, entry.DetailsStringId, entry.Status,
                entry.Slot, entry.ReadyToHandIn, entry.Objectives, Counters: []))));
}
