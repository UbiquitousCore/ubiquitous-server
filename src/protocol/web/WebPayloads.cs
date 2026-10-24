using System.Text.Json;
using System.Text.Json.Serialization;
using Ubiquitous.Data;

namespace Ubiquitous.Protocol.Web;

public sealed record LoginRequest(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("passHash")] string PassHash);

public sealed record ChatPayload(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("text")] string Text);

public sealed record ChatReceivedPayload(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("sender")] string Sender,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("senderId")] WebNetworkId? SenderId = null);

public sealed record GossipPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("entry")] uint Entry);

public sealed record NpcChoiceResultPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("menuId")] uint MenuId,
    [property: JsonPropertyName("choice")] int Choice,
    [property: JsonPropertyName("result")] string Result);

public sealed record TargetChangedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId);

public sealed record QuestStatusRow(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("status")] string Status);

public sealed record QuestStatusesPayload(
    [property: JsonPropertyName("statuses")] IReadOnlyList<QuestStatusRow> Statuses);

public sealed record QuestRewardItemPayload(
    [property: JsonPropertyName("entry")] uint Entry,
    [property: JsonPropertyName("nameTextId")] uint NameTextId,
    [property: JsonPropertyName("quality")] int Quality,
    [property: JsonPropertyName("count")] int Count);

public sealed record QuestListItem(
    [property: JsonPropertyName("id")] uint Id,
    [property: JsonPropertyName("titleStringId")] uint TitleStringId,
    [property: JsonPropertyName("detailsStringId")] uint DetailsStringId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("minLevel")] int MinLevel,
    [property: JsonPropertyName("name")] string Name = "",
    [property: JsonPropertyName("rewards")] IReadOnlyList<QuestRewardItemPayload>? Rewards = null,
    [property: JsonPropertyName("rewardChoices")] IReadOnlyList<QuestRewardItemPayload>? RewardChoices = null,
    [property: JsonPropertyName("rewardMoney")] int? RewardMoney = null,
    [property: JsonPropertyName("objectives")] IReadOnlyList<QuestObjectivePayload>? Objectives = null);

public sealed record QuestDetailsPayload(
    [property: JsonPropertyName("giverId")] WebNetworkId GiverId,
    [property: JsonPropertyName("id")] uint Id,
    [property: JsonPropertyName("titleStringId")] uint TitleStringId,
    [property: JsonPropertyName("detailsStringId")] uint DetailsStringId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("minLevel")] int MinLevel,
    [property: JsonPropertyName("rewards")] IReadOnlyList<QuestRewardItemPayload>? Rewards = null,
    [property: JsonPropertyName("rewardChoices")] IReadOnlyList<QuestRewardItemPayload>? RewardChoices = null,
    [property: JsonPropertyName("rewardMoney")] int? RewardMoney = null,
    [property: JsonPropertyName("objectives")] IReadOnlyList<QuestObjectivePayload>? Objectives = null);

public sealed record QuestListPayload(
    [property: JsonPropertyName("giverId")] WebNetworkId GiverId,
    [property: JsonPropertyName("quests")] IReadOnlyList<QuestListItem> Quests);

public sealed record QuestObjectivePayload(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("killed")] int Killed,
    [property: JsonPropertyName("triggered")] int Triggered,
    [property: JsonPropertyName("held")] int Held,
    [property: JsonPropertyName("objectiveItem")] uint ObjectiveItem,
    [property: JsonPropertyName("objectiveItemCount")] int ObjectiveItemCount,
    [property: JsonPropertyName("goalCount")] int GoalCount,
    [property: JsonPropertyName("objectiveStringId")] uint ObjectiveStringId);

public sealed record QuestCounterPayload(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("kind")] QuestObjectiveKind Kind,
    [property: JsonPropertyName("count")] int Count);

public sealed record QuestZonePayload(
    [property: JsonPropertyName("id")] uint Id,
    [property: JsonPropertyName("mapId")] ushort MapId,
    [property: JsonPropertyName("x")] float X,
    [property: JsonPropertyName("y")] float Y,
    [property: JsonPropertyName("z")] float Z,
    [property: JsonPropertyName("radius")] float Radius);

public sealed record QuestUpdatePayload(
    [property: JsonPropertyName("id")] uint Id,
    [property: JsonPropertyName("titleStringId")] uint TitleStringId,
    [property: JsonPropertyName("detailsStringId")] uint DetailsStringId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("slot")] int Slot,
    [property: JsonPropertyName("readyToHandIn")] bool ReadyToHandIn,
    [property: JsonPropertyName("objectives")] IReadOnlyList<QuestObjectivePayload> Objectives,
    [property: JsonPropertyName("counters")] IReadOnlyList<QuestCounterPayload>? Counters = null,
    [property: JsonPropertyName("guideZones")] IReadOnlyList<QuestZonePayload>? GuideZones = null,
    [property: JsonPropertyName("handInZone")] QuestZonePayload? HandInZone = null);

public sealed record QuestRemovedPayload(
    [property: JsonPropertyName("questId")] uint QuestId);

public sealed record QuestLogEntryPayload(
    [property: JsonPropertyName("id")] uint Id,
    [property: JsonPropertyName("titleStringId")] uint TitleStringId,
    [property: JsonPropertyName("detailsStringId")] uint DetailsStringId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("slot")] int Slot,
    [property: JsonPropertyName("readyToHandIn")] bool ReadyToHandIn,
    [property: JsonPropertyName("objectives")] IReadOnlyList<QuestObjectivePayload> Objectives,
    [property: JsonPropertyName("minLevel")] int MinLevel,
    [property: JsonPropertyName("rewards")] IReadOnlyList<QuestRewardItemPayload>? Rewards = null,
    [property: JsonPropertyName("rewardChoices")] IReadOnlyList<QuestRewardItemPayload>? RewardChoices = null,
    [property: JsonPropertyName("rewardMoney")] int? RewardMoney = null,
    [property: JsonPropertyName("guideZones")] IReadOnlyList<QuestZonePayload>? GuideZones = null,
    [property: JsonPropertyName("handInZone")] QuestZonePayload? HandInZone = null);

public sealed record QuestLogPayload(
    [property: JsonPropertyName("quests")] IReadOnlyList<QuestLogEntryPayload> Quests);

public sealed record VendorItemPayload(
    [property: JsonPropertyName("slot")] int Slot,
    [property: JsonPropertyName("entry")] int Entry,
    [property: JsonPropertyName("nameTextId")] int NameTextId,
    [property: JsonPropertyName("displayInfoId")] int DisplayInfoId,
    [property: JsonPropertyName("buyPrice")] int BuyPrice,
    [property: JsonPropertyName("stock")] int? Stock,
    [property: JsonPropertyName("stackCount")] int StackCount);

public sealed record VendorTabPayload(
    [property: JsonPropertyName("tabIndex")] int TabIndex,
    [property: JsonPropertyName("currency")] int Currency,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("itemDataDecoded")] bool ItemDataDecoded,
    [property: JsonPropertyName("items")] IReadOnlyList<VendorItemPayload> Items);

public sealed record VendorItemsPayload(
    [property: JsonPropertyName("tabs")] IReadOnlyList<VendorTabPayload> Tabs);

public sealed record BuyResultPayload(
    [property: JsonPropertyName("code")] string Code);

public sealed record VendorSellPayload(
    [property: JsonPropertyName("from")] StorageSlotPayload From,
    [property: JsonPropertyName("itemId")] string ItemId);

public sealed record SellResultPayload(
    [property: JsonPropertyName("code")] string Code);

public sealed record PlayerValuesChangedPayload(
    [property: JsonPropertyName("changed")] Dictionary<string, int> Changed);

public sealed record PlayerExpGainedPayload(
    [property: JsonPropertyName("amount")] int Amount,
    [property: JsonPropertyName("levelUp")] bool LevelUp);

public sealed record AttackMeleeStartPayload(
    [property: JsonPropertyName("attackerId")] WebNetworkId AttackerId,
    [property: JsonPropertyName("targetId")] WebNetworkId TargetId);

public sealed record AttackRangeStartPayload(
    [property: JsonPropertyName("attackerId")] WebNetworkId AttackerId,
    [property: JsonPropertyName("targetId")] WebNetworkId TargetId);

public sealed record AttackShotFiredPayload(
    [property: JsonPropertyName("attackerId")] WebNetworkId AttackerId,
    [property: JsonPropertyName("targetId")] WebNetworkId TargetId,
    [property: JsonPropertyName("speed")] double Speed,
    [property: JsonPropertyName("spellVisualId")] int SpellVisualId);

public sealed record AttackHaltedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId);

public sealed record AttackEnterCombatPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId);

public sealed record AttackExitCombatPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId);

public sealed record AttackSwingResultPayload(
    [property: JsonPropertyName("attackerId")] WebNetworkId AttackerId,
    [property: JsonPropertyName("targetId")] WebNetworkId TargetId,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("damage")] int Damage,
    [property: JsonPropertyName("finalHealth")] int FinalHealth,
    [property: JsonPropertyName("hand")] string Hand = WireCodes.Hand.Main,
    [property: JsonPropertyName("absorbed")] int? Absorbed = null);

public sealed record AttackShotResultPayload(
    [property: JsonPropertyName("attackerId")] WebNetworkId AttackerId,
    [property: JsonPropertyName("targetId")] WebNetworkId TargetId,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("damage")] int Damage,
    [property: JsonPropertyName("finalHealth")] int FinalHealth,
    [property: JsonPropertyName("absorbed")] int? Absorbed = null);

public sealed record UnitDiedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("pos")] Position? Position = null);

public sealed record PlayerDeathPayload(
    [property: JsonPropertyName("killerId")] WebNetworkId? KillerId,
    [property: JsonPropertyName("deathCost")] int DeathCost);

public sealed record EntityValuesChangedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("changed")] Dictionary<string, int> Changed);

public sealed record EntityLootablePayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("state")] string State);

public sealed record LootRowPayload(
    [property: JsonPropertyName("slot")] int Slot,
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("entry")] int Entry,
    [property: JsonPropertyName("count")] int Count);

public sealed record LootContentsPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("money")] int Money,
    [property: JsonPropertyName("items")] IReadOnlyList<LootRowPayload> Items);

public sealed record LootBusyPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("busy")] bool Busy);

public sealed record LootSlotEmptiedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("looterId")] WebNetworkId LooterId,
    [property: JsonPropertyName("money")] bool Money,
    [property: JsonPropertyName("slot")] int? Slot = null);

public sealed record InventoryItemPayload(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("entry")] int Entry,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("slot")] StorageSlotPayload? Slot = null,
    [property: JsonPropertyName("details")] ItemPrototypePayload? Prototype = null);

public sealed record ItemPrototypePayload(
    [property: JsonPropertyName("entry")] int Entry,
    [property: JsonPropertyName("itemClass")] int ItemClass,
    [property: JsonPropertyName("subClass")] int SubClass,
    [property: JsonPropertyName("displayInfoId")] int DisplayInfoId,
    [property: JsonPropertyName("inventoryType")] int InventoryType,
    [property: JsonPropertyName("requiredLevel")] int RequiredLevel,
    [property: JsonPropertyName("buyPrice")] int BuyPrice,
    [property: JsonPropertyName("sellPrice")] int SellPrice,
    [property: JsonPropertyName("spells")] IReadOnlyList<ItemSpellPayload> Spells);

public sealed record ItemSpellPayload(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("trigger")] int Trigger,
    [property: JsonPropertyName("cooldown")] int Cooldown);

public sealed record InventoryUpdatedPayload(
    [property: JsonPropertyName("bagSlots")] int BagSlots,
    [property: JsonPropertyName("bankSlots")] int BankSlots,
    [property: JsonPropertyName("items")] IReadOnlyList<InventoryItemPayload> Items);

public sealed record InventoryItemRemovedPayload(
    [property: JsonPropertyName("itemId")] string ItemId);

public sealed record InventoryStackChangedPayload(
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("slot")] StorageSlotPayload Slot,
    [property: JsonPropertyName("count")] int Count);

public sealed record StorageSlotPayload(
    [property: JsonPropertyName("area")] string Area,
    [property: JsonPropertyName("index")] int Index);

public sealed record InventoryMovePayload(
    [property: JsonPropertyName("from")] StorageSlotPayload From,
    [property: JsonPropertyName("to")] StorageSlotPayload To);

public sealed record InventoryItemMovedPayload(
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("from")] StorageSlotPayload From,
    [property: JsonPropertyName("to")] StorageSlotPayload To);

public sealed record InventoryEquipPayload(
    [property: JsonPropertyName("from")] StorageSlotPayload From,
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("equip")] bool Equip,
    [property: JsonPropertyName("to")] StorageSlotPayload? To = null);

public sealed record InventoryItemsSwappedPayload(
    [property: JsonPropertyName("first")] InventorySwapSidePayload First,
    [property: JsonPropertyName("second")] InventorySwapSidePayload Second);

// An empty side leaves itemId off the wire, so it has to read back as optional.
public sealed record InventorySwapSidePayload(
    [property: JsonPropertyName("from")] StorageSlotPayload From,
    [property: JsonPropertyName("itemId"), JsonPropertyOrder(-1)] string? ItemId = null);

public sealed record InventoryEquipResultPayload(
    [property: JsonPropertyName("code")] string Code);

public sealed record ItemRefusedPayload(
    [property: JsonPropertyName("code")] string Code);

public sealed record EntityAppearanceChangedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("equipment")] IReadOnlyList<int> Equipment);

public sealed record InventoryUsePayload(
    [property: JsonPropertyName("from")] StorageSlotPayload From,
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("targetId")] string? TargetId = null);

public sealed record InventoryDestroyPayload(
    [property: JsonPropertyName("from")] StorageSlotPayload From,
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("count")] int Count);

public sealed record InventorySplitPayload(
    [property: JsonPropertyName("from")] StorageSlotPayload From,
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("to")] StorageSlotPayload To);

public sealed record InventorySortPayload(
    [property: JsonPropertyName("area")] string Area);

public sealed record InventorySortedPayload(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("area")] string Area);

public sealed record InventoryBankOpenPayload(
    [property: JsonPropertyName("open")] bool Open);

public sealed record InventoryBankUpdatedPayload(
    [property: JsonPropertyName("bankSlots")] int BankSlots,
    [property: JsonPropertyName("items")] IReadOnlyList<InventoryItemPayload> Items);

public sealed record InventoryExpandPayload(
    [property: JsonPropertyName("area")] string Area,
    [property: JsonPropertyName("slots")] uint Slots);

public sealed record InventoryCapacityChangedPayload(
    [property: JsonPropertyName("area")] string Area,
    [property: JsonPropertyName("capacity")] int Capacity);

public sealed record ResultPayload(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("reason")] string? Reason = null);

public sealed record LoginResultPayload(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("isAdmin")] bool IsAdmin);

public sealed record ChooseCharacterRequest(
    [property: JsonPropertyName("name")] string Name);

public sealed record OutfitRequest(
    [property: JsonPropertyName("race")] int Race,
    [property: JsonPropertyName("class")] int Class);

public sealed record OutfitPayload(
    [property: JsonPropertyName("race")] int Race,
    [property: JsonPropertyName("class")] int Class,
    [property: JsonPropertyName("equipment")] IReadOnlyList<int> Equipment);

public sealed record DeleteCharacterRequest(
    [property: JsonPropertyName("name")] string Name);

public sealed record CreateCharacterRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("race")] int Race,
    [property: JsonPropertyName("class")] int Class,
    [property: JsonPropertyName("gender")] int Gender,
    [property: JsonPropertyName("appearance")] Appearance Appearance);

public sealed record Position(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("z")] double Z);

public sealed record CharacterSummary(
    [property: JsonPropertyName("guid")] string Guid,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("gender")] int Gender,
    [property: JsonPropertyName("race")] int Race,
    [property: JsonPropertyName("class")] int Class,
    [property: JsonPropertyName("displayInfoId")] int DisplayInfoId,
    [property: JsonPropertyName("appearance")] Appearance Appearance,
    [property: JsonPropertyName("mapId")] int MapId,
    [property: JsonPropertyName("position")] Position Position,
    [property: JsonPropertyName("equipment")] IReadOnlyList<int> Equipment,
    [property: JsonPropertyName("xp")] int Xp = 0,
    [property: JsonPropertyName("xpToLevel")] int XpToLevel = 0);

public sealed record Appearance(
    [property: JsonPropertyName("skin")] int Skin,
    [property: JsonPropertyName("hairStyle")] int HairStyle,
    [property: JsonPropertyName("hairColour")] int HairColour,
    [property: JsonPropertyName("face")] int Face);

public sealed record CharactersListedPayload(
    [property: JsonPropertyName("characters")] IReadOnlyList<CharacterSummary> Characters,
    [property: JsonPropertyName("races")] IReadOnlyList<int>? Races = null);

public sealed record WorldLocation(
    [property: JsonPropertyName("map")] int Map,
    [property: JsonPropertyName("pos")] Position Position);

public sealed record WorldEnteredPayload(
    [property: JsonPropertyName("map")] int Map,
    [property: JsonPropertyName("pos")] Position Position,
    [property: JsonPropertyName("self")] WebNetworkId? Self = null,
    [property: JsonPropertyName("auras")] IReadOnlyList<HeldAuraPayload>? Auras = null);

public sealed record HeldAuraPayload(
    [property: JsonPropertyName("spellId")] uint SpellId,
    [property: JsonPropertyName("casterId")] WebNetworkId CasterId,
    [property: JsonPropertyName("harmful")] int Harmful,
    [property: JsonPropertyName("maxDuration")] int MaxDuration,
    [property: JsonPropertyName("remaining")] int Remaining);

public sealed record EntitySpawnedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("entry")] int Entry,
    [property: JsonPropertyName("position")] Position Position,
    [property: JsonPropertyName("yaw")] double Yaw,
    [property: JsonPropertyName("kind")] string? Kind = null,
    [property: JsonPropertyName("npcFlags")] uint? NpcFlags = null,
    [property: JsonPropertyName("offersQuests")] bool? OffersQuests = null,
    [property: JsonPropertyName("staticObjectType")] uint? StaticObjectType = null,
    [property: JsonPropertyName("canInteract")] bool? CanInteract = null,
    [property: JsonPropertyName("target")] Position? Target = null,
    [property: JsonPropertyName("level")] int? Level = null,
    [property: JsonPropertyName("health")] int? Health = null,
    [property: JsonPropertyName("maxHealth")] int? MaxHealth = null,
    [property: JsonPropertyName("faction")] int? Faction = null,
    [property: JsonPropertyName("dead")] bool? Dead = null,
    [property: JsonPropertyName("animationId")] uint? AnimationId = null,
    [property: JsonPropertyName("neverFightsBack")] bool? NeverFightsBack = null,
    [property: JsonPropertyName("auras")] IReadOnlyList<HeldAuraPayload>? Auras = null);

public sealed record PlayerSpawnedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("position")] Position Position,
    [property: JsonPropertyName("yaw")] double Yaw,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("class")] int Class,
    [property: JsonPropertyName("race")] int Race,
    [property: JsonPropertyName("gender")] int Gender,
    [property: JsonPropertyName("displayInfoId")] int DisplayInfoId,
    [property: JsonPropertyName("skin")] int Skin,
    [property: JsonPropertyName("hairStyle")] int HairStyle,
    [property: JsonPropertyName("hairColour")] int HairColour,
    [property: JsonPropertyName("face")] int Face,
    [property: JsonPropertyName("equipment")] IReadOnlyList<int> Equipment,
    [property: JsonPropertyName("health")] int Health,
    [property: JsonPropertyName("maxHealth")] int MaxHealth,
    [property: JsonPropertyName("power")] int Power,
    [property: JsonPropertyName("maxPower")] int MaxPower,
    [property: JsonPropertyName("auras")] IReadOnlyList<HeldAuraPayload>? Auras = null);

public sealed record EntityMovedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("position")] Position? Position = null,
    [property: JsonPropertyName("yaw")] double? Yaw = null,
    [property: JsonPropertyName("moving")] bool? Moving = null,
    [property: JsonPropertyName("state")] MovementState? State = null,
    [property: JsonPropertyName("speed")] double? Speed = null,
    [property: JsonPropertyName("speedBonus")] double? SpeedBonus = null,
    [property: JsonPropertyName("velocity")] Position? Velocity = null,
    [property: JsonPropertyName("path")] IReadOnlyList<Position>? Path = null,
    [property: JsonPropertyName("pathStep")] int? PathStep = null,
    [property: JsonPropertyName("target")] Position? Target = null);

public sealed record EntityRemovedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId);

public sealed record PlayerMovePayload(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("z")] double Z,
    [property: JsonPropertyName("yaw")] double Yaw,
    [property: JsonPropertyName("moving")] bool Moving,
    [property: JsonPropertyName("state")] MovementState State,
    [property: JsonPropertyName("velocity")] Position? Velocity = null,
    [property: JsonPropertyName("speed")] double? Speed = null);

public sealed record MoveAcceptedPayload(
    [property: JsonPropertyName("pos")] Position Position);

public sealed record NpcTeleportAnswerPayload(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId);

public sealed record BusFarePayload(
    [property: JsonPropertyName("from")] uint From,
    [property: JsonPropertyName("to")] uint To,
    [property: JsonPropertyName("cost")] int Cost);

public sealed record PlayerRevivedPayload(
    [property: JsonPropertyName("networkId")] WebNetworkId NetworkId,
    [property: JsonPropertyName("health")] int Health,
    [property: JsonPropertyName("maxHealth")] int MaxHealth,
    [property: JsonPropertyName("power")] int Power,
    [property: JsonPropertyName("maxPower")] int MaxPower,
    [property: JsonPropertyName("map")] int Map,
    [property: JsonPropertyName("pos")] Position Position,
    [property: JsonPropertyName("yaw")] double Yaw);

public sealed record ActionSlotPayload(
    [property: JsonPropertyName("bar")] string Bar,
    [property: JsonPropertyName("slot")] int Slot,
    [property: JsonPropertyName("id")] uint Id,
    [property: JsonPropertyName("type")] int Type);

public sealed record SpellBookPayload(
    [property: JsonPropertyName("spells")] IReadOnlyList<uint> Spells,
    [property: JsonPropertyName("actionBar")] IReadOnlyList<ActionSlotPayload> ActionBar);

public sealed record AdminTeleportPayload(
    [property: JsonPropertyName("mapId")] ushort MapId,
    [property: JsonPropertyName("x")] float X,
    [property: JsonPropertyName("z")] float Z,
    [property: JsonPropertyName("y")] float? Y = null);

public sealed record StressTestPayload([property: JsonPropertyName("count")] int Count);

public sealed record SpellLearnedPayload(
    [property: JsonPropertyName("spellIds")] IReadOnlyList<uint> SpellIds);

public sealed record SpellCastingPayload(
    [property: JsonPropertyName("spellId")] uint SpellId,
    [property: JsonPropertyName("casterId")] WebNetworkId CasterId,
    [property: JsonPropertyName("castTime")] int CastTime);

public sealed record SpellReleasedPayload(
    [property: JsonPropertyName("spellId")] uint SpellId,
    [property: JsonPropertyName("casterId")] WebNetworkId CasterId,
    [property: JsonPropertyName("targets")] IReadOnlyList<WebNetworkId> Targets,
    [property: JsonPropertyName("resisted")] IReadOnlyList<WebNetworkId> Resisted,
    [property: JsonPropertyName("speed")] float Speed,
    [property: JsonPropertyName("aimed")] bool Aimed);

public sealed record SpellCooldownStartedPayload(
    [property: JsonPropertyName("spellId")] uint SpellId,
    [property: JsonPropertyName("duration")] int Duration);

public sealed record SpellFailedPayload(
    [property: JsonPropertyName("casterId")] WebNetworkId CasterId,
    [property: JsonPropertyName("spellId")] uint SpellId,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record SpellInterruptedPayload(
    [property: JsonPropertyName("casterId")] WebNetworkId CasterId);

public sealed record SpellOutcomePayload(
    [property: JsonPropertyName("casterId")] WebNetworkId CasterId,
    [property: JsonPropertyName("targetId")] WebNetworkId TargetId,
    [property: JsonPropertyName("spellId")] uint SpellId,
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("damage")] int Damage,
    [property: JsonPropertyName("finalHealth")] int? FinalHealth = null,
    [property: JsonPropertyName("absorbed")] int? Absorbed = null);

public sealed record AuraAppliedPayload(
    [property: JsonPropertyName("spellId")] uint SpellId,
    [property: JsonPropertyName("unitId")] WebNetworkId UnitId,
    [property: JsonPropertyName("casterId")] WebNetworkId CasterId,
    [property: JsonPropertyName("harmful")] int Harmful,
    [property: JsonPropertyName("maxDuration")] int MaxDuration,
    [property: JsonPropertyName("remaining")] int Remaining);

public sealed record AuraEndedPayload(
    [property: JsonPropertyName("spellId")] uint SpellId,
    [property: JsonPropertyName("unitId")] WebNetworkId UnitId,
    [property: JsonPropertyName("casterId")] WebNetworkId CasterId);

public enum MovementState
{
    Standing,
    Walking,
    Swimming,
    Jumping,
    Falling,
    Landed,
    LandedMoving,
}

[JsonConverter(typeof(WebNetworkIdJsonConverter))]
public readonly record struct WebNetworkId
{
    private WebNetworkId(string? text, long number)
    {
        Text = text;
        Number = number;
        IsNumber = text is null;
    }

    public string? Text { get; }
    public long Number { get; }
    public bool IsNumber { get; }

    public static WebNetworkId FromString(string value) =>
        string.IsNullOrEmpty(value)
            ? throw new ArgumentException("Network id text must not be empty.", nameof(value))
            : new(value, 0);

    public static WebNetworkId FromNumber(long value) =>
        value < 0 ? throw new ArgumentOutOfRangeException(nameof(value)) : new(null, value);
}

internal sealed class WebNetworkIdJsonConverter : JsonConverter<WebNetworkId>
{
    public override WebNetworkId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => WebNetworkId.FromString(reader.GetString()!),
            JsonTokenType.Number when reader.TryGetInt64(out var value) => WebNetworkId.FromNumber(value),
            _ => throw new JsonException("Network id must be a non-negative integer or non-empty string."),
        };

    public override void Write(Utf8JsonWriter writer, WebNetworkId value, JsonSerializerOptions options)
    {
        if (value.IsNumber)
        {
            writer.WriteNumberValue(value.Number);
        }
        else
        {
            writer.WriteStringValue(value.Text);
        }
    }
}
