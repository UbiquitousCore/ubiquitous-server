namespace Ubiquitous.Data;

public sealed record SpellDefinition(
    uint Id,
    string NameKey,
    int CastTimeMs,
    int CooldownMs,
    float MaxRange,
    int? PowerCost = null,
    int? EffectDamage = null,
    int? EffectHeal = null,
    int? EffectDamageRandom = null,
    int? EffectHealRandom = null,
    int? AuraPeriodicDamage = null,
    int? AuraPeriodicHeal = null,
    bool AuraStun = false,
    bool AuraSleep = false,
    int DurationMs = 0,
    int? AuraAttributeId = null,
    int? AuraAttributeAmount = null,
    bool AuraAttributeIsPercent = false,
    int? AuraSlowPercent = null,
    bool AuraChill = false,
    uint? AuraTriggerSpellId = null,
    int? AuraTriggerChancePercent = null,
    IReadOnlyList<uint>? SpellGroupMask = null,
    int? AuraSpellModifierType = null,
    bool AuraSpellModifierIsPercent = false,
    int? AuraSpellModifierAmount = null,
    int? AuraSpeedupPercent = null,
    bool AuraSilence = false,
    int? AuraMeleeAttackSpeedPercent = null,
    int? AuraRangeAttackSpeedPercent = null,
    uint? AuraPeriodicTriggerSpellId = null,
    int? AuraPeriodicTriggerChancePercent = null,
    bool EffectRangedWeaponHit = false,
    int? EffectRangedWeaponDamageBonus = null,
    int? EffectRangedWeaponDamagePercent = null,
    uint? EffectTriggerSpellId = null,
    int? EffectTriggerChancePercent = null,
    bool EffectMeleeWeaponHit = false,
    int EffectMeleeWeaponSlot = EquipmentSlots.MainHand,
    int? EffectMeleeWeaponDamageBonus = null,
    int? EffectMeleeWeaponDamagePercent = null,
    bool AuraStealth = false,
    int WornSlotNeeded = -1,
    uint RequiredItemClass = 0,
    uint ItemSubclassesAllowed = 0,
    float Speed = 0f,
    int? AuraAbsorbAmount = null,
    int? AuraAbsorbPercent = null,
    int EffectTarget = 0,
    int? EffectPowerRestore = null,
    int? EffectPowerRestoreRandom = null,
    int EffectPowerKind = 0,
    IReadOnlyList<SpellDefinition>? ExtraEffects = null,
    bool EffectLeapToTarget = false,
    uint? EffectLearnSpellId = null,
    uint? AuraMountId = null,
    int? AuraMountKeepSeatPercent = null,
    int? AuraMountSpeedupPercent = null,
    int Rank = 0);

public static class SpellEffectTargets
{
    public const int Self = 1;
    public const int SingleEnemy = 2;
    public const int AreaEnemy = 4;
    public const int AreaEnemyOfTarget = 14;
    public const int PositionAreaEnemy = 16;

    public static bool IsFriendly(int target) => target is 6 or 9 or 10 or 11 or 13 or 17;
}

public static class PowerKinds
{
    public const int Mana = 1;
    public const int Morale = 2;
    public const int Energy = 3;

    public static int ForClass(int playerClass) => playerClass switch
    {
        PlayerClassIds.Warrior => Morale,
        PlayerClassIds.Mage or PlayerClassIds.Monk => Mana,
        PlayerClassIds.Ranger => Energy,
        _ => 0,
    };
}

public sealed record SpellEffectDefinition(
    uint Id,
    int Type,
    int SubType,
    int School,
    int BaseValue,
    int Spread,
    uint EffectArgument,
    int Target = 0,
    uint TargetCondition = 0,
    uint CasterCondition = 0);

public sealed record MapDefinition(
    ushort Id, string InternalName, string? File = null,
    float? MinX = null, float? MaxX = null, float? MinZ = null, float? MaxZ = null,
    ushort? RecallMapId = null, float? RecallX = null, float? RecallY = null, float? RecallZ = null,
    int MinLevel = 0, bool IsDungeon = false);

public sealed record ZoneDefinition(
    uint Id,
    ushort MapId,
    uint ParentZoneId,
    string NameKey,
    float MinX,
    float MinZ,
    float MaxX,
    float MaxZ);

public sealed record MapGateDefinition(
    ushort SourceMapId,
    float SourceMinX,
    float SourceMaxX,
    float SourceMinZ,
    float SourceMaxZ,
    ushort DestinationMapId,
    float DestinationX,
    float DestinationY,
    float DestinationZ,
    float DestinationYaw,
    bool IsDungeonEntrance = false,
    bool IsDungeonExit = false)
{
    public bool Contains(float x, float z) =>
        x >= SourceMinX && x <= SourceMaxX && z >= SourceMinZ && z <= SourceMaxZ;

    public bool Crosses(float fromX, float fromZ, float toX, float toZ)
    {
        float tMin = 0f, tMax = 1f;
        return ClipAxis(fromX, toX, SourceMinX, SourceMaxX, ref tMin, ref tMax)
            && ClipAxis(fromZ, toZ, SourceMinZ, SourceMaxZ, ref tMin, ref tMax)
            && tMin <= tMax;
    }

    private static bool ClipAxis(float from, float to, float min, float max, ref float tMin, ref float tMax)
    {
        var delta = to - from;
        if (delta == 0f)
        {
            return from >= min && from <= max;
        }

        var t1 = (min - from) / delta;
        var t2 = (max - from) / delta;
        if (t1 > t2) (t1, t2) = (t2, t1);
        tMin = Math.Max(tMin, t1);
        tMax = Math.Min(tMax, t2);
        return tMin <= tMax;
    }
}

public sealed record DungeonSummonDefinition(
    ushort MapId,
    uint CreatureEntryId,
    IReadOnlyList<string> After,
    float X,
    float Y,
    float Z,
    float Yaw = 0,
    string? Notice = null,
    TimeSpan Delay = default);

public sealed record DungeonBarrierDefinition(
    string Id,
    ushort MapId,
    float MinX,
    float MaxX,
    float MinZ,
    float MaxZ,
    string OpenedBy,
    IReadOnlyList<string>? Models = null)
{
    public IReadOnlyList<string> ModelNames => Models ?? [];

    public bool Contains(float x, float z) =>
        x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

    public bool Crosses(float fromX, float fromZ, float toX, float toZ) =>
        new MapGateDefinition(MapId, MinX, MaxX, MinZ, MaxZ, MapId, 0, 0, 0, 0)
            .Crosses(fromX, fromZ, toX, toZ);

    public bool Refuses(float fromX, float fromZ, float toX, float toZ) =>
        !Contains(fromX, fromZ) && Crosses(fromX, fromZ, toX, toZ);
}

public enum DungeonMode
{
    Normal,
    Legend,
}

public sealed record LegendCounterparts(
    IReadOnlyDictionary<uint, uint> Creatures,
    IReadOnlyDictionary<uint, uint> Spells);

public sealed record DungeonDefinition(
    ushort MapId,
    string Name,
    IReadOnlyList<uint> BossEntryIds,
    LegendCounterparts? Legend = null);

public sealed record WaypointRouteStop(
    float X,
    float Y,
    float Z,
    int PauseMilliseconds);

public sealed record WaypointRouteDefinition(
    uint Id,
    ushort MapId,
    IReadOnlyList<WaypointRouteStop> Stops,
    int? LoopToStopIndex = 0);

public sealed record CreatureDefinition(
    uint EntryId,
    string NameKey,
    int Level,
    int MaxHealth,
    int? MinDamage = null,
    int? MaxDamage = null,
    int? RespawnTimeMinSeconds = null,
    int? RespawnTimeMaxSeconds = null,
    int? MoneyDropMin = null,
    int? MoneyDropMax = null,
    int? MeleeAttackIntervalMs = null,
    int? NoticeDistance = null,
    float? LeashRange = null,
    uint RawNpcFlags = 0,
    uint RawExtraFlags = 0,
    int? FactionType = null,
    CreatureRank Rank = CreatureRank.Ordinary,
    uint FestivalId = 0,
    int? RangedAttackIntervalMs = null,
    int? Family = null)
{
    public bool FightsAtRange => RangedAttackIntervalMs is > 0;
}

public enum CreatureRank
{
    Ordinary = 0,

    Rare = 1,

    Elite = 2,

    Boss = 4,

    WorldBoss = 5,
}

public static class CreatureExtraFlags
{
    private const uint TrainingDummyBit = 1u << 7;

    public static bool IsTrainingDummy(CreatureDefinition? creature) =>
        creature is not null && (creature.RawExtraFlags & TrainingDummyBit) != 0;
}

public sealed record CreatureSpawnDefinition(
    uint Id,
    ushort MapId,
    uint CreatureEntryId,
    float X,
    float Y,
    float Z,
    float Yaw,
    int IdleBehaviour = 0,
    uint PatrolRouteId = 0,
    float WanderRadius = 0,
    uint LeaderSpawnId = 0,
    uint AnimationId = 0);

public sealed record StaticObjectSpawnDefinition(
    uint Id,
    ushort MapId,
    uint EntryId,
    float X,
    float Y,
    float Z,
    float Yaw,
    uint State = 0,
    uint Flags = 0);

public sealed record StaticObjectTypeDefinition(uint Entry, uint Type, uint Festival = 0);

public static class OperatorSpells
{
    public const uint Finish = 90001;

    public const uint Restore = 90002;

    public static readonly IReadOnlyList<uint> All = [Finish, Restore];
}

public static class FestivalIds
{
    public const uint None = 0;
    public const uint WinterSolstice = 1;
    public const uint Anniversary = 2;

    public const uint Expansion1 = 3;

    public static bool IsSeasonal(uint festival) =>
        festival is WinterSolstice or Anniversary;
}

public static class StaticObjectTypes
{
    public const uint StorageBox = 9;

    public const uint MailBox = 8;

    public const uint SummonStone = 105;
}

public sealed record StaticObjectQuestBindingDefinition(uint StaticObjectEntry, uint QuestId);

public sealed record CreatureQuestBindingDefinition(uint CreatureEntry, uint QuestId);

public sealed record LootTableItemDefinition(uint ItemId, double DropChancePercent, int MinDropCount, int MaxDropCount);

public sealed record LootGroupDefinition(int GuaranteedDrops, int MaxDrops, IReadOnlyList<LootTableItemDefinition> Items);

public sealed record CreatureLootTableDefinition(uint CreatureEntryId, IReadOnlyList<LootGroupDefinition> Groups);

public sealed record StaticObjectItemBindingDefinition(uint StaticObjectEntry, uint QuestId, uint ItemEntry, uint Reserve = 0);

public static class AttributeIds
{
    public const int Armor = 0;
    public const int HealthMax = 1;
    public const int PowerMax = 2;
    public const int CriticalStrike = 18;
    public const int HitRating = 19;
    public const int BlockRating = 20;
    public const int DodgeRating = 21;
    public const int ParryRating = 22;
    public const int Strength = 25;
    public const int Agility = 26;
    public const int Stamina = 27;
    public const int Wisdom = 28;
    public const int SpellPower = 38;
    public const int SpellCriticalRating = 40;
    public const int HealingPower = 41;
    public const int ManaRegenRating = 42;
    public const int HealthRegenRating = 43;
}

public enum TeleportStopKind
{
    BusStop = 0,
    DungeonGate = 1,
}

public sealed record MapGraveyardDefinition(
    uint Id, ushort MapId, float X, float Y, float Z, float Yaw, ushort ZoneId, uint Faction);

public sealed record TeleportStopDefinition(
    uint Id, ushort MapId, float X, float Y, float Z, float Yaw = 0f,
    TeleportStopKind Kind = TeleportStopKind.BusStop);

public sealed record TravelRouteDefinition(uint Id, uint FirstStop, uint SecondStop, int Price);

public sealed record StorageUpgradeDefinition(
    uint Id, string Area, uint SlotCount, uint FromSlotCount, uint PriceCurrency, uint PriceAmount);

public sealed record TalentTreeDefinition(uint Id, int ClassId, IReadOnlyList<TalentDefinition> Talents);

public sealed record TalentDefinition(
    uint Id,
    int Tier,
    int Column,
    int PointsInTreeRequired,
    IReadOnlyList<uint> Prerequisites,
    IReadOnlyList<uint> RankSpellIds)
{
    public int MaxRank => RankSpellIds.Count;
}

public enum NpcFlags : uint
{
    None = 0,
    Gossip = 1u << 0,
    Vendor = 1u << 2,

    Auctioneer = 1u << 21,

    Teleporter = 1u << 25,
}

public sealed record NpcDefinition(uint EntryId, NpcFlags Flags, uint GreetingMenuId = 0, bool OffersQuests = false);

// Scripts holds the script id each choice slot runs, slot 1 first; 0 runs none.
public sealed record GossipMenuDefinition(
    uint Id, IReadOnlyList<uint> SubMenus, IReadOnlyList<GossipTeleportDefinition> Teleports,
    IReadOnlyList<uint>? Scripts = null);

public sealed record GossipTeleportDefinition(
    int Choice, uint ZoneId, ushort MapId, float X, float Y, float Z, float Yaw,
    uint Currency, int Cost);

public static class CreatureFamilies
{
    public const int Critter = 9;
}

public static class UnitFactionTypes
{
    public const int Hostile = 0;

    public const int Townsfolk = 1;

    public const int Neutral = 2;

    public static readonly IReadOnlySet<int> AllWorld = new HashSet<int> { Hostile, Townsfolk, Neutral };
}

public sealed record VendorItem(
    int Slot, int Entry, int NameTextId, int DisplayInfoId, int BuyPrice, int MaxCount, int StackCount);

public sealed record VendorTab(
    int TabIndex, int Currency, int Count, bool ItemDataDecoded, IReadOnlyList<VendorItem> Items);

public sealed record VendorStockDefinition(uint SellerEntryId, VendorTab Tab);

public enum QuestObjectiveKind
{
    Killed,
    Held,
    Triggered,
}

public sealed record QuestObjectiveDefinition(
    int Index,
    QuestObjectiveKind Kind,
    int GoalCount,
    uint ObjectiveCreature = 0,
    uint ObjectiveItem = 0,
    uint ObjectiveStringId = 0,
    uint ObjectiveObject = 0,
    uint ObjectiveSpell = 0);

public sealed record QuestDefinition(
    uint Id,
    uint? GiverEntryId,
    uint TitleStringId,
    uint DetailsStringId,
    int MinLevel,
    IReadOnlyList<QuestObjectiveDefinition>? Objectives = null,
    uint AllowedClassMask = 0,
    bool IsRepeatable = false,
    IReadOnlyList<uint>? PrerequisitesAll = null,
    IReadOnlyList<uint>? AnyOfQuestIds = null,
    int Level = 0)
{
    public IReadOnlyList<QuestObjectiveDefinition> Objectives { get; init; } = Objectives ?? [];

    public IReadOnlyList<uint> PrerequisitesAll { get; init; } = PrerequisitesAll ?? [];

    public IReadOnlyList<uint> AnyOfQuestIds { get; init; } = AnyOfQuestIds ?? [];
}

public static class QuestClassGate
{
    public static bool Allows(QuestDefinition quest, int? playerClassId)
    {
        if (quest.AllowedClassMask == 0 || playerClassId is null) return true;
        var classId = playerClassId.Value;
        return classId >= 0 && classId < 32 && (quest.AllowedClassMask & (1u << classId)) != 0;
    }
}

// A quest whose objective is casting a spell is for those who have that spell: the
// class training quests ask each class to try its own first skill on a dummy.
public static class QuestSpellGate
{
    public static bool Allows(QuestDefinition quest, IReadOnlySet<uint>? knownSpells) =>
        knownSpells is null || quest.Objectives.All(objective =>
            objective is not { Kind: QuestObjectiveKind.Triggered, ObjectiveSpell: not 0, ObjectiveObject: 0 }
            || knownSpells.Contains(objective.ObjectiveSpell));
}

public static class QuestPrerequisiteGate
{
    public static bool Allows(QuestDefinition quest, IReadOnlyCollection<uint>? completedQuestIds)
    {
        if (completedQuestIds is null) return true;
        foreach (var required in quest.PrerequisitesAll)
        {
            if (!completedQuestIds.Contains(required)) return false;
        }

        return quest.AnyOfQuestIds.Count == 0 || quest.AnyOfQuestIds.Any(completedQuestIds.Contains);
    }
}

public static class QuestLevelGate
{
    public static bool Allows(QuestDefinition quest, int? playerLevel) =>
        playerLevel is null || playerLevel.Value >= quest.MinLevel;
}

public static class QuestFinisherGate
{
    public static bool HasBinding(
        uint questId,
        IReadOnlyList<CreatureQuestBindingDefinition>? creatureFinishers,
        IReadOnlyList<StaticObjectQuestBindingDefinition>? staticObjectFinishers) =>
        creatureFinishers?.Any(binding => binding.QuestId == questId) == true
        || staticObjectFinishers?.Any(binding => binding.QuestId == questId) == true;

    public static bool BindsCreature(
        uint questId, uint creatureEntry, IReadOnlyList<CreatureQuestBindingDefinition>? creatureFinishers) =>
        creatureFinishers?.Any(binding =>
            binding.QuestId == questId && binding.CreatureEntry == creatureEntry) == true;

    public static bool BindsStaticObject(
        uint questId, uint staticObjectEntry, IReadOnlyList<StaticObjectQuestBindingDefinition>? staticObjectFinishers) =>
        staticObjectFinishers?.Any(binding =>
            binding.QuestId == questId && binding.StaticObjectEntry == staticObjectEntry) == true;

    public static bool MarksCreature(
        QuestDefinition quest,
        uint creatureEntry,
        IReadOnlyList<CreatureQuestBindingDefinition>? creatureFinishers,
        IReadOnlyList<StaticObjectQuestBindingDefinition>? staticObjectFinishers) =>
        BindsCreature(quest.Id, creatureEntry, creatureFinishers)
        || (!HasBinding(quest.Id, creatureFinishers, staticObjectFinishers) && quest.GiverEntryId == creatureEntry);
}

public sealed record QuestRewardItem(uint Entry, int Count, uint NameTextId, int Quality);

public sealed record QuestRewardDefinition(
    uint QuestId,
    int Money,
    IReadOnlyList<QuestRewardItem> Fixed,
    IReadOnlyList<QuestRewardItem> Choices,
    int Xp = 0);

public sealed record ItemAttributeBonus(int Attribute, int Amount);

public static class ItemStatKinds
{
    private static readonly IReadOnlyDictionary<int, int> ToAttribute = new Dictionary<int, int>
    {
        [1] = AttributeIds.HealthMax,
        [2] = AttributeIds.PowerMax,
        [10] = AttributeIds.Armor,
        [11] = AttributeIds.Strength,
        [12] = AttributeIds.Agility,
        [13] = AttributeIds.Stamina,
        [14] = AttributeIds.Wisdom,
        [17] = AttributeIds.HitRating,
        [18] = AttributeIds.CriticalStrike,
        [20] = AttributeIds.BlockRating,
        [22] = AttributeIds.DodgeRating,
        [23] = AttributeIds.ParryRating,
        [24] = AttributeIds.SpellPower,
        [26] = AttributeIds.SpellCriticalRating,
        [27] = AttributeIds.HealingPower,
        [28] = AttributeIds.ManaRegenRating,
        [29] = AttributeIds.HealthRegenRating,
    };

    public static int? AttributeFor(int kind) =>
        ToAttribute.TryGetValue(kind, out var attribute) ? attribute : null;
}

public sealed record ItemDefinition(
    uint Id, string NameKey, byte Class, byte SubClass, uint DisplayInfoId,
    uint? OnUseSpellId = null, uint? OnUseCooldownSeconds = null, uint? BuyPrice = null,
    uint? QuestId = null, uint? SellPrice = null, uint? AllowableClassMask = null,
    uint InventoryType = 0, uint RequiredLevel = 0, uint? AllowableRaceMask = null,
    uint? Quality = null,
    uint? MinDamage = null, uint? MaxDamage = null, uint? SpeedMs = null, uint? Armor = null,
    IReadOnlyList<ItemAttributeBonus>? Stats = null,
    uint? RequiredSpellId = null,
    uint ItemLevel = 0, uint MaxStack = 0);

public static class GuildStone
{
    private const byte StoneClass = 0;
    private const byte StoneSubClass = 10;

    public static bool Matches(ItemDefinition item) =>
        item.Class == StoneClass && item.SubClass == StoneSubClass;
}

public static class ItemClassMask
{
    public static uint BitFor(int playerClass) => 1u << playerClass;

    public static bool Allows(uint? mask, int playerClass) =>
        mask is null or 0 || (mask.Value & BitFor(playerClass)) != 0;
}

public static class ItemRaceMask
{
    public static uint BitFor(int playerRace) => 1u << playerRace;

    public static bool Allows(uint? mask, int playerRace) =>
        mask is null or 0 || (mask.Value & BitFor(playerRace)) != 0;
}

public static class PlayerRaceIds
{
    public const int Human = 0;
    public const int Elf = 1;
    public const int Orc = 2;
    public const int Undead = 3;
    public const int Mendel = 7;

    public static readonly IReadOnlySet<int> All = new HashSet<int> { Human, Elf, Orc, Undead, Mendel };
}

public static class PlayerClassIds
{
    public const int Warrior = 1;
    public const int Mage = 2;
    public const int Ranger = 3;
    public const int Monk = 4;

    public static readonly IReadOnlySet<int> All = new HashSet<int> { Warrior, Mage, Ranger, Monk };
}

public static class ItemClasses
{
    public const byte Weapons = 2;
    public const byte Riding = 14;
}

public static class EquipmentSlots
{
    public const int Head = 0;
    public const int Neck = 1;
    public const int Shoulder = 2;
    public const int Hands = 3;
    public const int Chest = 4;
    public const int Shirt = 5;
    public const int Back = 6;
    public const int Legs = 7;
    public const int Feet = 8;
    public const int Ring1 = 9;
    public const int Ring2 = 10;
    public const int Trinket1 = 11;
    public const int Trinket2 = 12;
    public const int MainHand = 13;
    public const int OffHand = 14;
    public const int Ranged = 15;

    public const int Count = 16;

    public static IReadOnlyList<int> Build(params (int Slot, int Entry)[] worn)
    {
        var slots = new int[Count];
        foreach (var (slot, entry) in worn)
        {
            if (slot < 0 || slot >= Count)
                throw new ArgumentOutOfRangeException(nameof(worn), slot, $"Slot must be 0-{Count - 1}.");
            slots[slot] = entry;
        }

        return slots;
    }
}

public sealed record StartingLoadoutItem(uint ItemEntry, int Slot, int Count)
{
    public bool IsWorn => Slot >= 0 && Slot < EquipmentSlots.Count;

    public int BagIndex => Slot - EquipmentSlots.Count;
}

public sealed record StartingActionButton(int Slot, uint SpellId);

public sealed record StartingAttributes(int Strength, int Agility, int Stamina, int Intellect);

public sealed record CharacterStartingLoadout(
    int Race,
    int PlayerClass,
    ushort MapId,
    float X,
    float Y,
    float Z,
    float Orientation,
    StartingAttributes Attributes,
    IReadOnlyList<StartingLoadoutItem> Items,
    IReadOnlyList<uint> Spells,
    IReadOnlyList<StartingActionButton> ActionBar)
{
    public IReadOnlyList<int> WornEquipment() => EquipmentSlots.Build(
        [.. Items.Where(item => item.IsWorn).Select(item => (item.Slot, (int)item.ItemEntry))]);

    public IReadOnlyList<StartingLoadoutItem> BagItems =>
        [.. Items.Where(item => !item.IsWorn).OrderBy(item => item.Slot)];
}

public sealed record StartingItemStack(uint Entry, int Count);

public sealed record PlayerDisplayDefinition(
    int Gender,
    int Race,
    int Class,
    int DisplayInfoId,
    int Skin,
    int HairStyle,
    int HairColour,
    int Face);

// The looks one playable body offers, each option numbered as the build numbers it.
public sealed record PlayerLookDefinition(
    int Race, int Gender, IReadOnlyList<int> Skins, IReadOnlyList<int> Faces,
    IReadOnlyList<int> HairStyles, IReadOnlyList<int> HairColours)
{
    public const string Dataset = "playerLooks";
}

public static class PlayerDisplayValidator
{
    public static IReadOnlyList<string> Validate(PlayerDisplayDefinition display)
    {
        var errors = new List<string>();
        if (display.Gender < 0) errors.Add("display.gender must not be negative.");
        if (!PlayerRaceIds.All.Contains(display.Race)) errors.Add("display.race is not a known race id.");
        if (display.Class < 0) errors.Add("display.class must not be negative.");
        if (display.DisplayInfoId < 0) errors.Add("display.displayInfoId must not be negative.");
        if (display.Skin < 0) errors.Add("display.skin must not be negative.");
        if (display.HairStyle < 0) errors.Add("display.hairStyle must not be negative.");
        if (display.HairColour < 0) errors.Add("display.hairColour must not be negative.");
        if (display.Face < 0) errors.Add("display.face must not be negative.");
        return errors;
    }
}
