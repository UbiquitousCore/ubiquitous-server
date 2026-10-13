using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private string InventoryMove(JsonElement payload)
    {
        if (_world is null) return Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before inventory operations.");
        var request = WebMessageJson.DeserializePayload<InventoryMovePayload>(payload);
        var result = _belongings.Items.Move(
            new StorageSlotState(request.From.Area, request.From.Index),
            new StorageSlotState(request.To.Area, request.To.Index),
            _belongings.BagSlots, _belongings.BankSlots);
        if (!result.Accepted)
            return Error(WireCodes.ProtocolError.InvalidInventory, result.Reason ?? "Inventory move rejected.");
        if (result.Displaced is not null)
            return Reply(WebMessageTypes.InventoryItemsSwapped, new InventoryItemsSwappedPayload(
                new InventorySwapSidePayload(
                    new StorageSlotPayload(result.From!.Area, result.From.Index), result.Item!.Id),
                new InventorySwapSidePayload(
                    new StorageSlotPayload(result.To!.Area, result.To.Index), result.Displaced.Id)));
        return Reply(WebMessageTypes.InventoryItemMoved, new InventoryItemMovedPayload(
            result.Item!.Id,
            new StorageSlotPayload(result.From!.Area, result.From.Index),
            new StorageSlotPayload(result.To!.Area, result.To.Index)));
    }

    private IReadOnlyList<string> InventoryEquip(JsonElement payload)
    {
        if (_world is null || _character is null)
            return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before inventory operations.")];
        var request = WebMessageJson.DeserializePayload<InventoryEquipPayload>(payload);
        var from = new StorageSlotState(request.From.Area, request.From.Index);

        if (!request.Equip)
        {
            var takenOff = _belongings.Items.Unequip(request.ItemId, from,
                request.To is null ? null : new StorageSlotState(request.To.Area, request.To.Index),
                _belongings.BagSlots);
            return takenOff.Accepted ? EquipmentChanged(takenOff, from) : [EquipRefused(takenOff.Code)];
        }

        var item = _belongings.Items.Items.FirstOrDefault(candidate => candidate.Id == request.ItemId);
        if (item is null) return [EquipRefused(ItemErrors.ItemNotInSlot)];
        if (!MeetsItemEquipRequirement(item.Entry, out var refusal)) return [EquipRefused(refusal)];

        var result = _belongings.Items.Equip(
            request.ItemId, from, request.To?.Index, InventoryTypeOf, _belongings.BagSlots);
        return result.Accepted ? EquipmentChanged(result, from) : [EquipRefused(result.Code)];
    }

    private uint InventoryTypeOf(int entry) =>
        _content.Items.TryGetValue(checked((uint)entry), out var proto) ? proto.InventoryType : 0;

    private bool MeetsItemEquipRequirement(int entry, out string code)
    {
        code = ItemErrors.Ok;
        if (!_content.Items.TryGetValue(checked((uint)entry), out var proto))
        {
            code = ItemErrors.UnknownItem;
            return false;
        }

        if (!MeetsItemRequirements(proto))
        {
            code = ItemErrors.CantEquip;
            return false;
        }

        return true;
    }

    private bool MeetsItemRequirements(ItemDefinition proto) =>
        _level >= proto.RequiredLevel
        && ItemRaceMask.Allows(proto.AllowableRaceMask, _character!.Race)
        && ItemClassMask.Allows(proto.AllowableClassMask, _character.Class);

    private enum UseEffect { OffersQuest, OpensGuildFounding, TeachesSpell, CastsSpell, Nothing }

    private UseEffect UseOf(ItemDefinition proto) => proto switch
    {
        { QuestId: not null } => UseEffect.OffersQuest,
        _ when GuildStone.Matches(proto) => UseEffect.OpensGuildFounding,
        { OnUseSpellId: uint lessonId } when _content.Spells.TryGetValue(lessonId, out var lesson)
            && SpellLessons.Taught(lesson).Count > 0 => UseEffect.TeachesSpell,
        { OnUseSpellId: uint spellId } when _content.Spells.TryGetValue(spellId, out var spell)
            && SpellService.HasResolvableEffect(spell) => UseEffect.CastsSpell,
        _ => UseEffect.Nothing,
    };

    private string EquipRefused(string code) =>
        Reply(WebMessageTypes.InventoryEquipResult, new InventoryEquipResultPayload(code));

    private string ItemRefused(string code) =>
        Reply(WebMessageTypes.ItemRefused, new ItemRefusedPayload(code));

    private void RefreshWornAttributes()
    {
        if (_character is null) return;

        var totals = new Dictionary<int, int>();
        foreach (int entry in _character.Equipment)
        {
            if (entry <= 0 || !_content.Items.TryGetValue((uint)entry, out var item)) continue;
            if (item.Armor is uint armour) Add(AttributeIds.Armor, (int)armour);
            foreach (var stat in item.Stats ?? []) Add(stat.Attribute, stat.Amount);
        }

        _attributeModifiers.SetWorn(PlayerUnitId, totals);

        var mainhand = _character.Equipment.Count > EquipmentSlots.MainHand
            ? _character.Equipment[EquipmentSlots.MainHand]
            : 0;
        if (mainhand > 0 && _content.Items.TryGetValue((uint)mainhand, out var weapon))
            _combat.SetPlayerWeapon((int?)weapon.MinDamage, (int?)weapon.MaxDamage);
        else
            _combat.SetPlayerWeapon(null, null);

        var offhand = _character.Equipment.Count > EquipmentSlots.OffHand
            ? _character.Equipment[EquipmentSlots.OffHand]
            : 0;
        if (offhand > 0 && _content.Items.TryGetValue((uint)offhand, out var second)
            && second.Class == ItemClasses.Weapons)
            _combat.SetPlayerOffHandWeapon((int?)second.MinDamage, (int?)second.MaxDamage);
        else
            _combat.SetPlayerOffHandWeapon(null, null);

        void Add(int attribute, int amount) =>
            totals[attribute] = totals.GetValueOrDefault(attribute) + amount;
    }

    private IReadOnlyList<string> EquipmentChanged(InventoryEquipResult result, StorageSlotState from)
    {
        _character = _character! with { Equipment = result.Equipment };
        RefreshWornAttributes();

        var moved = result.Item!;
        var responses = new List<string>
        {
            Reply(WebMessageTypes.InventoryItemsSwapped, new InventoryItemsSwappedPayload(
                new InventorySwapSidePayload(new StorageSlotPayload(from.Area, from.Index), moved.Id),
                new InventorySwapSidePayload(
                    new StorageSlotPayload(moved.Area, moved.Slot), result.Displaced?.Id))),
        };

        foreach (var relocation in result.AlsoRemoved)
        {
            responses.Add(Reply(WebMessageTypes.InventoryItemsSwapped, new InventoryItemsSwappedPayload(
                new InventorySwapSidePayload(
                    new StorageSlotPayload(relocation.From.Area, relocation.From.Index), relocation.Item.Id),
                new InventorySwapSidePayload(
                    new StorageSlotPayload(relocation.Item.Area, relocation.Item.Slot)))));
        }

        responses.Add(Reply(WebMessageTypes.EntityAppearanceChanged, new EntityAppearanceChangedPayload(
            WebNetworkId.FromString(PlayerUnitId), result.Equipment)));
        return responses;
    }

    private IReadOnlyList<string> InventoryUse(JsonElement payload, DateTimeOffset now)
    {
        if (_world is null || _character is null)
            return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before inventory operations.")];
        var request = WebMessageJson.DeserializePayload<InventoryUsePayload>(payload);
        var item = _belongings.Items.Items.FirstOrDefault(candidate =>
            candidate.Id == request.ItemId
            && string.Equals(candidate.Area, request.From.Area, StringComparison.Ordinal)
            && candidate.Slot == request.From.Index);
        if (item is null)
            return [Error(WireCodes.ProtocolError.InvalidInventory, "Item or source slot is stale.")];

        if (!_content.Items.TryGetValue(checked((uint)item.Entry), out var proto))
            return [Error(WireCodes.ProtocolError.CannotUse, "This item has no use.")];

        if (!MeetsItemRequirements(proto))
            return [Error(WireCodes.ProtocolError.CannotUse, "You cannot use this item.")];

        switch (UseOf(proto))
        {
            case UseEffect.OffersQuest:
                return InventoryUseQuestStarter(item, proto.QuestId!.Value);
            case UseEffect.OpensGuildFounding:
                return [Reply(WebMessageTypes.GuildFoundPrompt, new GuildFoundPromptPayload(item.Entry))];
            case UseEffect.TeachesSpell:
                return InventoryUseLesson(item, proto, _content.Spells[proto.OnUseSpellId!.Value]);
            case UseEffect.Nothing:
                return [Error(WireCodes.ProtocolError.CannotUse, "This item has no use.")];
        }

        var spellId = proto.OnUseSpellId!.Value;
        var spell = _content.Spells[spellId];

        if (_itemCooldownUntil.TryGetValue(item.Entry, out var readyAt) && now < readyAt)
            return [Error(WireCodes.ProtocolError.CannotUse, "This item is not ready yet.")];

        var casterId = WebNetworkId.FromString(PlayerUnitId);
        bool targetsSelf = spell.EffectHeal is not null || spell.AuraPeriodicHeal is not null
            || spell.EffectTarget == SpellEffectTargets.Self;
        string? effectTargetId = targetsSelf ? PlayerUnitId : request.TargetId;
        if (!targetsSelf && (string.IsNullOrEmpty(effectTargetId)
            || !_sight.Contains(effectTargetId) || _combat.IsDead(effectTargetId)))
            return [Error(WireCodes.ProtocolError.CannotUse, "A valid target is required.")];

        var responses = new List<string>(ResolveSpellEffect(casterId, effectTargetId!, spellId, spell, now));

        if (proto.OnUseCooldownSeconds is uint cooldownSeconds)
        {
            _itemCooldownUntil[item.Entry] = now + TimeSpan.FromSeconds(cooldownSeconds);
        }

        if (proto.InventoryType == 0) responses.AddRange(ConsumeOne(item));

        return responses;
    }

    private List<string> ConsumeOne(InventoryItemState item)
    {
        var consumed = _belongings.Items.ConsumeOneOf(item.Id);
        return
        [
            .. consumed.RemovedItemIds.Select(itemId =>
                Reply(WebMessageTypes.InventoryItemRemoved, new InventoryItemRemovedPayload(itemId))),
            .. consumed.ReducedItems.Select(reduced =>
                Reply(WebMessageTypes.InventoryStackChanged, new InventoryStackChangedPayload(
                    reduced.Id, new StorageSlotPayload("bag", reduced.Slot), reduced.Count))),
        ];
    }

    private IReadOnlyList<string> InventoryUseQuestStarter(InventoryItemState item, uint questId)
    {
        if (_quests.Accepted.Contains(questId))
            return [Error(WireCodes.ProtocolError.CannotUse, "This quest is already in your log.")];
        if (_quests.Completed.Contains(questId))
            return [Error(WireCodes.ProtocolError.CannotUse, "This quest has already been completed.")];

        var quest = _content.Quests.FirstOrDefault(q => q.Id == questId);
        if (quest is null || !QuestClassGate.Allows(quest, _character?.Class)
            || !QuestPrerequisiteGate.Allows(quest, _quests.Completed)
            || !QuestLevelGate.Allows(quest, _level)
            || !QuestSpellGate.Allows(quest, _spells.Known))
            return [Error(WireCodes.ProtocolError.CannotUse, "This item's quest is not available.")];

        var payout = Rewards(quest.Id);
        return
        [
            Reply(WebMessageTypes.QuestDetails, new QuestDetailsPayload(
                WebNetworkId.FromString($"item:{item.Entry}"), quest.Id, quest.TitleStringId,
                quest.DetailsStringId, QuestStates.Offered, quest.MinLevel,
                payout?.Fixed, payout?.Choices, payout?.Money, OfferObjectives(quest))),
        ];
    }

    private IReadOnlyList<string> InventoryDestroy(JsonElement payload)
    {
        if (_world is null) return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before inventory operations.")];
        var request = WebMessageJson.DeserializePayload<InventoryDestroyPayload>(payload);
        var result = _belongings.Items.Destroy(request.ItemId,
            new StorageSlotState(request.From.Area, request.From.Index), request.Count);
        if (!result.Accepted) return [Error(WireCodes.ProtocolError.InvalidInventory, result.Reason ?? "Inventory destroy rejected.")];
        return
        [
            Reply(WebMessageTypes.InventoryItemRemoved, new InventoryItemRemovedPayload(result.ItemId!)),
            .. HeldObjectiveUpdates(),
        ];
    }

    private IReadOnlyList<string> InventorySplit(JsonElement payload)
    {
        if (_world is null) return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before inventory operations.")];
        var request = WebMessageJson.DeserializePayload<InventorySplitPayload>(payload);
        var result = _belongings.Items.Split(request.ItemId,
            new StorageSlotState(request.From.Area, request.From.Index), request.Count,
            new StorageSlotState(request.To.Area, request.To.Index));
        if (!result.Accepted) return [Error(WireCodes.ProtocolError.InvalidInventory, result.Reason ?? "Inventory split rejected.")];
        return
        [
            Reply(WebMessageTypes.InventoryItemAdded, _belongings.PayloadFor(result.Added!)),
            Reply(WebMessageTypes.InventoryStackChanged, new InventoryStackChangedPayload(
                result.Source!.Id, new StorageSlotPayload("bag", result.Source.Slot), result.Source.Count)),
        ];
    }

    private IReadOnlyList<string> InventorySort(JsonElement payload)
    {
        if (_world is null) return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before inventory operations.")];
        var request = WebMessageJson.DeserializePayload<InventorySortPayload>(payload);
        var result = _belongings.Items.Sort(request.Area);
        var responses = new List<string> { Reply(WebMessageTypes.InventorySorted, new InventorySortedPayload(result.Accepted, request.Area)) };
        if (result.Accepted)
        {
            responses.Add(Reply(WebMessageTypes.InventoryUpdated, InventorySnapshot()));
        }

        return responses;
    }

    private IReadOnlyList<string> InventoryOpenBank(JsonElement payload)
    {
        if (_world is null) return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before inventory operations.")];
        var networkId = PayloadIds.NetworkId(payload);
        bool isReachableBank = networkId is not null
            && _visibleStaticObjectEntries.TryGetValue(networkId, out var entry)
            && _content.BankBoxEntries.Contains(entry)
            && InRangeOf(networkId, InteractionRange.Max, _staticObjectPositions);
        if (!isReachableBank) return [Reply(WebMessageTypes.InventoryBankOpen, new InventoryBankOpenPayload(false))];

        return
        [
            Reply(WebMessageTypes.InventoryBankOpen, new InventoryBankOpenPayload(true)),
            Reply(WebMessageTypes.InventoryBankUpdated, new InventoryBankUpdatedPayload(
                _belongings.BankSlots, _belongings.Items.Items
                    .Where(item => string.Equals(item.Area, "bank", StringComparison.Ordinal))
                    .Select(_belongings.PayloadFor)
                    .ToArray())),
        ];
    }

    private async Task<IReadOnlyList<string>> InventoryUpgradeStorageAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        if (_world is null) return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before inventory operations.")];
        var request = WebMessageJson.DeserializePayload<InventoryExpandPayload>(payload);
        bool isBag = string.Equals(request.Area, "bag", StringComparison.Ordinal);
        int? currentSlots = isBag ? _belongings.BagSlots
            : string.Equals(request.Area, "bank", StringComparison.Ordinal) ? _belongings.BankSlots
            : null;

        var (verdict, rung) = StorageUpgradeRules.Decide(
            _content.StorageUpgrades, request.Area, currentSlots, request.Slots, _belongings.Currency);
        switch (verdict)
        {
            case StorageUpgradeVerdict.NoSuchRung:
                return [Error(WireCodes.ProtocolError.UpgradeRefused, "That upgrade is not available.")];
            case StorageUpgradeVerdict.CannotAfford:
                return [Error(WireCodes.ProtocolError.UpgradeRefused, "You cannot afford this upgrade.")];
        }

        _belongings.Currency -= checked((int)rung!.PriceAmount);
        if (isBag) _belongings.BagSlots = checked((int)rung.SlotCount);
        else _belongings.BankSlots = checked((int)rung.SlotCount);

        await SaveCharacterAsync(cancellationToken);

        return
        [
            Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
                new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = _belongings.Currency })),
            Reply(WebMessageTypes.InventoryCapacityChanged,
                new InventoryCapacityChangedPayload(request.Area, checked((int)rung.SlotCount))),
        ];
    }

    private InventoryUpdatedPayload InventorySnapshot() =>
        new(_belongings.BagSlots, _belongings.BankSlots, _belongings.Items.Items.Select(_belongings.PayloadFor).ToArray());
}
