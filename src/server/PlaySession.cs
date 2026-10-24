using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Scripts;
using Ubiquitous.World;
using AuctionStore = Ubiquitous.Common.IAuctionStore;
using DomainPosition = Ubiquitous.Common.Position;
using FriendStore = Ubiquitous.Common.IFriendStore;
using GuildStore = Ubiquitous.Common.IGuildStore;
using InventoryStore = Ubiquitous.Common.IInventoryStore;
using MailStore = Ubiquitous.Common.IMailStore;
using QuestLogStore = Ubiquitous.Common.IQuestLogStore;
using ReportStore = Ubiquitous.Common.IReportStore;

namespace Ubiquitous.Server;

public sealed partial class PlaySession : ISessionPlayer
{
    private readonly IAccountAuthenticator _accounts;
    private readonly ICharacterStore _characters;
    private readonly SessionOutbox _outbox;
    private bool _loginAttempted;
    private int _failedLogins;
    private const int MaxFailedLogins = 5;
    private AccountRecord? _account;
    private CharacterRecord? _character;
    private readonly IReadOnlyList<CreatureSpawnDefinition> _creatureSpawns;
    private readonly IReadOnlyList<StaticObjectSpawnDefinition> _staticObjectSpawns;
    private readonly TargetingService _targeting = new();
    private readonly GossipService _gossip = new();
    private readonly QuestService _quests = new();
    private readonly QuestListService _questLists = new();
    private readonly VendorService _vendors = new();
    private readonly CombatService _combat;
    private readonly SpellService _spells;
    private readonly AuraService _auras = new();
    private readonly PeriodicTriggerService _periodicTrigger = new();
    private readonly ControlEffectService _control = new();
    private readonly SlowEffectService _slow = new();
    private readonly SpeedupEffectService _speedup = new();
    private readonly ControlEffectService _silence = new();
    private readonly SpeedupEffectService _meleeAttackSpeed = new();
    private readonly SpeedupEffectService _rangeAttackSpeed = new();
    private readonly ChillEffectService _chill = new();
    private readonly SpellFlightService _spellFlights = new();
    private readonly AbsorbEffectService _absorb = new();
    private readonly StealthEffectService _stealth = new();
    private readonly Dictionary<string, BodyStates> _auraStateSent = new(StringComparer.Ordinal);
    private readonly DirtyValueTracker _dirty = new();
    private readonly AttributeModifierService _attributeModifiers = new();
    private static readonly TimeSpan StuckRescueCooldown = TimeSpan.FromMinutes(15);
    private DateTimeOffset? _lastRescueAt;
    private readonly Dictionary<int, (uint Id, int Type)> _actionBar = new();
    private readonly TeleportService _teleports = new();
    private readonly TravelService _travel;
    private readonly InventoryStore? _inventoryStore;
    private readonly IProgressStore? _progressStore;
    private long _nextAutosaveTicks = long.MaxValue;
    private readonly QuestLogStore? _questLogStore;
    private readonly EmoteService _emotes = new();
    private readonly ChatHandler _chatHandler;
    private readonly EmoteHandler _emoteHandler;
    private readonly ReportHandler _reportHandler;
    private readonly SocialHandler _socialHandler;
    private readonly GuildHandler _guildHandler;
    private readonly PartyHandler _partyHandler;
    private readonly NavigationHandler _navigationHandler;

    private readonly PatrolHandler _patrolHandler;

    private readonly PatrolRouteLibrary _patrols;
    private readonly TimeProvider _time;
    private readonly OnlineCharacters _online;
    private string? _claimedCharacterGuid;
    private readonly MailHandler _mailHandler;
    private readonly SummonHandler _summonHandler;
    private readonly AuctionHandler _auctionHandler;
    private readonly TradeHandler _tradeHandler;
    private readonly WalkableGroundSource? _walkableGround;
    private readonly VisibleCreatureSet _sight = new();
    private readonly Dictionary<string, uint> _visibleStaticObjectEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DomainPosition> _staticObjectPositions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _despawnDue = new(StringComparer.Ordinal);
    private readonly Dictionary<int, DateTimeOffset> _itemCooldownUntil = new();
    private static readonly TimeSpan RespawnDelay = TimeSpan.FromSeconds(30);
    private readonly SharedMapRegistry _sharedMaps;
    private SharedMapState? _shared;
    private readonly Dictionary<uint, CreatureSpawnDefinition> _spawnById;

    private readonly HashSet<string> _engagedByMe = new(StringComparer.Ordinal);

    private void HoldEngaged(string wireId)
    {
        if (_shared is null || !_engagedByMe.Add(wireId)) return;
        _shared.MarkEngaged(wireId);
        if (_shared.Settle(wireId, Now) is { } settled) _settled.Add((wireId, settled));
    }

    // Creatures stopped mid-walk by a fight, told to every client so each stops them in the same place.
    private readonly List<(string WireId, DomainPosition At)> _settled = [];

    private void AnnounceSettled(List<string> responses)
    {
        foreach (var (wireId, at) in _settled)
        {
            responses.Add(Reply(WebMessageTypes.EntityMoved, new EntityMovedPayload(
                WebNetworkId.FromString(wireId), new Position(at.X, at.Y, at.Z), Moving: false)));
        }

        _settled.Clear();
    }

    private void ReleaseEngaged(string wireId)
    {
        if (_shared is null || !_engagedByMe.Remove(wireId)) return;
        _shared.MarkDisengaged(wireId);
    }

    private void ReleaseAllEngaged()
    {
        foreach (var wireId in _engagedByMe.ToArray()) ReleaseEngaged(wireId);
    }

    private int _summonCursor;
    private readonly CreatureChaseService _chase;
    private readonly Dictionary<string, string> _helperFights = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _announcedMobVictims = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CorpseLoot> _loot = new(StringComparer.Ordinal);
    private readonly LootService _lootService = new();
    private readonly GameContent _content;
    private readonly Dictionary<uint, List<VendorTab>> _vendorTabs;
    private int _level;
    private int _xp;
    private readonly PlayerBelongings _belongings;
    private const double LootRange = InteractionRange.Max;
    private const double MeleeRange = 3.0;
    private const int DefaultAggroRange = 10;
    private readonly Dictionary<string, DateTimeOffset> _outOfMeleeRangeSince = new(StringComparer.Ordinal);
    private bool _awaitingArrivalMove;

    private DateTimeOffset _arrivedAt;

    private static readonly TimeSpan ArrivalSettleWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RangedAttackInterval = CombatService.SwingInterval;
    private bool _playerMoving;
    private WorldSessionState? _world;

    private readonly bool _developmentEntry;
    private readonly IReadOnlySet<string> _developmentUserIds;
    private readonly ILogger _logger;

    private string PlayerUnitId => $"player:{_character?.Guid}";

    private PlaySessionHub? _hub;

    public void AttachHub(PlaySessionHub hub) => _hub = hub;

    public Ubiquitous.Common.Position? PlayerPosition => _world?.PlayerPosition;

    public bool IsAlive => _character is not null && !_combat.IsPlayerDead;

    public bool IsChatChannelJoined(string channel) => _chatHandler.IsJoined(channel);

    CharacterRecord? ISessionPlayer.Character => _character;
    string ISessionPlayer.UnitId => PlayerUnitId;
    WorldSessionState? ISessionPlayer.World => _world;
    PlaySessionHub? ISessionPlayer.Hub => _hub;
    bool ISessionPlayer.IsAdmin => _account?.IsAdmin == true;
    bool ISessionPlayer.Sees(PlaySession other) => !other.HidesFrom(this);

    bool ISessionPlayer.InReachOfStaticObject(string networkId, IReadOnlySet<uint> entries) =>
        _visibleStaticObjectEntries.TryGetValue(networkId, out var entry)
        && entries.Contains(entry)
        && InRangeOf(networkId, InteractionRange.Max, _staticObjectPositions);

    public bool IsInParty => _partyHandler.IsInParty;

    internal PlayerBelongings Belongings => _belongings;

    internal string UnitId => PlayerUnitId;

    public IReadOnlyList<string> PartyMemberIdsExcludingSelf() => _partyHandler.MemberIdsExcludingSelf();

    public Task GoOfflineInPartyAsync() => _partyHandler.GoOfflineAsync();

    public PlaySession(
        SessionStores stores,
        ServerContent content,
        SharedWorld world,
        PlaySessionOptions? options = null,
        ILogger? logger = null)
    {
        options ??= new PlaySessionOptions();
        _outbox = new SessionOutbox(ReportToAuraBoard);
        _logger = logger ?? NullLogger.Instance;
        _developmentEntry = options.DevelopmentEntry;
        _developmentUserIds = options.DevelopmentUserIds ?? new HashSet<string>();
        _accounts = stores.Accounts;
        _characters = stores.Characters;
        _inventoryStore = stores.Inventory;
        _progressStore = stores.Progress;
        _questLogStore = stores.QuestLog;

        _content = content.Game;
        _belongings = new PlayerBelongings(_content);
        _creatureSpawns = content.CreatureSpawns;
        _staticObjectSpawns = content.StaticObjectSpawns;
        _walkableGround = content.WalkableGround;
        _spawnById = new Dictionary<uint, CreatureSpawnDefinition>();
        foreach (var spawn in _creatureSpawns) _spawnById[spawn.Id] = spawn;

        _sharedMaps = world.Maps;
        _duels = world.Duels;
        _pvpFlags = world.PvpFlags;
        _blows = world.Blows;
        _ambient = world.Ambient;
        _instances = world.Instances;
        _online = world.Online;
        _time = world.Time;

        _scripts = new ScriptRegistry(_content.Dungeons, _content.CreatureScripts);
        _travel = new TravelService(_content.Stops, _content.Routes);
        _spells = new SpellService(_content.StartingSpells, options.SpellRoll);
        _combat = new CombatService(rollUnit: options.CombatRoll, time: world.Time);
        _combat.SparedBy = attackerId =>
            _character is not null && _duels.IsActiveOpponent(PlayerUnitId, attackerId);
        _combat.AbsorbIncoming = (damage, at) =>
            _character is null ? damage : _absorb.Absorb(PlayerUnitId, damage, at);
        _combat.Evading = IsReturningHome;
        _combat.CannotAct = (unitId, at) => _control.IsControlled(unitId, at);
        _combat.InActiveDuel = () =>
            _character is not null && _duels.OpponentOf(PlayerUnitId) is not null;
        _combat.UseTargetStats(CombatStatsFor);
        _chase = new CreatureChaseService(ChaseRoute);

        _chatHandler = new ChatHandler(this, _outbox);
        _emoteHandler = new EmoteHandler(this, _outbox, _emotes);
        _reportHandler = new ReportHandler(this, _outbox, _characters, stores.Report ?? new InMemoryReportStore());
        _socialHandler = new SocialHandler(this, _outbox, _characters, stores.Friend ?? new InMemoryFriendStore());
        _partyHandler = new PartyHandler(this, _outbox, world.Parties);
        _navigationHandler = new NavigationHandler(this, _outbox, _walkableGround);
        _patrols = content.Patrols;
        _patrolHandler = new PatrolHandler(this, _outbox, _patrols);
        _mailHandler = new MailHandler(this, _outbox, _belongings, _content, _characters, world.Mail);
        _summonHandler = new SummonHandler(this, _outbox, _content, world.Parties);
        _auctionHandler = new AuctionHandler(this, _outbox, _belongings, _content, world.Auctions);
        _tradeHandler = new TradeHandler(this, _outbox, _belongings, world.Trades);
        _guildHandler = new GuildHandler(this, _outbox, world.Guilds);
        _vendorTabs = _content.VendorTabs.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        _belongings.Currency = _content.StartingCurrency;
        for (var index = 0; index < _content.StartingItems.Count; index++)
        {
            var id = index == 0 ? "starter-item" : $"starter-item-{index}";
            var stack = _content.StartingItems[index];
            _belongings.Items.Seed(new InventoryItemState(id, (int)stack.Entry, stack.Count, index));
        }

        foreach (var button in _content.StartingActionBar)
        {
            _actionBar[button.Slot] = (button.SpellId, 0);
        }
    }

    public async Task LoadInventoryAsync(CancellationToken cancellationToken = default)
    {
        if (_character is not null && _inventoryStore is not null)
            _belongings.Items.Load(await _inventoryStore.ListAsync(_character.Guid, cancellationToken));
    }

    public async Task SaveInventoryAsync(CancellationToken cancellationToken = default)
    {
        _savedProgress = null;
        if (_character is not null && _inventoryStore is not null)
            await _inventoryStore.SaveAsync(_character.Guid, _belongings.Items.ToPersisted(), cancellationToken);
    }

    public async Task LoadQuestLogAsync(CancellationToken cancellationToken = default)
    {
        if (_character is null || _questLogStore is null) return;
        var log = await _questLogStore.GetAsync(_character.Guid, cancellationToken);
        _quests.Load(log.Accepted, log.Completed);
    }

    public async Task SaveQuestLogAsync(CancellationToken cancellationToken = default)
    {
        _savedProgress = null;
        if (_character is not null && _questLogStore is not null)
            await _questLogStore.SaveAsync(_character.Guid, _quests.ToPersisted(), cancellationToken);
    }

    public async Task SaveCharacterAsync(CancellationToken cancellationToken = default)
    {
        _savedProgress = null;
        if (_character is null) return;
        await _characters.UpdateAsync(CharacterSnapshot(_character), cancellationToken);
    }

    private CharacterRecord CharacterSnapshot(CharacterRecord character)
    {
        var position = _world?.PlayerPosition ?? new DomainPosition(character.X, character.Y, character.Z);
        var mapId = _world?.MapId ?? character.MapId;

        if (_combat.IsPlayerDead && _world is not null)
        {
            var grave = GraveyardLookup.Resolve(_content, _world.MapId, _world.PlayerPosition);
            mapId = grave.MapId;
            position = new DomainPosition(grave.Position.X, grave.Position.Y, grave.Position.Z);
        }

        return character with
        {
            MapId = mapId,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Level = _level,
            Health = Math.Max(1, _combat.PlayerHealth),
            Xp = _xp,
            Currency = _belongings.Currency,
            BagSlots = _belongings.BagSlots,
            BankSlots = _belongings.BankSlots,
            TalentRanks = _talents.Ranks.ToDictionary(),
            LearnedSpells = _learnedSpells.Count == 0 ? null : [.. _learnedSpells],
            ActionBar = [.. _actionBar.Select(button => new PersistedActionButton(button.Key, button.Value.Id, button.Value.Type))],
        };
    }

    public static readonly TimeSpan AutosaveInterval = TimeSpan.FromMinutes(5);

    public bool IsAutosaveDue(DateTimeOffset now) => now.UtcTicks >= Volatile.Read(ref _nextAutosaveTicks);

    public async Task AutosaveAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (_character is null || !IsAutosaveDue(now)) return;
        Volatile.Write(ref _nextAutosaveTicks, (now + AutosaveInterval).UtcTicks);
        try
        {
            await SaveProgressAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Periodic save failed; it will be tried again in {Interval}", AutosaveInterval);
        }
    }

    // What the last whole save wrote; a save of the same again is skipped, and
    // a save of any one part forgets it so a skip never leaves the store behind.
    private string? _savedProgress;

    public async Task SaveProgressAsync(CancellationToken cancellationToken = default)
    {
        if (_character is null) return;
        var character = CharacterSnapshot(_character);
        var items = _belongings.Items.ToPersisted();
        var quests = _quests.ToPersisted();
        var progress = ProgressFingerprint(character, items, quests);
        if (string.Equals(progress, _savedProgress, StringComparison.Ordinal)) return;

        if (_progressStore is not null)
            await _progressStore.SaveAsync(character, items, quests, cancellationToken);
        else
        {
            await SaveInventoryAsync(cancellationToken);
            await SaveQuestLogAsync(cancellationToken);
            await SaveCharacterAsync(cancellationToken);
        }
        _savedProgress = progress;
    }

    private static string ProgressFingerprint(object character, object items, object quests) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new { character, items, quests })));

    public DateTimeOffset Now => _time.GetUtcNow();

    public string RefuseRateLimited() => Error(WireCodes.ProtocolError.RateLimited, "Too many messages; slow down.");

    public void LeaveWorld()
    {
        if (_claimedCharacterGuid is not null)
        {
            _online.Release(_claimedCharacterGuid);
            _claimedCharacterGuid = null;
        }

        LeaveInstanceIfAny();
        LeaveDuelIfAny();
        ReleaseSharedCombat();
        ClearStressBots(null);
        ForgetReportedAuras();
        Volatile.Write(ref _nextAutosaveTicks, long.MaxValue);
    }

    public async Task<IReadOnlyList<string>> HandleTextAsync(string text, CancellationToken cancellationToken = default)
    {
        WebMessageEnvelope envelope;
        try
        {
            envelope = WebMessageJson.Deserialize(text);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return [Error(WireCodes.ProtocolError.Malformed, "Malformed request.")];
        }

        if (RefuseOutOfReach(envelope.Type, envelope.Payload) is string refusal)
            return refusal.Length == 0 ? [] : [refusal];

        try
        {
            return envelope.Type switch
            {
                WebMessageTypes.SessionPing => [Reply(WebMessageTypes.SessionPong, new { })],
                WebMessageTypes.AccountLogin => [await LoginAsync(envelope.Payload, cancellationToken)],
                WebMessageTypes.AccountLogout => [Logout()],
                WebMessageTypes.DevEnterWorld => await DevEnterWorldAsync(envelope.Payload, cancellationToken),
                WebMessageTypes.CharactersList => [await ListCharactersAsync(cancellationToken)],
                WebMessageTypes.CharactersCreate => await CreateCharacterAsync(envelope.Payload, cancellationToken),
                WebMessageTypes.CharactersDelete => await DeleteCharacterAsync(envelope.Payload, cancellationToken),
                WebMessageTypes.CharactersOutfit => [Outfit(envelope.Payload)],
                WebMessageTypes.CharactersChoose => await ChooseCharacterAsync(envelope.Payload, cancellationToken),
                WebMessageTypes.CharactersReturn => [ReturnToCharacterList()],
                WebMessageTypes.PlayerMove => await MovePlayerAsync(envelope.Payload, cancellationToken),
                WebMessageTypes.PlayerChat => [_chatHandler.Say(envelope.Payload, IsInParty)],
                WebMessageTypes.TargetChange => [Target(envelope.Payload)],
                WebMessageTypes.NpcTalk => [NpcTalk(envelope.Payload)],
                WebMessageTypes.NpcPreviousPage => [NpcPreviousPage(envelope.Payload)],
                WebMessageTypes.NpcPickOption => NpcPickOption(envelope.Payload),
                WebMessageTypes.QuestCheckMark => [QuestCheckMark(envelope.Payload)],
                WebMessageTypes.QuestRefreshStatuses => [QuestRefreshStatuses()],
                WebMessageTypes.QuestList => [QuestList(envelope.Payload)],
                WebMessageTypes.QuestAccept => [QuestAccept(envelope.Payload)],
                WebMessageTypes.QuestComplete => QuestComplete(envelope.Payload),
                WebMessageTypes.QuestDestroy => [QuestDestroy(envelope.Payload)],
                WebMessageTypes.AuctioneerOpen => [AuctioneerOpen(envelope.Payload)],
                WebMessageTypes.VendorOpen => [VendorOpen(envelope.Payload)],
                WebMessageTypes.VendorBuy => VendorBuy(envelope.Payload),
                WebMessageTypes.VendorSell => VendorSell(envelope.Payload),
                WebMessageTypes.AttackEngage => AttackEngage(envelope.Payload),
                WebMessageTypes.PlayerUnstuck => PlayerUnstuck(),
                WebMessageTypes.LootOpen => LootOpen(envelope.Payload),
                WebMessageTypes.LootClose => LootClose(envelope.Payload),
                WebMessageTypes.LootTake => LootTake(envelope.Payload),
                WebMessageTypes.LootTakeMoney => LootTakeMoney(envelope.Payload),
                WebMessageTypes.StaticObjectInteract => StaticObjectInteract(envelope.Payload),
                WebMessageTypes.NpcTeleportAsk => Teleport(envelope.Payload),
                WebMessageTypes.BusRequest => BusRequest(envelope.Payload),
                WebMessageTypes.BusConfirm => BusConfirm(envelope.Payload),
                WebMessageTypes.AdminTeleport => AdminTeleport(envelope.Payload),
                WebMessageTypes.AdminGiveItem => AdminGiveItem(envelope.Payload),
                WebMessageTypes.AdminGiveGold => AdminGiveGold(envelope.Payload),
                WebMessageTypes.AdminSetLevel => AdminSetLevel(envelope.Payload),
                WebMessageTypes.AdminLearnSpells => AdminLearnSpells(),
                WebMessageTypes.DevStressTest => StressTest(envelope.Payload),
                WebMessageTypes.InventoryMove => InventoryMove(envelope.Payload),
                WebMessageTypes.InventoryEquip => InventoryEquip(envelope.Payload),
                WebMessageTypes.InventoryUse => InventoryUse(envelope.Payload, Now),
                WebMessageTypes.InventoryDestroy => InventoryDestroy(envelope.Payload),
                WebMessageTypes.InventorySplit => InventorySplit(envelope.Payload),
                WebMessageTypes.InventorySort => InventorySort(envelope.Payload),
                WebMessageTypes.InventoryOpenBank => InventoryOpenBank(envelope.Payload),
                WebMessageTypes.InventoryExpand => await InventoryUpgradeStorageAsync(envelope.Payload, cancellationToken),
                WebMessageTypes.SpellRequest => SpellRequest(envelope.Payload, Now),
                WebMessageTypes.SpellAbort => [SpellAbort(envelope.Payload)],
                WebMessageTypes.MountDismount => MountDismount(Now),
                WebMessageTypes.TalentLearn => await TalentLearnAsync(envelope.Payload, cancellationToken),
                WebMessageTypes.TalentReset => await TalentResetAsync(cancellationToken),
                WebMessageTypes.ActionBarAssign => [ActionBarAssign(envelope.Payload)],
                WebMessageTypes.PlayerEmote => _emoteHandler.Perform(envelope.Payload),
                WebMessageTypes.ChatJoinChannel => [_chatHandler.Join(envelope.Payload)],
                WebMessageTypes.ChatLeaveChannel => [_chatHandler.Leave(envelope.Payload)],
                WebMessageTypes.ChatWhisper => await _chatHandler.Whisper(envelope.Payload),
                WebMessageTypes.SocialFriendAdd => [await _socialHandler.AddFriend(envelope.Payload)],
                WebMessageTypes.SocialFriendRemove => [await _socialHandler.RemoveFriend(envelope.Payload)],
                WebMessageTypes.SocialFriendList => [await _socialHandler.List()],
                WebMessageTypes.SocialWho => [_socialHandler.Who()],
                WebMessageTypes.SocialIgnoreAdd => [await _socialHandler.AddIgnore(envelope.Payload)],
                WebMessageTypes.SocialIgnoreRemove => [await _socialHandler.RemoveIgnore(envelope.Payload)],
                WebMessageTypes.SocialReport => [await _reportHandler.Submit(envelope.Payload)],
                WebMessageTypes.DuelChallenge => DuelChallenge(envelope.Payload),
                WebMessageTypes.DuelAccept => DuelAccept(),
                WebMessageTypes.DuelDecline => DuelDecline(),
                WebMessageTypes.DuelForfeit => DuelForfeit(),
                WebMessageTypes.PvpFlagSet => PvpFlagSet(envelope.Payload),
                WebMessageTypes.TradeRequest => await _tradeHandler.Request(envelope.Payload),
                WebMessageTypes.TradeAccept => await _tradeHandler.Accept(),
                WebMessageTypes.TradeDecline => await _tradeHandler.Decline(),
                WebMessageTypes.TradeOffer => await _tradeHandler.Offer(envelope.Payload),
                WebMessageTypes.TradeConfirm => await _tradeHandler.Confirm(envelope.Payload),
                WebMessageTypes.TradeCancel => await _tradeHandler.Cancel(),
                WebMessageTypes.PartyInvite => await _partyHandler.Invite(envelope.Payload),
                WebMessageTypes.PartyAccept => await _partyHandler.Accept(),
                WebMessageTypes.PartyDecline => await _partyHandler.Decline(),
                WebMessageTypes.PartyLeave => await _partyHandler.Leave(),
                WebMessageTypes.PartyKick => await _partyHandler.Kick(envelope.Payload),
                WebMessageTypes.PartyVoteKick => _partyHandler.VoteKick(envelope.Payload),
                WebMessageTypes.PartyVoteKickAnswer => _partyHandler.AnswerVoteKick(envelope.Payload),
                WebMessageTypes.MailOpen => await _mailHandler.Open(envelope.Payload),
                WebMessageTypes.MailSend => await _mailHandler.Send(envelope.Payload),
                WebMessageTypes.MailRead => await _mailHandler.Read(envelope.Payload),
                WebMessageTypes.MailClaim => await _mailHandler.Claim(envelope.Payload),
                WebMessageTypes.MailDelete => await _mailHandler.Delete(envelope.Payload),
                WebMessageTypes.AuctionList => await _auctionHandler.List(envelope.Payload),
                WebMessageTypes.AuctionBrowse => await _auctionHandler.Browse(envelope.Payload),
                WebMessageTypes.AuctionCancel => await _auctionHandler.Cancel(envelope.Payload),
                WebMessageTypes.AuctionBid => await _auctionHandler.Bid(envelope.Payload),
                WebMessageTypes.AuctionBuyout => await _auctionHandler.Buyout(envelope.Payload),
                WebMessageTypes.GuildFound => await _guildHandler.Found(envelope.Payload),
                WebMessageTypes.GuildInvite => await _guildHandler.Invite(envelope.Payload),
                WebMessageTypes.GuildAccept => await _guildHandler.Accept(),
                WebMessageTypes.GuildDecline => await _guildHandler.Decline(),
                WebMessageTypes.GuildLeave => await _guildHandler.Leave(),
                WebMessageTypes.GuildKick => await _guildHandler.Kick(envelope.Payload),
                WebMessageTypes.GuildDisband => await _guildHandler.Disband(),
                WebMessageTypes.GuildRosterRequest => await _guildHandler.Roster(),
                WebMessageTypes.InstanceEnter => await InstanceEnter(envelope.Payload),
                WebMessageTypes.DungeonRestart => DungeonRestart(),
                WebMessageTypes.DungeonChooseMode => DungeonChooseMode(envelope.Payload),
                WebMessageTypes.NavigationRequest => _navigationHandler.Describe(envelope.Payload),
                WebMessageTypes.PatrolRequest => _patrolHandler.Request(),
                WebMessageTypes.PatrolSave => _patrolHandler.Save(envelope.Payload),
                WebMessageTypes.ShareQuest => await ShareQuest(envelope.Payload),
                WebMessageTypes.QuestShareAnswer => await QuestShareAnswer(envelope.Payload),
                WebMessageTypes.SummonOpen => _summonHandler.Open(envelope.Payload),
                WebMessageTypes.SummonRequest => await _summonHandler.Request(envelope.Payload),
                WebMessageTypes.SummonAnswer => await SummonAnswer(envelope.Payload),
                _ => [Error(WireCodes.ProtocolError.Unsupported, $"Unsupported message type '{envelope.Type}'.")],
            };
        }
        catch (JsonException)
        {
            return [Error(WireCodes.ProtocolError.InvalidPayload, "Invalid payload.")];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected failure handling {MessageType}", envelope.Type);
            return [Error(WireCodes.ProtocolError.Internal, "The server could not handle that request.")];
        }
    }

    public ushort? CurrentMapId => _world?.MapId;

    private GroundAnswer? GroundHeightAt(float x, float z, float? nearY)
    {
        if (_world is null) return null;
        if (_walkableGround?.GroundHeightNear(_world.MapId, x, z, nearY ?? 0f) is float walkable)
            return new GroundAnswer(walkable, KnowsFloors: true);
        if (_instanceWorld is not null) return null;
        return _content.Terrain?.HeightAt(_world.MapId, x, z) is float terrain
            ? new GroundAnswer(terrain, KnowsFloors: false)
            : null;
    }

    public PlayerPresence? Presence
    {
        get
        {
            if (_world is null || _character is null) return null;
            var playerId = PlayerUnitId;
            var now = Now;
            var staminaBonus = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Stamina, now);
            var wisdomBonus = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Wisdom, now);
            return new PlayerPresence(
                WebNetworkId.FromString(playerId), _world.MapId,
                new Position(_world.PlayerPosition.X, _world.PlayerPosition.Y, _world.PlayerPosition.Z), _world.PlayerYaw,
                _character.Name, _character.Level, _character.Class, _character.Race, _character.Gender, _character.DisplayInfoId,
                _character.Skin, _character.HairStyle, _character.HairColour, _character.Face, _character.Equipment,
                _combat.PlayerHealth, _combat.EffectiveMaxHealth(staminaBonus),
                _spells.Power, _spells.EffectiveMaxPower(wisdomBonus),
                AurasWornBy(playerId));
        }
    }

    public OutboundEvent? DescribeReply(string raw) => _outbox.Describe(raw);

    public string EncodeObserverEvent(OutboundEvent outbound)
    {
        if (outbound is { Type: WebMessageTypes.UnitDied, NetworkId: { } deadId }) NoteRemoteDeath(deadId);
        return WebMessageJson.Serialize(WebMessageEnvelope.Create(outbound.Type, _outbox.NextSequence(), outbound.Payload));
    }

    private string Target(JsonElement payload)
    {
        if (_world is null) return Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before targeting.");
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null) return Error(WireCodes.ProtocolError.InvalidTarget, "Network ID is required.");
        IReadOnlyCollection<TargetCandidate> candidates = PlayerAlongside(networkId)?.PlayerPosition is { } standing
            ? [.. _sight.Targets, new TargetCandidate(networkId, standing.X, standing.Y, standing.Z)]
            : _sight.Targets;
        var result = _targeting.Select(networkId, _world.PlayerPosition.X, _world.PlayerPosition.Y,
            _world.PlayerPosition.Z, candidates);
        return result.Accepted
            ? Reply(WebMessageTypes.TargetChanged,
                new TargetChangedPayload(WebNetworkId.FromString(result.NetworkId)))
            : Error(WireCodes.ProtocolError.InvalidTarget, result.Reason ?? "Target rejected.");
    }

    private string NpcTalk(JsonElement payload)
    {
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null || !_sight.Entries.TryGetValue(networkId, out var entry))
            return Error(WireCodes.ProtocolError.InvalidGossip, "NPC is not visible.");
        var result = _gossip.Open(networkId, entry,
            _content.Npcs);
        return result is null ? Error(WireCodes.ProtocolError.InvalidGossip, "NPC has no gossip menu.")
            : Reply(WebMessageTypes.Gossip, new GossipPayload(WebNetworkId.FromString(result.NetworkId), result.Entry));
    }

    private string NpcPreviousPage(JsonElement payload) => NpcTalk(payload);

    private IReadOnlyList<string> NpcPickOption(JsonElement payload)
    {
        var networkId = PayloadIds.NetworkId(payload);
        if (networkId is null || !_sight.Entries.TryGetValue(networkId, out var entry))
            return [Error(WireCodes.ProtocolError.InvalidGossip, "NPC is not visible.")];
        if (!payload.TryGetProperty("menuId", out var menuValue) || !menuValue.TryGetUInt32(out var menuId)
            || !payload.TryGetProperty("choice", out var choiceValue) || !choiceValue.TryGetInt32(out var choice))
            return [Error(WireCodes.ProtocolError.InvalidGossip, "A choice names its menu and its slot.")];

        var pick = _gossip.Pick(new GossipRequest(networkId, menuId, choice), entry, _content.Npcs, _content.GossipMenus, _content.Stops);
        if (pick is null) return [Error(WireCodes.ProtocolError.InvalidGossip, "That choice is not offered here.")];

        string Answer(string result) => Reply(WebMessageTypes.NpcChoiceResult,
            new NpcChoiceResultPayload(WebNetworkId.FromString(networkId), menuId, choice, result));

        if (pick.Kind == GossipPickKind.RunsScript)
        {
            var started = new List<string>();
            return StartRitual(networkId, pick.Script, Now, started)
                ? [Answer(WireCodes.ChoiceResult.Started), .. started]
                : [Answer(WireCodes.ChoiceResult.NothingHappens)];
        }

        if (pick.Kind != GossipPickKind.Teleport || _world is null || _character is null)
            return [Answer(WireCodes.ChoiceResult.NothingHappens)];

        var teleport = pick.Teleport!;
        if (teleport.Cost > _belongings.Currency) return [Answer(WireCodes.ChoiceResult.NotEnoughMoney)];

        var responses = new List<string> { Answer(WireCodes.ChoiceResult.Teleported) };
        if (teleport.Cost > 0)
        {
            _belongings.Currency -= teleport.Cost;
            responses.Add(Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
                new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = _belongings.Currency })));
        }

        TravelTo(responses, teleport.MapId, new DomainPosition(teleport.X, teleport.Y, teleport.Z), teleport.Yaw);
        return responses;
    }

    private IReadOnlyList<string> AttackEngage(JsonElement payload)
    {
        if (_world is null || _character is null)
            return [Error(WireCodes.ProtocolError.InvalidTarget, "Character selection is required before attacking.")];
        var attackerId = PlayerUnitId;
        if (_control.IsControlled(attackerId, Now))
            return [Error(WireCodes.ProtocolError.InvalidTarget, "You are stunned or asleep.")];
        var targetId = PayloadIds.Read(payload, "targetId");
        if (HiddenFromMe(targetId))
            return [Error(WireCodes.ProtocolError.InvalidTarget, "Attack target is not visible.")];
        var isDuelOpponent = targetId is not null && _duels.IsActiveOpponent(attackerId, targetId);
        var isPvpFlagged = targetId is not null && _pvpFlags.IsFlaggedPair(attackerId, targetId);
        if (targetId is null || (!_sight.Contains(targetId) && !isDuelOpponent && !isPvpFlagged))
            return [Error(WireCodes.ProtocolError.InvalidTarget, "Attack target is not visible.")];
        if (_combat.IsPlayerDead)
            return [Error(WireCodes.ProtocolError.InvalidTarget, "You are dead.")];
        if (_combat.IsDead(targetId))
            return [Error(WireCodes.ProtocolError.InvalidTarget, "Attack target is already dead.")];
        if (_shared is not null && !_shared.IsAlive(targetId) && _sight.Contains(targetId))
            return [Error(WireCodes.ProtocolError.InvalidTarget, "Attack target is already dead.")];
        if (!isDuelOpponent && !isPvpFlagged
            && IsFriendlyCreature(targetId))
            return [Error(WireCodes.ProtocolError.InvalidTarget, "Attack target is friendly.")];
        if (IsReturningHome(targetId))
            return [Error(WireCodes.ProtocolError.InvalidTarget, "Attack target is evading.")];
        if (!InMeleeRange(targetId)
            && !(HasRangedWeapon() && InRangeOf(targetId, CombatService.MaxRangedAttackDistance)))
            return [Error(WireCodes.ProtocolError.InvalidTarget, "Attack target is out of range.")];
        var allowedTargets = _sight.Entries.Keys.ToHashSet(StringComparer.Ordinal);
        if (isDuelOpponent || isPvpFlagged) allowedTargets.Add(targetId);
        var result = _combat.Start(attackerId, targetId, allowedTargets, Now,
            CombatStatsFor(targetId), (a, b) => _duels.IsActiveOpponent(a, b), _pvpFlags.IsFlaggedPair);
        if (result is null) return [];
        HoldEngaged(result.TargetId);
        if (_shared is not null) _shared.ClearReturning(result.TargetId);
        var responses = new List<string>();
        AnnounceSettled(responses);
        Dismount(Now, responses);
        BreakStealth(Now, responses);
        PullPackOf(result.TargetId, Now, responses);
        if (result.StoppedTargetId is { } previous)
        {
            if (string.Equals(_combat.RetaliationTarget(previous), attackerId, StringComparison.Ordinal))
            {
                NotePackJoiner(previous);
                responses.Add(Reply(WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
                    WebNetworkId.FromString(previous), WebNetworkId.FromString(attackerId))));
            }
            else
            {
                responses.Add(Reply(WebMessageTypes.AttackHalted,
                    new AttackHaltedPayload(WebNetworkId.FromString(previous))));
            }
        }
        responses.Add(Reply(WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
            WebNetworkId.FromString(result.AttackerId), WebNetworkId.FromString(result.TargetId))));
        // A creature that fights back turns on its attacker at once, not at its first swing.
        if (CreatureFor(result.TargetId) is { } struck && _sight.Contains(result.TargetId)
            && _scripts.For(struck, _instanceWorld is not null, AdvertisesQuests(struck)).FightsBack
            && string.Equals(_combat.RetaliationTarget(result.TargetId), attackerId, StringComparison.Ordinal)
            && !(_announcedMobVictims.TryGetValue(result.TargetId, out var facing) && facing == attackerId))
        {
            _announcedMobVictims[result.TargetId] = attackerId;
            responses.Add(Reply(WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
                WebNetworkId.FromString(result.TargetId), WebNetworkId.FromString(attackerId))));
        }

        if (result.EnteredCombat)
        {
            responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
                new AttackEnterCombatPayload(WebNetworkId.FromString(result.AttackerId))));
            responses.Add(Reply(WebMessageTypes.AttackEnterCombat,
                new AttackEnterCombatPayload(WebNetworkId.FromString(result.TargetId))));
        }

        return responses;
    }

    private string? StuckRescueRefusal(string playerId, DateTimeOffset now)
    {
        bool coolingDown = _lastRescueAt is { } last && now < last + StuckRescueCooldown;
        bool fighting = _combat.CurrentTargetOf(playerId) is not null;
        return coolingDown ? "That is not ready yet."
            : fighting ? "You cannot do that while in combat."
            : null;
    }

    private IReadOnlyList<string> PlayerUnstuck()
    {
        if (_world is null || _character is null) return [];
        var playerId = PlayerUnitId;
        var now = Now;
        bool wasDead = _combat.IsPlayerDead;

        if (!wasDead)
        {
            if (StuckRescueRefusal(playerId, now) is { } reason) return [Error(WireCodes.ProtocolError.UnstuckRefused, reason)];
            _lastRescueAt = now;
        }

        var responses = new List<string>();
        int health = 0, maxHealth = 0, power = 0, maxPower = 0;
        if (wasDead)
        {
            var staminaBonus = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Stamina, now);
            var wisdomBonus = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Wisdom, now);
            var healed = _combat.Revive(staminaBonus);
            _combat.ClearDeath(playerId);
            _spells.RestoreFullPower(wisdomBonus);
            if (healed is not null)
            {
                health = healed.FinalHealth;
                maxHealth = healed.MaxHealth;
            }
            power = _spells.Power;
            maxPower = _spells.EffectiveMaxPower(wisdomBonus);
        }

        UnmarkEngaged();
        var stopped = _combat.Disengage(playerId);
        if (stopped is not null)
        {
            responses.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(WebNetworkId.FromString(stopped))));
            responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(WebNetworkId.FromString(playerId))));
        }

        var destination = GraveyardLookup.Resolve(_content, _world.MapId, _world.PlayerPosition);
        var mapId = destination.MapId;
        responses.AddRange(EnterWorld(mapId, destination.Position, destination.Yaw));

        if (wasDead)
        {
            responses.Add(Reply(WebMessageTypes.PlayerRevived, new PlayerRevivedPayload(
                WebNetworkId.FromString(playerId), health, maxHealth, power, maxPower, mapId,
                new Position(destination.Position.X, destination.Position.Y, destination.Position.Z),
                destination.Yaw)));
        }

        return responses;
    }

    private static double CastRangeOf(SpellDefinition spell) => Math.Max(spell.MaxRange, MeleeRange);

    private bool TryPositionOf(string networkId, out DomainPosition position)
    {
        if (_shared is not null && _shared.TryGetPosition(networkId, out position)) return true;
        if (networkId.StartsWith("player:", StringComparison.Ordinal)
            && _hub?.FindByCharacterNetworkId(WebNetworkId.FromString(networkId))?.PlayerPosition is { } standing)
        {
            position = standing;
            return true;
        }
        position = default;
        return false;
    }

    private bool InRangeOf(string networkId, double range, IReadOnlyDictionary<string, DomainPosition>? positions = null)
    {
        DomainPosition target;
        if (positions is not null)
        {
            if (!positions.TryGetValue(networkId, out target)) return false;
        }
        else if (!TryPositionOf(networkId, out target)) return false;
        if (_world is null) return false;
        var player = _world.PlayerPosition;
        var distance = Math.Sqrt(
            Math.Pow(player.X - target.X, 2) + Math.Pow(player.Y - target.Y, 2) + Math.Pow(player.Z - target.Z, 2));
        return distance <= range;
    }

    public bool NeedsFastTick
    {
        get
        {
            if (_character is null || _world is null) return false;
            if (_combat.IsPlayerDead) return true;
            var playerId = PlayerUnitId;
            return _combat.CurrentTargetOf(playerId) is not null || _helperFights.Count > 0
                || _duels.OpponentOf(playerId) is not null || _blows.HasPending(playerId) || _gathering.InProgress;
        }
    }

    private void MarkUnitValues(string unitId, IReadOnlyDictionary<string, int> changed, DateTimeOffset now) =>
        _dirty.MarkAll(unitId, changed, now);

    private void FlushUnitValues(DateTimeOffset now, List<string> responses)
    {
        var playerId = _character is null ? null : PlayerUnitId;
        foreach (var (unitId, changed) in _dirty.FlushDue(now))
        {
            if (_combat.IsDead(unitId)) continue;
            if (_combat.IsPlayerDead && string.Equals(unitId, playerId, StringComparison.Ordinal)) continue;
            var copy = new Dictionary<string, int>(changed);
            if (string.Equals(unitId, playerId, StringComparison.Ordinal))
                responses.Add(Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(copy)));
            responses.Add(Reply(WebMessageTypes.EntityValuesChanged,
                new EntityValuesChangedPayload(WebNetworkId.FromString(unitId), copy)));
        }
    }

    public void TickWorld(DateTimeOffset now) => _shared?.TickWorld(now);

    public IReadOnlyList<string> TickCombat(DateTimeOffset now)
    {
        if (_world is null || _character is null) return [];
        var playerId = PlayerUnitId;
        var responses = new List<string>();
        if ((_combat.IsPlayerDead || _combat.IsDead(playerId))
            && _combat.CurrentTargetOf(playerId) is string dyingTarget)
        {
            UnmarkEngaged();
            _combat.Disengage(playerId);
            StandDownPack(responses);
            foreach (var guardId in _helperFights.Keys.ToArray())
                StandDownGuard(guardId, responses);
            if (_shared is not null && !_shared.IsEngaged(dyingTarget))
            {
                MarkReturning(dyingTarget);
                FaceHome(dyingTarget, responses);
            }
        }
        if (_pendingLeaveTarget is { } leftTarget)
        {
            _pendingLeaveTarget = null;
            LeaveDeadTarget(leftTarget, responses);
        }
        var fightTarget = _combat.CurrentTargetOf(playerId);
        if (fightTarget is null) { _fightStartedAt = null; _fightTarget = null; }
        else if (!string.Equals(fightTarget, _fightTarget, StringComparison.Ordinal))
        {
            _fightTarget = fightTarget;
            _fightStartedAt = now;
        }
        if (_shared is not null)
        {
            if (_combat.CurrentTargetOf(playerId) is { } ownTarget) _shared.AddFighter(ownTarget, playerId);
            foreach (var joinerId in _packJoiners) _shared.AddFighter(joinerId, playerId);
            foreach (var (guardId, mobId) in _helperFights) _shared.AddFighter(mobId, guardId);
        }
        DropHiddenTarget(responses);
        TickDespawns(now, responses);
        TickRespawns(now, responses);
        TickMiningView(responses);
        TickGathering(now, responses);
        TickAggro(now, responses);
        TickLeash(now, responses);
        TickChase(now, responses);
        DrainMapMovements(responses);
        TickGuardAssist(now, responses);
        TickPackAssist(now, responses);
        TickHunts(now, responses);
        TickStance(now, responses);
        TickCreatureScripts(now, responses);
        TickIdleMovement(now, responses);
        TickStressBots(now, responses);
        responses.AddRange(TickAuras(now));
        responses.AddRange(TickPeriodicTriggers(now));
        responses.AddRange(TickControlEffects(now));
        responses.AddRange(TickSilenceEffects(now));
        responses.AddRange(TickAttributeModifiers(now));
        responses.AddRange(OnceEach(TickSlowEffects(now).Concat(TickChillEffects(now))));
        responses.AddRange(TickSpeedupEffects(now));
        responses.AddRange(TickMeleeAttackSpeedEffects(now));
        responses.AddRange(TickRangeAttackSpeedEffects(now));
        responses.AddRange(TickStealthEffects(now));
        responses.AddRange(TickAbsorbEffects(now));
        responses.AddRange(TickDuelNotifications(now));
        _partyHandler.Tick(now);
        responses.AddRange(_partyHandler.DrainNotices());
        int playerArmor = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Armor, now);
        int playerStrength = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Strength, now);
        int playerStamina = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Stamina, now);
        int playerAgility = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Agility, now);
        int playerCritRating = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.CriticalStrike, now);
        int playerParryRating = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.ParryRating, now);
        int playerBlockRating = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.BlockRating, now);
        int playerHitRating = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.HitRating, now);
        int playerDodgeRating = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.DodgeRating, now);
        int playerMeleeAttackSpeedPercent = _meleeAttackSpeed.CurrentPercent(playerId, now);
        var swings = _combat.Tick(
            now, playerArmor, playerStrength, playerStamina, playerAgility, playerCritRating,
            playerParryRating, playerBlockRating, playerHitRating, playerMeleeAttackSpeedPercent, playerDodgeRating);
        AppendCombatEntries(responses);
        bool struckOut = swings.Any(swing => string.Equals(swing.AttackerId, playerId, StringComparison.Ordinal));
        foreach (var swing in swings)
        {
            if (swing.OtherPlayer)
            {
                if (!_control.IsControlled(playerId, now))
                    HandOverBlow(swing, swing.Ranged ? PlayerBlowKind.Shot : PlayerBlowKind.Swing);
                continue;
            }
            var target = WebNetworkId.FromString(swing.TargetId);
            var attacker = WebNetworkId.FromString(swing.AttackerId);
            if (!string.Equals(swing.AttackerId, playerId, StringComparison.Ordinal)
                && !_helperFights.ContainsKey(swing.AttackerId)
                && !_hunts.ContainsKey(swing.AttackerId)
                && !swing.AttackerId.StartsWith("player:", StringComparison.Ordinal)
                && _sight.Contains(swing.AttackerId))
            {
                if (!_announcedMobVictims.TryGetValue(swing.AttackerId, out var announced)
                    || !string.Equals(announced, swing.TargetId, StringComparison.Ordinal))
                {
                    _announcedMobVictims[swing.AttackerId] = swing.TargetId;
                    responses.Add(Reply(WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(attacker, target)));
                }
            }
            responses.Add(swing.Ranged
                ? Reply(WebMessageTypes.AttackShotResult, new AttackShotResultPayload(
                    attacker, target, WireOutcome(swing.Result), swing.Damage, swing.FinalHealth, WireAbsorbed(swing)))
                : Reply(WebMessageTypes.AttackSwingResult, new AttackSwingResultPayload(
                    attacker, target, WireOutcome(swing.Result), swing.Damage, swing.FinalHealth,
                    swing.OffHand ? WireCodes.Hand.Off : WireCodes.Hand.Main, WireAbsorbed(swing))));
            if (string.Equals(swing.TargetId, playerId, StringComparison.Ordinal))
            {
                MarkUnitValues(swing.TargetId, new Dictionary<string, int> { [WireCodes.UnitValue.Health] = swing.FinalHealth, [WireCodes.UnitValue.MaxHealth] = swing.MaxHealth }, now);
                if (swing.Spared)
                {
                    responses.AddRange(EndDuelAsLoser(playerId));
                    continue;
                }
                if (!swing.Killed) continue;
                DieTo(swing.AttackerId, responses);
                continue;
            }

            MarkUnitValues(swing.TargetId, new Dictionary<string, int> { [WireCodes.UnitValue.Health] = swing.FinalHealth, [WireCodes.UnitValue.MaxHealth] = swing.MaxHealth }, now);
            if (!swing.Killed && swing.SpellId != 0 && string.Equals(swing.AttackerId, playerId, StringComparison.Ordinal)
                && _content.Spells.TryGetValue(swing.SpellId, out var shotSpell))
                LandSpellShot(attacker, swing, shotSpell, now, responses);
            if (!swing.Killed) continue;

            if (LandHuntKill(swing, now, responses)) continue;

            if (_helperFights.TryGetValue(swing.TargetId, out var helpedMob))
            {
                _combat.Disengage(swing.TargetId);
                ReleaseEngaged(helpedMob);
                _helperFights.Remove(swing.TargetId);
                if (_sight.SpawnIds.TryGetValue(swing.TargetId, out var fallenSpawn)) _shared?.Kill(fallenSpawn, now);
                HoldCorpse(swing.TargetId, now, carriesLoot: false);
                responses.Add(Reply(WebMessageTypes.UnitDied, DeathOf(target)));
                responses.Add(Reply(WebMessageTypes.AttackExitCombat,
                    new AttackExitCombatPayload(WebNetworkId.FromString(swing.TargetId))));
                continue;
            }

            if (_sight.Entries.TryGetValue(swing.TargetId, out var victimEntry))
            {
                var creature = _content.Creatures.TryGetValue(victimEntry, out var found) ? found : null;
                var lootTable = _content.LootTables.TryGetValue(victimEntry, out var table) ? table : null;
                _loot[swing.TargetId] = _lootService.Generate(
                    checked((int)victimEntry), lootTable, creature?.MoneyDropMin, creature?.MoneyDropMax);
                foreach (var update in _quests.RecordKill(victimEntry, _content.Quests))
                    responses.Add(Reply(WebMessageTypes.QuestUpdated, QuestUpdatePayloadFor(update)));
                responses.AddRange(GrantKillXp(creature, now));
            }
            var carriesLoot = _loot.TryGetValue(swing.TargetId, out var spoils)
                && !LootService.IsEmpty(spoils);
            KillShared(swing.TargetId, now);
            HoldCorpse(swing.TargetId, now, carriesLoot);
            LeaveDeadTarget(swing.TargetId, responses);
            responses.Add(Reply(WebMessageTypes.UnitDied, DeathOf(target)));
            responses.Add(Reply(WebMessageTypes.EntityLootable,
                new EntityLootablePayload(target, carriesLoot ? WireCodes.Loot.Lootable : WireCodes.Loot.Emptied)));
            responses.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(attacker)));
            responses.Add(VictimLeftCombat(target));
            responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(attacker)));
        }

        TakeIncomingBlows(now, playerArmor, playerStamina, responses);

        int playerHealthRegenRating = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.HealthRegenRating, now);
        var regen = _combat.TickRegen(now, playerStamina, playerHealthRegenRating);
        if (regen is not null)
        {
            var healthValues = new Dictionary<string, int> { [WireCodes.UnitValue.Health] = regen.FinalHealth, [WireCodes.UnitValue.MaxHealth] = regen.MaxHealth };
            MarkUnitValues(playerId, healthValues, now);
        }

        if (_spells.TakeFinished(now) is { } finished) responses.AddRange(LandFinishedCast(finished, now));
        LandSpellsInFlight(now, responses);

        var wisdomBonus = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Wisdom, now);
        var manaRegenRating = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.ManaRegenRating, now);
        var powerRegen = _spells.PowerRegen(now, wisdomBonus, manaRegenRating);
        if (powerRegen is not null)
        {
            MarkUnitValues(playerId, new Dictionary<string, int> { [WireCodes.UnitValue.Power] = powerRegen.Value, [WireCodes.UnitValue.MaxPower] = _spells.EffectiveMaxPower(wisdomBonus) }, now);
        }

        TickDungeonCompletion(now, responses);
        AnnounceEvades(now, responses);
        if (_combat.TakePlayerStruck() | struckOut)
        {
            ShakeRider(now, responses);
            BreakStealth(now, responses);
            InterruptGather(responses);
        }
        MarkAuraStateUpdates(now);
        AnnounceSettled(responses);
        FlushUnitValues(now, responses);
        if (!_combat.IsPlayerDead) SendChangedAttributes(now, responses);

        return responses;
    }

    private void DieTo(string killerId, List<string> responses)
    {
        var playerId = PlayerUnitId;
        var target = WebNetworkId.FromString(playerId);
        var attacker = WebNetworkId.FromString(killerId);
        responses.Add(Reply(WebMessageTypes.UnitDied, DeathOf(target)));
        EndEffectsOnSelf(null, Now, responses);
        responses.AddRange(ApplyDeathPenalty(killerId));
        responses.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(attacker)));
        responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(target)));
        responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(attacker)));
        ReleaseEngaged(killerId);
        UnmarkEngaged();
        StandDownPack(responses);
        MarkFightsReturning();
        _combat.Disengage(playerId);
        foreach (var guardId in _helperFights.Keys.ToArray())
            StandDownGuard(guardId, responses);
        if (_shared is not null && !_shared.IsEngaged(killerId))
        {
            MarkReturning(killerId);
            FaceHome(killerId, responses);
        }
        if (_duels.OpponentOf(playerId) is not null)
            responses.AddRange(EndDuelAsLoser(playerId));
    }

    private float OwnSpeedMultiplier(DateTimeOffset now) =>
        _character is null ? 1f : SpeedMultiplierOf(PlayerUnitId, now);

    private float SpeedMultiplierOf(string unitId, DateTimeOffset now) =>
        Math.Max(0f, 1f - _slow.CurrentPercent(unitId, now) / 100f + _speedup.CurrentPercent(unitId, now) / 100f);

    private int _moveSpeedSent = 100;

    [Flags]
    private enum BodyStates
    {
        None = 0,
        Chilled = 1,
        Silenced = 2,
        Slowed = 4,
        SpedUp = 8,
        Stealthed = 16,
        Stunned = 32,
    }

    private BodyStates BodyStatesOf(string unitId, DateTimeOffset now)
    {
        var states = BodyStates.None;
        if (_slow.IsSlowed(unitId, now)) states |= BodyStates.Slowed;
        if (_control.IsControlled(unitId, now)) states |= BodyStates.Stunned;
        if (_chill.IsChilled(unitId, now)) states |= BodyStates.Chilled;
        if (_speedup.IsActive(unitId, now)) states |= BodyStates.SpedUp;
        if (_stealth.IsStealthed(unitId, now)) states |= BodyStates.Stealthed;
        if (_silence.IsControlled(unitId, now)) states |= BodyStates.Silenced;
        return states;
    }

    private void MarkAuraStateUpdates(DateTimeOffset now, string? alsoConsider = null)
    {
        if (_character is null) return;
        var playerId = PlayerUnitId;
        var considered = new HashSet<string>(_auraStateSent.Keys, StringComparer.Ordinal) { playerId };
        if (alsoConsider is not null) considered.Add(alsoConsider);

        foreach (var unitId in considered)
        {
            var states = BodyStatesOf(unitId, now);
            if (_auraStateSent.TryGetValue(unitId, out var sent) ? sent == states : states == BodyStates.None) continue;
            if (states == BodyStates.None) _auraStateSent.Remove(unitId);
            else _auraStateSent[unitId] = states;

            int Flag(BodyStates state) => states.HasFlag(state) ? 1 : 0;
            MarkUnitValues(unitId, new Dictionary<string, int>
            {
                [WireCodes.UnitValue.Slowed] = Flag(BodyStates.Slowed),
                [WireCodes.UnitValue.Stunned] = Flag(BodyStates.Stunned),
                [WireCodes.UnitValue.Chilled] = Flag(BodyStates.Chilled),
                [WireCodes.UnitValue.SpedUp] = Flag(BodyStates.SpedUp),
                [WireCodes.UnitValue.Stealthed] = Flag(BodyStates.Stealthed),
                [WireCodes.UnitValue.Silenced] = Flag(BodyStates.Silenced),
            }, now);
        }

        int moveSpeed = _control.IsControlled(playerId, now)
            ? 0
            : (int)Math.Round(OwnSpeedMultiplier(now) * 100f, MidpointRounding.AwayFromZero);
        if (moveSpeed != _moveSpeedSent)
        {
            _moveSpeedSent = moveSpeed;
            MarkUnitValues(playerId, new Dictionary<string, int> { [WireCodes.UnitValue.MoveSpeed] = moveSpeed }, now);
        }
    }

    private bool InMeleeRange(string networkId) => DistanceTo(networkId) is { } distance && distance <= MeleeRange;

    private UnitDiedPayload DeathOf(WebNetworkId body)
    {
        var id = body.Text;
        DomainPosition? at = null;
        if (_world is not null && _character is not null
            && string.Equals(id, PlayerUnitId, StringComparison.Ordinal))
            at = _world.PlayerPosition;
        else if (id is not null && _shared?.PositionNow(id, Now) is { } standing)
            at = standing;
        return new UnitDiedPayload(body, at is { } fell ? new Position(fell.X, fell.Y, fell.Z) : null);
    }

    private double? DistanceTo(string networkId)
    {
        if (_world is null || !TryPositionOf(networkId, out var target)) return null;
        var player = _world.PlayerPosition;
        return Math.Sqrt(
            Math.Pow(player.X - target.X, 2) + Math.Pow(player.Y - target.Y, 2) + Math.Pow(player.Z - target.Z, 2));
    }

    private bool HasRangedWeapon()
    {
        if (_character is null || _character.Equipment.Count <= EquipmentSlots.Ranged) return false;
        var entry = _character.Equipment[EquipmentSlots.Ranged];
        if (entry <= 0) return false;
        return _content.Items.TryGetValue((uint)entry, out var item) && item.Class == ItemClasses.Weapons;
    }

    private RangedAttackContext? RangedContextFor(string targetId, DateTimeOffset now)
    {
        if (DistanceTo(targetId) is not { } distance) return null;
        return new RangedAttackContext(
            InMeleeRange: distance <= MeleeRange,
            Distance: distance,
            HasRangedWeapon: HasRangedWeapon(),
            RangedInterval: CombatService.EffectiveSwingInterval(
                _rangeAttackSpeed.CurrentPercent(PlayerUnitId, now)),
            IsMoving: _playerMoving);
    }

    private void TickStance(DateTimeOffset now, List<string> responses)
    {
        if (_world is null || _character is null) return;
        var playerId = PlayerUnitId;
        var targetId = _combat.CurrentTargetOf(playerId);
        if (targetId is null) return;
        if (RangedContextFor(targetId, now) is not { } context) return;

        var change = _combat.UpdateStance(playerId, context, now);
        if (change is not null)
        {
            var attacker = WebNetworkId.FromString(change.AttackerId);
            var target = WebNetworkId.FromString(change.TargetId);
            responses.Add(change.To == CombatService.AttackStance.Ranged
                ? Reply(WebMessageTypes.AttackRangeStart, new AttackRangeStartPayload(attacker, target))
                : Reply(WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(attacker, target)));
        }

        if (_combat.TickRangedShot(playerId, context, now) is { } shot)
        {
            responses.Add(Reply(WebMessageTypes.AttackShotFired, new AttackShotFiredPayload(
                WebNetworkId.FromString(shot.AttackerId), WebNetworkId.FromString(shot.TargetId),
                shot.Speed, shot.SpellVisualId)));
        }
    }

    public int Arrivals { get; private set; }

    private IReadOnlyList<string> EnterWorld(ushort mapId, DomainPosition position, float yaw,
        string? instanceKey = null, InstanceWorldState? instanceState = null,
        SharedMapState? instanceShared = null, DungeonMode instanceMode = DungeonMode.Normal)
    {
        Arrivals++;
        _riding = null;
        if (instanceKey is null && _character is not null && IsDungeonMap(mapId))
        {
            var reopened = ResolveInstance(mapId);
            instanceKey = reopened.InstanceKey;
            instanceState = reopened.World;
            instanceShared = reopened.Shared;
            instanceMode = reopened.Mode;
        }

        var wasFighting = _character is not null
            && _combat.CurrentTargetOf(PlayerUnitId) is not null;
        ReleaseSharedCombat();

        var responses = new List<string>();
        if (wasFighting && _character is not null)
            responses.Add(Reply(WebMessageTypes.AttackExitCombat,
                new AttackExitCombatPayload(WebNetworkId.FromString(PlayerUnitId))));
        RemoveView(responses);
        ResetVisibility();
        _awaitingArrivalMove = _world is not null;
        _arrivedAt = Now;
        if (_instanceKey is not null && !string.Equals(_instanceKey, instanceKey, StringComparison.Ordinal))
            _instances.Leave(PlayerUnitId, _instanceKey);
        _instanceKey = instanceKey;
        _instanceWorld = instanceState;
        _copy = instanceKey is null ? DungeonCopy.Normal : DungeonCopy.For(_content.Dungeons, mapId, instanceMode);

        _content.Maps.TryGetValue(mapId, out var map);
        _world = new WorldSessionState(mapId, position, yaw, map);
        var sharedMapId = mapId;
        _shared = _sharedMaps.For(mapId, (x, z, nearY) =>
            _walkableGround?.GroundHeightNear(sharedMapId, x, z, nearY ?? 0f) is float walkable
                ? new GroundAnswer(walkable, KnowsFloors: true)
                : _content.Terrain?.HeightAt(sharedMapId, x, z) is float terrain
                    ? new GroundAnswer(terrain, KnowsFloors: false)
                    : null,
            (from, to) => _walkableGround is null
                || _walkableGround.GroundHeightNear(sharedMapId, from.X, from.Z, from.Y) is null
                || _walkableGround.CanStepStraight(sharedMapId, from, to));
        _combat.UseHostilityLog(_shared);
        _movementCursor = _shared.MovementCursor;
        var wire = new Position(position.X, position.Y, position.Z);
        responses.Add(Reply(WebMessageTypes.WorldLoading, new WorldLocation(mapId, wire)));
        responses.Add(Reply(WebMessageTypes.WorldEntered, new WorldEnteredPayload(mapId, wire,
            WebNetworkId.FromString(PlayerUnitId), AurasWornBy(PlayerUnitId))));
        responses.Add(AmbientReply(mapId));

        if (_instanceWorld is not null)
        {
            var population = _instanceWorld.EnsurePopulated(_creatureSpawns.Select(_copy.Spawn), _staticObjectSpawns);
            var summonsSoFar = 0;
            _instanceWorld.SummonedSince(ref summonsSoFar);
            _summonCursor = summonsSoFar;
            _shared = instanceShared ?? SharedForInstance(mapId);
            _combat.UseHostilityLog(_shared);
            _movementCursor = _shared.MovementCursor;
            var entering = Now;
            foreach (var entity in population.Creatures)
            {
                var wireId = entity.Id.ToString();
                _shared.Adopt(wireId, entity.SpawnId,
                    new DomainPosition(entity.Position.X, entity.Position.Y, entity.Position.Z));
                var live = _shared.TryGetPosition(wireId, out var standing)
                    ? entity with { Position = new Ubiquitous.Common.Position(standing.X, standing.Y, standing.Z) }
                    : entity;

                if (!_shared.IsAlive(wireId))
                {
                    if (!_shared.TryGetCorpseUntil(entity.SpawnId, out var until) || entering >= until) continue;
                    RegisterCreature(live);
                    responses.Add(SpawnedReply(live, dead: true));
                    responses.Add(Reply(WebMessageTypes.EntityLootable,
                        new EntityLootablePayload(WebNetworkId.FromString(wireId), WireCodes.Loot.Emptied)));
                    _combat.MarkDead(wireId);
                    _despawnDue[wireId] = until;
                    continue;
                }

                RegisterCreature(live);
                responses.Add(SpawnedReply(live));
            }

            foreach (var entity in population.StaticObjects)
            {
                RegisterStaticObject(entity);
                responses.Add(SpawnedReply(entity));
            }

            responses.Add(HiddenObjectsReply(mapId));
            responses.AddRange(OpenBarrierReplies(mapId));
            responses.AddRange(QuestGuideRefreshReplies());
            return responses;
        }

        foreach (var entity in _shared!.VisibleCreatures(_world.PlayerPosition, WorldSessionState.DefaultViewRange, _creatureSpawns))
        {
            RegisterCreature(entity);
            responses.Add(SpawnedReply(entity));
        }

        responses.Add(HiddenObjectsReply(mapId));
        responses.AddRange(EnterWorldCorpseReplies(mapId));

        foreach (var entity in _shared.VisibleStaticObjects(_world.PlayerPosition, WorldSessionState.DefaultViewRange, _staticObjectSpawns))
        {
            RegisterStaticObject(entity);
            responses.Add(SpawnedReply(entity));
        }

        responses.AddRange(QuestGuideRefreshReplies());
        return responses;
    }

    // Design choice: a step this small is a position correction, not a step that breaks a cast.
    private const double CastBreakingStep = 0.05;

    private async Task<IReadOnlyList<string>> MovePlayerAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var world = _world;
        var from = world?.PlayerPosition;
        var replies = await ApplyMoveRequestAsync(payload, cancellationToken);
        if (from is not { } start || _world != world || !_playerMoving
            || Math.Sqrt(Math.Pow(world!.PlayerPosition.X - start.X, 2) + Math.Pow(world.PlayerPosition.Z - start.Z, 2))
                <= CastBreakingStep
            || !_spells.CancelPending())
            return replies;
        return [Reply(WebMessageTypes.SpellInterrupted, new SpellInterruptedPayload(WebNetworkId.FromString(PlayerUnitId))),
            .. replies];
    }

    private Task<IReadOnlyList<string>> ApplyMoveRequestAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_world is null)
        {
            return Task.FromResult<IReadOnlyList<string>>([Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before movement.")]);
        }

        if (_character is not null && _control.IsControlled(PlayerUnitId, Now))
        {
            var locked = _world.PlayerPosition;
            return Task.FromResult<IReadOnlyList<string>>([Reply(WebMessageTypes.MoveAccepted,
                new MoveAcceptedPayload(new Position(locked.X, locked.Y, locked.Z)))]);
        }

        var request = WebMessageJson.DeserializePayload<PlayerMovePayload>(payload);
        var requested = new DomainPosition((float)request.X, (float)request.Y, (float)request.Z);
        float speedMultiplier = AllowedSpeedMultiplier(Now);
        if (_awaitingArrivalMove && Now - _arrivedAt >= ArrivalSettleWindow)
            _awaitingArrivalMove = false;
        var isAdmin = _account?.IsAdmin == true && !_awaitingArrivalMove;
        var previousPosition = _world.PlayerPosition;
        _playerMoving = request.Moving;
        var decision = _world.ApplyPlayerMovement(requested, (float)request.Yaw, speedMultiplier, bypassValidation: isAdmin);
        if (decision.Accepted) _awaitingArrivalMove = false;
        if (!decision.Accepted || decision.Corrected)
        {
            return Task.FromResult<IReadOnlyList<string>>([Reply(WebMessageTypes.MoveAccepted,
                new MoveAcceptedPayload(new Position(
                    decision.Position.X, decision.Position.Y, decision.Position.Z)))]);
        }

        if (ClosedBarrierCrossed(previousPosition, decision.Position) is { } closed)
        {
            _world.ApplyPlayerMovement(previousPosition, _world.PlayerYaw, bypassValidation: true);
            return Task.FromResult<IReadOnlyList<string>>([Reply(WebMessageTypes.MoveAccepted,
                new MoveAcceptedPayload(new Position(
                    previousPosition.X, previousPosition.Y, previousPosition.Z))),
                Error(WireCodes.ProtocolError.BarrierClosed, $"The way through {closed.Id} is not open.")]);
        }

        var gate = _content.MapGates.FirstOrDefault(g =>
            g.SourceMapId == _world.MapId &&
            g.Crosses(previousPosition.X, previousPosition.Z, decision.Position.X, decision.Position.Z) &&
            (_instanceWorld is null ? !g.IsDungeonExit : g.IsDungeonExit));
        if (gate is not null)
        {
            var leavingId = PlayerUnitId;
            UnmarkEngaged();
            var gateStopped = _combat.Disengage(leavingId);
            var gateResponses = new List<string>();
            if (gateStopped is not null)
            {
                gateResponses.Add(Reply(WebMessageTypes.AttackHalted,
                    new AttackHaltedPayload(WebNetworkId.FromString(gateStopped))));
                gateResponses.Add(Reply(WebMessageTypes.AttackExitCombat,
                    new AttackExitCombatPayload(WebNetworkId.FromString(leavingId))));
            }
            if (gate.IsDungeonEntrance && _instanceWorld is null && _character is not null)
            {
                var entered = ResolveInstance(gate.DestinationMapId);
                gateResponses.AddRange(EnterWorld(
                    gate.DestinationMapId,
                    new DomainPosition(gate.DestinationX, gate.DestinationY, gate.DestinationZ),
                    gate.DestinationYaw,
                    entered.InstanceKey, entered.World, entered.Shared, entered.Mode));
                return Task.FromResult<IReadOnlyList<string>>(gateResponses);
            }
            gateResponses.AddRange(EnterWorld(
                gate.DestinationMapId,
                new DomainPosition(gate.DestinationX, gate.DestinationY, gate.DestinationZ),
                gate.DestinationYaw));
            return Task.FromResult<IReadOnlyList<string>>(gateResponses);
        }

        var responses = new List<string> { Reply(WebMessageTypes.EntityMoved,
            new EntityMovedPayload(
                WebNetworkId.FromString(PlayerUnitId),
                new Position(decision.Position.X, decision.Position.Y, decision.Position.Z),
                decision.Yaw,
                request.Moving,
                request.State,
                request.Speed,
                Velocity: request.Velocity)) };
        if (_instanceWorld is null)
        {
            RefreshView(responses);
        }
        return Task.FromResult<IReadOnlyList<string>>(responses);
    }

    private string Error(string code, string reason) => _outbox.Error(code, reason);

    private string Reply(string type, object payload) => _outbox.Reply(type, payload);
}
