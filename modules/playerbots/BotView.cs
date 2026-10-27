using System.Globalization;
using System.Text.Json;
using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Position = Ubiquitous.Common.Position;
using WirePosition = Ubiquitous.Protocol.Web.Position;

namespace Ubiquitous.Modules.Playerbots;

public enum UnitKind
{
    Creature,
    StaticObject,
    Player,
}

public sealed class SeenUnit(string id, UnitKind kind)
{
    public string Id { get; } = id;
    public UnitKind Kind { get; } = kind;
    public uint Entry { get; set; }
    public Position Position { get; set; }
    public string? Name { get; set; }
    public int Level { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int? Faction { get; set; }
    public bool Dead { get; set; }
    public bool OffersQuests { get; set; }
    public uint NpcFlags { get; set; }
    public bool NeverFightsBack { get; set; }
    public bool Lootable { get; set; }
    public bool InFightWithMe { get; set; }
    public string? Targeting { get; set; }
    public int Class { get; set; }
}

public sealed record HeldItem(string Id, int Entry, int Count, string Area, int Index);

public sealed record QuestZone(ushort MapId, Position Center, float Radius);

public sealed record ObjectiveState(int Index, int Done, int Goal);

public sealed record QuestState(uint Id, bool Ready, IReadOnlyList<ObjectiveState> Objectives,
    IReadOnlyList<QuestZone> GuideZones, QuestZone? HandInZone, int Slot = 0);

public sealed record ChatHeard(string Channel, string? SenderId, string SenderName, string Text);

public sealed record LootRow(int Slot, int Entry, int Count);

public sealed record LootWindow(string CorpseId, int Money, IReadOnlyList<LootRow> Rows);

public sealed record CharacterChoice(string Name, int Level, int Skin, int Class);

// What the bot knows, built only from the messages a client would receive.
public sealed class BotView
{
    public bool? LoggedIn { get; private set; }
    public IReadOnlyList<CharacterChoice>? Characters { get; private set; }
    public IReadOnlyList<int> OfferedRaces { get; private set; } = [];
    public ResultPayload? LastCreate { get; private set; }
    public ResultPayload? LastDelete { get; private set; }
    public bool InWorld { get; private set; }
    public int MoveCorrections { get; private set; }

    public void ForgetCharacters()
    {
        Characters = null;
        LastCreate = null;
        LastDelete = null;
    }

    public string? SelfId { get; private set; }
    public int Class { get; set; }
    public ushort MapId { get; private set; }
    public Position Position { get; set; }
    public int Level { get; private set; } = 1;
    public int Health { get; private set; } = 1;
    public int MaxHealth { get; private set; } = 1;
    public int Power { get; private set; }
    public int MaxPower { get; private set; }
    public bool Dead { get; private set; }
    public bool InCombat { get; private set; }
    public string? AttackingId { get; private set; }
    public DateTimeOffset CastingUntil { get; private set; }

    public Dictionary<string, SeenUnit> Units { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Attackers { get; } = new(StringComparer.Ordinal);
    public HashSet<string> KilledByMe { get; } = new(StringComparer.Ordinal);
    public Dictionary<uint, QuestState> Quests { get; } = [];
    public Dictionary<string, string> GiverStatus { get; } = new(StringComparer.Ordinal);
    public List<(string GiverId, uint QuestId)> QuestOffers { get; } = [];
    public HashSet<uint> KnownSpells { get; } = [];
    public Dictionary<uint, DateTimeOffset> CooldownUntil { get; } = [];
    public LootWindow? Loot { get; set; }
    public List<(uint SpellId, string Reason)> SpellRefusals { get; } = [];

    public string? PartyLeaderId { get; private set; }
    public List<string> PartyMemberIds { get; } = [];
    public Dictionary<string, string> PartyNames { get; } = new(StringComparer.Ordinal);
    public (string Id, string Name)? PendingInvite { get; set; }
    public List<ChatHeard> Heard { get; } = [];
    public int Gold { get; private set; }

    public Dictionary<string, HeldItem> Items { get; } = new(StringComparer.Ordinal);
    public int BagSlots { get; private set; }
    public int TalentPoints { get; private set; }
    public Dictionary<uint, int> TalentRanks { get; } = [];
    public List<uint> TalentRefusals { get; } = [];
    public string? DuelAskedBy { get; set; }
    public string? SummonAskedBy { get; set; }
    public string? DuelOpponent { get; private set; }
    public DateTimeOffset DuelStartsAt { get; private set; }
    public string? TradeAskedBy { get; set; }
    public string? TradingWith { get; private set; }
    public bool TradeTheyConfirmed { get; private set; }
    public bool TradeIConfirmed { get; private set; }
    public List<string> SellRefusals { get; } = [];
    public List<string> ItemRefusals { get; } = [];

    // Where players were last seen before they left sight, and where vendors stand.
    public Dictionary<string, (ushort MapId, Position Position)> LastSeenPlayers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, (ushort MapId, Position Position)> Vendors { get; } = new(StringComparer.Ordinal);

    public int FreeBagSlots => BagSlots - Items.Values.Count(item => item.Area == InventoryAreas.Bag);

    public bool InParty => PartyMemberIds.Count > 0;

    public float HealthFraction => MaxHealth <= 0 ? 1f : (float)Health / MaxHealth;

    public void Apply(string message, DateTimeOffset now)
    {
        WebMessageEnvelope envelope;
        try { envelope = WebMessageJson.Deserialize(message); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { return; }

        var payload = envelope.Payload;
        switch (envelope.Type)
        {
            case WebMessageTypes.AccountLoginResult:
                LoggedIn = Read<ResultPayload>(payload).Ok;
                break;
            case WebMessageTypes.CharactersListed:
                var listed = Read<CharactersListedPayload>(payload);
                Characters = [.. listed.Characters.Select(c => new CharacterChoice(c.Name, c.Level, c.Appearance.Skin, c.Class))];
                OfferedRaces = listed.Races ?? [];
                break;
            case WebMessageTypes.CharactersCreated:
                LastCreate = Read<ResultPayload>(payload);
                break;
            case WebMessageTypes.CharactersDeleted:
                LastDelete = Read<ResultPayload>(payload);
                break;
            case WebMessageTypes.WorldEntered:
                var entered = Read<WorldEnteredPayload>(payload);
                InWorld = true;
                MapId = (ushort)entered.Map;
                Position = At(entered.Position);
                if (entered.Self is { } self) SelfId = Id(self);
                Units.Clear();
                Attackers.Clear();
                AttackingId = null;
                break;
            case WebMessageTypes.PlayerRevived:
                var revived = Read<PlayerRevivedPayload>(payload);
                if (Id(revived.NetworkId) != SelfId)
                {
                    if (Units.TryGetValue(Id(revived.NetworkId), out var risen))
                    {
                        risen.Dead = false;
                        risen.Health = revived.Health;
                        risen.Position = At(revived.Position);
                    }

                    break;
                }

                Dead = false;
                Health = revived.Health;
                MaxHealth = revived.MaxHealth;
                Power = revived.Power;
                MaxPower = revived.MaxPower;
                Position = At(revived.Position);
                break;
            case WebMessageTypes.PlayerDeath:
                Dead = true;
                Health = 0;
                AttackingId = null;
                Attackers.Clear();
                break;
            case WebMessageTypes.MoveAccepted:
                Position = At(Read<MoveAcceptedPayload>(payload).Position);
                MoveCorrections++;
                break;
            case WebMessageTypes.PlayerValuesChanged:
                ApplySelfValues(Read<PlayerValuesChangedPayload>(payload).Changed);
                break;
            case WebMessageTypes.EntitySpawned:
                Spawned(payload);
                break;
            case WebMessageTypes.EntityMoved:
                var moved = Read<EntityMovedPayload>(payload);
                if (Units.TryGetValue(Id(moved.NetworkId), out var mover))
                    mover.Position = At(moved.Target ?? moved.Position ?? new WirePosition(mover.Position.X, mover.Position.Y, mover.Position.Z));
                break;
            case WebMessageTypes.EntityRemoved:
                var removed = Id(Read<EntityRemovedPayload>(payload).NetworkId);
                if (Units.TryGetValue(removed, out var leaving) && leaving.Kind == UnitKind.Player)
                    LastSeenPlayers[removed] = (MapId, leaving.Position);
                Units.Remove(removed);
                Attackers.Remove(removed);
                if (AttackingId == removed) AttackingId = null;
                break;
            case WebMessageTypes.EntityValuesChanged:
                var values = Read<EntityValuesChangedPayload>(payload);
                if (Id(values.NetworkId) == SelfId) ApplySelfValues(values.Changed);
                else if (Units.TryGetValue(Id(values.NetworkId), out var valued)) ApplyUnitValues(valued, values.Changed);
                break;
            case WebMessageTypes.EntityLootable:
                var lootable = Read<EntityLootablePayload>(payload);
                if (Units.TryGetValue(Id(lootable.NetworkId), out var corpse))
                    corpse.Lootable = lootable.State == WireCodes.Loot.Lootable;
                break;
            case WebMessageTypes.AttackMeleeStart:
            case WebMessageTypes.AttackRangeStart:
                var start = Read<AttackMeleeStartPayload>(payload);
                Started(Id(start.AttackerId), Id(start.TargetId));
                break;
            case WebMessageTypes.AttackSwingResult:
                var swing = Read<AttackSwingResultPayload>(payload);
                Struck(Id(swing.AttackerId), Id(swing.TargetId), swing.FinalHealth);
                break;
            case WebMessageTypes.AttackShotResult:
                var shot = Read<AttackShotResultPayload>(payload);
                Struck(Id(shot.AttackerId), Id(shot.TargetId), shot.FinalHealth);
                break;
            case WebMessageTypes.SpellOutcome:
                var outcome = Read<SpellOutcomePayload>(payload);
                if (outcome.FinalHealth is { } spellHealth)
                    Struck(Id(outcome.CasterId), Id(outcome.TargetId), spellHealth);
                else if (Id(outcome.CasterId) == SelfId && Units.TryGetValue(Id(outcome.TargetId), out var hit))
                    hit.InFightWithMe = true;
                break;
            case WebMessageTypes.AttackHalted:
                var halted = Id(Read<AttackHaltedPayload>(payload).NetworkId);
                if (halted == SelfId || halted == AttackingId) AttackingId = null;
                break;
            case WebMessageTypes.AttackEnterCombat:
                if (Id(Read<AttackEnterCombatPayload>(payload).NetworkId) == SelfId) InCombat = true;
                break;
            case WebMessageTypes.AttackExitCombat:
                if (Id(Read<AttackExitCombatPayload>(payload).NetworkId) == SelfId)
                {
                    InCombat = false;
                    AttackingId = null;
                    Attackers.Clear();
                }
                break;
            case WebMessageTypes.UnitDied:
                Died(Id(Read<UnitDiedPayload>(payload).NetworkId));
                break;
            case WebMessageTypes.QuestStatuses:
                foreach (var row in Read<QuestStatusesPayload>(payload).Statuses) GiverStatus[Id(row.NetworkId)] = row.Status;
                break;
            case WebMessageTypes.QuestLog:
                Quests.Clear();
                foreach (var entry in Read<QuestLogPayload>(payload).Quests)
                    Quests[entry.Id] = QuestFrom(entry.Id, entry.ReadyToHandIn, entry.Objectives, entry.GuideZones, entry.HandInZone) with { Slot = entry.Slot };
                break;
            case WebMessageTypes.QuestUpdated:
                var update = Read<QuestUpdatePayload>(payload);
                Quests[update.Id] = QuestFrom(update.Id, update.ReadyToHandIn, update.Objectives, update.GuideZones, update.HandInZone) with { Slot = update.Slot };
                break;
            case WebMessageTypes.QuestRemoved:
                Quests.Remove(Read<QuestRemovedPayload>(payload).QuestId);
                break;
            case WebMessageTypes.QuestListUpdated:
                var list = Read<QuestListPayload>(payload);
                QuestOffers.Clear();
                foreach (var quest in list.Quests.Where(q => q.Status == QuestStates.Offered))
                    QuestOffers.Add((Id(list.GiverId), quest.Id));
                break;
            case WebMessageTypes.QuestDetails:
                var details = Read<QuestDetailsPayload>(payload);
                QuestOffers.Clear();
                if (details.Status == QuestStates.Offered) QuestOffers.Add((Id(details.GiverId), details.Id));
                break;
            case WebMessageTypes.SpellBook:
                KnownSpells.Clear();
                KnownSpells.UnionWith(Read<SpellBookPayload>(payload).Spells);
                break;
            case WebMessageTypes.SpellCooldownStarted:
                var cooldown = Read<SpellCooldownStartedPayload>(payload);
                CooldownUntil[cooldown.SpellId] = now.AddMilliseconds(cooldown.Duration);
                break;
            case WebMessageTypes.SpellCasting:
                var casting = Read<SpellCastingPayload>(payload);
                if (Id(casting.CasterId) == SelfId) CastingUntil = now.AddMilliseconds(casting.CastTime);
                break;
            case WebMessageTypes.SpellFailed:
                var failed = Read<SpellFailedPayload>(payload);
                if (Id(failed.CasterId) != SelfId) break;
                CastingUntil = default;
                SpellRefusals.Add((failed.SpellId, failed.Reason));
                break;
            case WebMessageTypes.SpellReleased:
                var released = Read<SpellReleasedPayload>(payload);
                if (Id(released.CasterId) != SelfId) break;
                CastingUntil = default;
                foreach (var aimedAt in released.Targets.Concat(released.Resisted))
                    if (Units.TryGetValue(Id(aimedAt), out var aimed) && aimed.Kind != UnitKind.Player) aimed.InFightWithMe = true;
                break;
            case WebMessageTypes.SpellInterrupted:
                if (Id(Read<SpellInterruptedPayload>(payload).CasterId) == SelfId) CastingUntil = default;
                break;
            case WebMessageTypes.LootContents:
                var contents = Read<LootContentsPayload>(payload);
                Loot = new LootWindow(Id(contents.NetworkId), contents.Money,
                    [.. contents.Items.Select(row => new LootRow(row.Slot, row.Entry, row.Count))]);
                break;
            case WebMessageTypes.LootSlotEmptied:
                var emptied = Read<LootSlotEmptiedPayload>(payload);
                if (Loot is { } window && window.CorpseId == Id(emptied.NetworkId))
                    Loot = emptied.Money ? window with { Money = 0 } : window with { Rows = [.. window.Rows.Where(row => row.Slot != emptied.Slot)] };
                break;
            case WebMessageTypes.PartyInvited:
                var invite = Read<PartyInvitedPayload>(payload);
                PendingInvite = (Id(invite.NetworkId), invite.Name);
                break;
            case WebMessageTypes.PartyRoster:
                var roster = Read<PartyRosterPayload>(payload);
                PartyMemberIds.Clear();
                PartyNames.Clear();
                PartyLeaderId = null;
                if (roster.PartyId is null) break;
                foreach (var member in roster.Members)
                {
                    PartyMemberIds.Add(Id(member.NetworkId));
                    PartyNames[Id(member.NetworkId)] = member.Name;
                    if (member.IsLeader) PartyLeaderId = Id(member.NetworkId);
                }
                break;
            case WebMessageTypes.PartyKicked:
                PartyMemberIds.Clear();
                PartyNames.Clear();
                PartyLeaderId = null;
                break;
            case WebMessageTypes.InventoryUpdated:
                var inventory = Read<InventoryUpdatedPayload>(payload);
                BagSlots = inventory.BagSlots;
                Items.Clear();
                foreach (var item in inventory.Items) Hold(item);
                break;
            case WebMessageTypes.InventoryCapacityChanged:
                if (payload.TryGetProperty("area", out var area) && area.GetString() == InventoryAreas.Bag
                    && payload.TryGetProperty("capacity", out var capacity) && capacity.TryGetInt32(out var slots))
                    BagSlots = slots;
                break;
            case WebMessageTypes.InventoryItemAdded:
                Hold(Read<InventoryItemPayload>(payload));
                break;
            case WebMessageTypes.InventoryItemRemoved:
                Items.Remove(Read<InventoryItemRemovedPayload>(payload).ItemId);
                break;
            case WebMessageTypes.InventoryStackChanged:
                var stack = Read<InventoryStackChangedPayload>(payload);
                Items[stack.ItemId] = Items.TryGetValue(stack.ItemId, out var stacked)
                    ? stacked with { Count = stack.Count, Area = stack.Slot.Area, Index = stack.Slot.Index }
                    : new HeldItem(stack.ItemId, 0, stack.Count, stack.Slot.Area, stack.Slot.Index);
                break;
            case WebMessageTypes.InventoryItemMoved:
                var moved2 = Read<InventoryItemMovedPayload>(payload);
                if (Items.TryGetValue(moved2.ItemId, out var shifted))
                    Items[moved2.ItemId] = shifted with { Area = moved2.To.Area, Index = moved2.To.Index };
                break;
            case WebMessageTypes.InventoryItemsSwapped:
                var swapped = Read<InventoryItemsSwappedPayload>(payload);
                Swap(swapped.First, swapped.Second);
                break;
            case WebMessageTypes.ItemRefused:
                ItemRefusals.Add(Read<ItemRefusedPayload>(payload).Code);
                break;
            case WebMessageTypes.SellResult:
                var sold = Read<SellResultPayload>(payload);
                if (sold.Code != ItemErrors.Ok) SellRefusals.Add(sold.Code);
                break;
            case WebMessageTypes.TalentState:
                var talents = Read<TalentStatePayload>(payload);
                TalentPoints = talents.Points;
                TalentRanks.Clear();
                foreach (var rank in talents.Ranks) TalentRanks[rank.TalentId] = rank.Rank;
                break;
            case WebMessageTypes.TalentRefused:
                TalentRefusals.Add(Read<TalentRefusedPayload>(payload).TalentId);
                break;
            case WebMessageTypes.DuelChallenged:
                DuelAskedBy = Id(Read<DuelChallengedPayload>(payload).FromId);
                break;
            case WebMessageTypes.SummonAsked:
                SummonAskedBy = Id(Read<SummonAskedPayload>(payload).SummonerId);
                break;
            case WebMessageTypes.DuelStarted:
                var duel = Read<DuelStartedPayload>(payload);
                DuelOpponent = Id(duel.OpponentId);
                DuelStartsAt = now.AddSeconds(duel.StartsIn);
                break;
            case WebMessageTypes.DuelEnded:
                var ended = Read<DuelEndedPayload>(payload);
                DuelOpponent = null;
                Health = ended.Health;
                MaxHealth = ended.MaxHealth;
                Attackers.Remove(Id(ended.OpponentId));
                if (AttackingId == Id(ended.OpponentId)) AttackingId = null;
                break;
            case WebMessageTypes.TradeRequested:
                TradeAskedBy = Id(Read<TradeRequestedPayload>(payload).FromId);
                break;
            case WebMessageTypes.TradeStarted:
                TradingWith = Id(Read<TradeStartedPayload>(payload).OpponentId);
                TradeTheyConfirmed = TradeIConfirmed = false;
                break;
            case WebMessageTypes.TradeUpdated:
                var trade = Read<TradeUpdatedPayload>(payload);
                TradingWith = Id(trade.OpponentId);
                TradeTheyConfirmed = trade.OpponentConfirmed;
                TradeIConfirmed = trade.YourConfirmed;
                break;
            case WebMessageTypes.TradeCancelled:
            case WebMessageTypes.TradeCompleted:
            case WebMessageTypes.TradeDeclined:
                TradingWith = null;
                TradeTheyConfirmed = TradeIConfirmed = false;
                break;
            case WebMessageTypes.ChatReceived:
                var chat = Read<ChatReceivedPayload>(payload);
                Heard.Add(new ChatHeard(chat.Channel, null, chat.Sender, chat.Text));
                break;
            case WebMessageTypes.ChatWhisperReceived:
                var whisper = Read<ChatWhisperReceivedPayload>(payload);
                Heard.Add(new ChatHeard(WebMessageTypes.ChatWhisper, Id(whisper.SenderNetworkId), whisper.SenderName, whisper.Text));
                break;
        }
    }

    private void Hold(InventoryItemPayload item)
    {
        if (item.Slot is not { } slot) return;
        Items[item.Id] = new HeldItem(item.Id, item.Entry, item.Count, slot.Area, slot.Index);
    }

    // The first item now stands where the second was, and the second (if any) where the first was.
    private void Swap(InventorySwapSidePayload first, InventorySwapSidePayload second)
    {
        if (first.ItemId is { } a && Items.TryGetValue(a, out var firstItem))
            Items[a] = firstItem with { Area = second.From.Area, Index = second.From.Index };
        if (second.ItemId is { } b && Items.TryGetValue(b, out var secondItem))
            Items[b] = secondItem with { Area = first.From.Area, Index = first.From.Index };
    }

    private void Spawned(JsonElement payload)
    {
        var id = payload.TryGetProperty("networkId", out var idValue) ? Id(idValue) : "";
        if (id.Length == 0 || id == SelfId) return;
        if (id.StartsWith("player:", StringComparison.Ordinal))
        {
            var player = Read<PlayerSpawnedPayload>(payload);
            Units[id] = new SeenUnit(id, UnitKind.Player)
            {
                Position = At(player.Position),
                Name = player.Name,
                Level = player.Level,
                Health = player.Health,
                MaxHealth = player.MaxHealth,
                Dead = player.Health <= 0,
                Class = player.Class,
            };
            LastSeenPlayers.Remove(id);
            return;
        }

        var spawned = Read<EntitySpawnedPayload>(payload);
        if (((spawned.NpcFlags ?? 0) & (uint)Ubiquitous.Data.NpcFlags.Vendor) != 0)
            Vendors[id] = (MapId, At(spawned.Position));
        Units[id] = new SeenUnit(id, spawned.Kind == "staticObject" ? UnitKind.StaticObject : UnitKind.Creature)
        {
            Entry = (uint)spawned.Entry,
            Position = At(spawned.Target ?? spawned.Position),
            Level = spawned.Level ?? 0,
            Health = spawned.Dead == true ? 0 : spawned.Health ?? 1,
            MaxHealth = spawned.MaxHealth ?? 1,
            Faction = spawned.Faction,
            Dead = spawned.Dead == true,
            OffersQuests = spawned.OffersQuests == true,
            NpcFlags = spawned.NpcFlags ?? 0,
            NeverFightsBack = spawned.NeverFightsBack == true,
        };
    }

    private void Started(string attacker, string target)
    {
        if (attacker == SelfId) AttackingId = target;
        else if (target == SelfId) Attackers.Add(attacker);
        if (Units.TryGetValue(attacker, out var unit))
        {
            unit.Targeting = target;
            unit.InFightWithMe |= target == SelfId;
        }

        if (attacker == SelfId && Units.TryGetValue(target, out var foe)) foe.InFightWithMe = true;
    }

    private void Struck(string attacker, string target, int finalHealth)
    {
        if (target == SelfId)
        {
            Health = finalHealth;
            if (attacker != SelfId && Units.TryGetValue(attacker, out var foe) && foe.Kind != UnitKind.Player)
            {
                Attackers.Add(attacker);
                foe.InFightWithMe = true;
            }
        }
        else if (Units.TryGetValue(target, out var unit))
        {
            unit.Health = finalHealth;
            unit.InFightWithMe |= attacker == SelfId;
        }

        if (Units.TryGetValue(attacker, out var striker)) striker.Targeting = target;
        if (finalHealth <= 0 && attacker == SelfId) KilledByMe.Add(target);
    }

    private void Died(string id)
    {
        Attackers.Remove(id);
        if (AttackingId == id) AttackingId = null;
        if (id == SelfId)
        {
            Dead = true;
            Health = 0;
            return;
        }

        if (Units.TryGetValue(id, out var unit))
        {
            unit.Dead = true;
            unit.Health = 0;
            if (unit.InFightWithMe) KilledByMe.Add(id);
        }
    }

    private void ApplySelfValues(Dictionary<string, int> changed)
    {
        foreach (var (key, value) in changed)
        {
            switch (key)
            {
                case WireCodes.UnitValue.Level: Level = value; break;
                case WireCodes.UnitValue.Health: Health = value; Dead = value <= 0; break;
                case WireCodes.UnitValue.MaxHealth: MaxHealth = value; break;
                case WireCodes.UnitValue.Power: Power = value; break;
                case WireCodes.UnitValue.MaxPower: MaxPower = value; break;
                case WireCodes.UnitValue.Gold: Gold = value; break;
            }
        }
    }

    private static void ApplyUnitValues(SeenUnit unit, Dictionary<string, int> changed)
    {
        if (changed.TryGetValue(WireCodes.UnitValue.Health, out var health))
        {
            unit.Health = health;
            unit.Dead = health <= 0;
        }

        if (changed.TryGetValue(WireCodes.UnitValue.MaxHealth, out var max)) unit.MaxHealth = max;
        if (changed.TryGetValue(WireCodes.UnitValue.Level, out var level)) unit.Level = level;
    }

    private static QuestState QuestFrom(uint id, bool ready, IReadOnlyList<QuestObjectivePayload> objectives,
        IReadOnlyList<QuestZonePayload>? guides, QuestZonePayload? handIn) =>
        new(id, ready,
            [.. objectives.Select(o => new ObjectiveState(o.Index, Math.Max(o.Killed, Math.Max(o.Held, o.Triggered)), o.GoalCount))],
            [.. (guides ?? []).Select(Zone)],
            handIn is null ? null : Zone(handIn));

    private static QuestZone Zone(QuestZonePayload zone) => new(zone.MapId, new Position(zone.X, zone.Y, zone.Z), zone.Radius);

    private static T Read<T>(JsonElement payload) => WebMessageJson.DeserializePayload<T>(payload);

    private static Position At(WirePosition position) => new((float)position.X, (float)position.Y, (float)position.Z);

    public static string Id(WebNetworkId id) =>
        id.IsNumber ? id.Number.ToString(CultureInfo.InvariantCulture) : id.Text ?? "";

    private static string Id(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Number => element.GetInt64().ToString(CultureInfo.InvariantCulture),
        _ => "",
    };

    public bool IsHostileCreature(SeenUnit unit) =>
        unit is { Kind: UnitKind.Creature, Dead: false, OffersQuests: false, NpcFlags: 0 }
        && (unit.Faction ?? UnitFactionTypes.Hostile) == UnitFactionTypes.Hostile;
}
