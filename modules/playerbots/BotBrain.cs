using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using Position = Ubiquitous.Common.Position;

namespace Ubiquitous.Modules.Playerbots;

public abstract record BotDecision;

public sealed record SendMessage(string Type, object Payload) : BotDecision;

// Purpose names what the walk is for, so an unreachable goal can be given up. Reach is
// how close is close enough to do it from, when the bot cannot get Within.
public sealed record WalkTo(Position Target, float Within, string? Purpose = null, float? Reach = null) : BotDecision;

public sealed record Hold : BotDecision
{
    public static Hold Instance { get; } = new();
}

public enum BotMode
{
    Free,
    Follow,
    Stay,
}

// Chooses one thing to do each turn, in the order a player would: stay alive,
// answer the group, fight, loot, look after its gear, hand in, pick up, work on
// a quest, grind, travel, wander. Everything it decides is something a client
// could ask for.
public sealed partial class BotBrain(BotKnowledge knowledge, PlayerbotSettings settings, Random random, Func<string, bool> isBot)
{
    public const float MeleeReach = 2.2f;
    public const float TalkReach = 4f;
    // The server lets a player talk, hand in, loot and use things from 10 units away.
    public const float InteractReach = 8f;
    public const float SightRange = 40f;
    public const float FollowSlack = 6f;
    private static readonly TimeSpan ReleaseDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryEngage = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetrySpell = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan StatusRefresh = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShunFor = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RestFor = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan InviteEvery = TimeSpan.FromSeconds(30);
    private const string Wandering = "wander";
    private const float HuntRadius = 15f;
    private static readonly TimeSpan EmptyGroundRest = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan StalledAfter = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan SetAsideFor = TimeSpan.FromMinutes(10);
    private readonly HashSet<uint> _abandoned = [];
    private readonly Dictionary<uint, (int Done, DateTimeOffset Since)> _questProgress = [];

    private readonly Queue<BotDecision> _replies = new();
    private readonly Dictionary<string, DateTimeOffset> _shunned = new(StringComparer.Ordinal);
    private readonly HashSet<int> _triedSlots = [];
    private readonly Dictionary<uint, DateTimeOffset> _setAside = [];
    private DateTimeOffset _diedAt;
    private DateTimeOffset _nextRelease;
    private DateTimeOffset _nextEngage;
    private DateTimeOffset _nextSpell;
    private DateTimeOffset _nextStatusRefresh;
    private DateTimeOffset _nextInvite;
    private DateTimeOffset _restUntil;
    private DateTimeOffset _idleUntil;
    private bool _rested;
    private string? _pull;
    private string? _commandTarget;
    private string? _talkingTo;
    private string? _lootingId;
    private Position? _wanderTo;
    private bool _followWhenGrouped = true;

    public BotMode Mode { get; private set; } = BotMode.Free;

    public BotRole Role(BotView view) => RoleOf(view.Class);

    // Whatever the bot was walking for cannot be reached; it is left alone for a while.
    public void Unreachable(string? purpose, DateTimeOffset now)
    {
        _wanderTo = null;
        _idleUntil = now + TimeSpan.FromSeconds(1);
        if (purpose is null) return;
        Shun(purpose, now);
        if (purpose == _pull) _pull = null;
        if (purpose == _commandTarget) _commandTarget = null;
    }

    public BotDecision Decide(BotView view, DateTimeOffset now)
    {
        var decision = Choose(view, now);
        NoteFinished(decision, view.InCombat || view.AttackingId is not null, now);
        return decision;
    }

    private BotDecision Choose(BotView view, DateTimeOffset now)
    {
        LearnFromRefusals(view, now);
        if (view.Dead) return Release(now);
        _diedAt = default;

        if (_replies.TryDequeue(out var reply)) return reply;
        if (Answer(view, now) is { } answer) return answer;
        if (view.CastingUntil > now) return Hold.Instance;
        FollowWhenGrouped(view);
        if (_lastProgressAt == default) _lastProgressAt = now;

        if (Trade(view, now) is { } trade) return trade;
        if (HealGroup(view, now) is { } heal) return heal;
        if (FightTarget(view, now) is { } foe) return Fight(view, foe, now);
        if (Mode == BotMode.Stay) return Hold.Instance;
        if (Recover(view, now) is { } recover) return recover;
        if (Loot(view, now) is { } loot) return loot;
        if (Equip(view, now) is { } equip) return equip;
        if (SpendTalent(view, now) is { } talent) return talent;
        if (Mode == BotMode.Follow) return Follow(view) ?? Hold.Instance;
        if (TakeBreak(view, now) is { } rest) return rest;

        if (now >= _nextStatusRefresh)
        {
            _nextStatusRefresh = now + StatusRefresh;
            return new SendMessage(WebMessageTypes.QuestRefreshStatuses, new { });
        }

        if (Sell(view, now) is { } sell) return sell;
        if ((_journey is not null || Outgrown(view, now)) && Travel(view, now, urgent: true) is { } leave) return leave;
        if ((HandIn(view, now) ?? PickUpQuests(view, now) ?? WorkOnQuests(view, now) ?? Grind(view, now)) is { } work)
        {
            _lastProgressAt = now;
            return work;
        }

        return Travel(view, now)
            ?? Team(view, now)
            ?? Wander(view, now);
    }

    // A refused spell is left alone for a while, and for long when trying again cannot help.
    private void LearnFromRefusals(BotView view, DateTimeOffset now)
    {
        foreach (var (spellId, reason) in view.SpellRefusals)
        {
            var wait = reason switch
            {
                WireCodes.SpellRefusal.OutOfRange or WireCodes.SpellRefusal.NotEnoughPower => TimeSpan.FromSeconds(3),
                WireCodes.SpellRefusal.OnCooldown or WireCodes.SpellRefusal.AlreadyCasting
                    or WireCodes.SpellRefusal.Incapacitated or WireCodes.SpellRefusal.Silenced => TimeSpan.FromSeconds(2),
                _ => TimeSpan.FromMinutes(5),
            };
            _setAside[spellId] = now + wait;
        }

        view.SpellRefusals.Clear();
    }

    private BotDecision Release(DateTimeOffset now)
    {
        if (_diedAt == default) _diedAt = now;
        _pull = null;
        _commandTarget = null;
        if (now - _diedAt < ReleaseDelay || now < _nextRelease) return Hold.Instance;
        _nextRelease = now + ReleaseDelay;
        return new SendMessage(WebMessageTypes.PlayerUnstuck, new { });
    }

    private BotDecision? Answer(BotView view, DateTimeOffset now)
    {
        if (view.PendingInvite is { } invite)
        {
            view.PendingInvite = null;
            if (!view.InParty && settings.AcceptPartyInvites)
                return new SendMessage(WebMessageTypes.PartyAccept, new { });
            return new SendMessage(WebMessageTypes.PartyDecline, new { });
        }

        if ((AnswerDuel(view) ?? AnswerTrade(view) ?? AnswerSummon(view)) is { } social) return social;

        while (view.Heard.Count > 0)
        {
            var heard = view.Heard[0];
            view.Heard.RemoveAt(0);
            if (Command(view, heard) is { } command) return command;
        }

        return null;
    }

    private BotDecision? Command(BotView view, ChatHeard heard)
    {
        var master = view.PartyLeaderId;
        if (master is null || master == view.SelfId) return null;
        var fromMaster = heard.SenderId == master
            || (heard.SenderId is null && view.PartyNames.TryGetValue(master, out var name) && name == heard.SenderName);
        if (!fromMaster) return null;

        var said = BotCommand.Parse(heard.Text);
        string? answer = said switch
        {
            BotCommandKind.Follow => SetMode(BotMode.Follow, "Following."),
            BotCommandKind.Stay => SetMode(BotMode.Stay, "Staying here."),
            BotCommandKind.Free => SetMode(BotMode.Free, "Going my own way."),
            BotCommandKind.Attack => Assist(view, master),
            BotCommandKind.Leave => "Leaving the party.",
            BotCommandKind.Report => $"Level {view.Level}, health {view.Health}/{view.MaxHealth}, {view.Quests.Count} quests.",
            _ => null,
        };
        if (answer is null) return null;

        var replyChannel = heard.Channel == WebMessageTypes.ChatWhisper
            ? new SendMessage(WebMessageTypes.ChatWhisper, new ChatWhisperPayload(master, answer))
            : new SendMessage(WebMessageTypes.PlayerChat, new ChatPayload("party", answer));
        if (said != BotCommandKind.Leave) return replyChannel;
        _replies.Enqueue(new SendMessage(WebMessageTypes.PartyLeave, new { }));
        return replyChannel;
    }

    private string SetMode(BotMode mode, string answer)
    {
        Mode = mode;
        _followWhenGrouped = false;
        _pull = null;
        return answer;
    }

    private string Assist(BotView view, string master)
    {
        _commandTarget = view.Units.TryGetValue(master, out var leader) ? leader.Targeting : null;
        return _commandTarget is null ? "I can't see what you are fighting." : "Attacking.";
    }

    // A bot joining someone else's group follows them until told otherwise.
    private void FollowWhenGrouped(BotView view)
    {
        if (!view.InParty)
        {
            if (Mode == BotMode.Follow) Mode = BotMode.Free;
            _followWhenGrouped = true;
            return;
        }

        if (_followWhenGrouped && view.PartyLeaderId != view.SelfId && Mode == BotMode.Free) Mode = BotMode.Follow;
    }

    private SeenUnit? FightTarget(BotView view, DateTimeOffset now)
    {
        if (view.DuelOpponent is { } rival && view.Units.TryGetValue(rival, out var opponent) && !opponent.Dead)
            return now >= view.DuelStartsAt ? opponent : null;
        var attacker = view.Attackers
            .Select(id => view.Units.GetValueOrDefault(id))
            .Where(unit => unit is { Dead: false })
            .OrderBy(unit => Flat(view.Position, unit!.Position))
            .FirstOrDefault();
        if (attacker is not null) return attacker;
        if (Mode == BotMode.Stay) return null;
        if (Protect(view) is { } onAMate) return onAMate;

        foreach (var id in new[] { _commandTarget, LeaderTarget(view), view.AttackingId, _pull })
        {
            if (id is not null && view.Units.TryGetValue(id, out var unit) && unit is { Dead: false, Kind: UnitKind.Creature }
                && !Shunned(id, now) && (!unit.NeverFightsBack || id == _pull || id == _commandTarget))
                return unit;
        }

        _commandTarget = null;
        _pull = null;
        return null;
    }

    private static string? LeaderTarget(BotView view)
    {
        if (view.PartyLeaderId is not { } leader || leader == view.SelfId) return null;
        return view.Units.TryGetValue(leader, out var unit) ? unit.Targeting : null;
    }

    private BotDecision Fight(BotView view, SeenUnit foe, DateTimeOffset now)
    {
        _idleUntil = default;
        var distance = Distance(view.Position, foe.Position);
        var spell = UsableSpell(view, now, knowledge.IsHarmful);
        if (spell is { MaxRange: > 6f } ranged && view.AttackingId != foe.Id)
        {
            if (distance > ranged.MaxRange * 0.85f) return new WalkTo(foe.Position, ranged.MaxRange * 0.7f, foe.Id);
            return Cast(ranged, foe.Id, now);
        }

        if (distance > MeleeReach) return new WalkTo(foe.Position, MeleeReach * 0.6f, foe.Id);
        if (view.AttackingId != foe.Id && now >= _nextEngage)
        {
            _nextEngage = now + RetryEngage;
            return new SendMessage(WebMessageTypes.AttackEngage, new { targetId = foe.Id });
        }

        return spell is not null && distance <= Math.Max(spell.MaxRange, MeleeReach) ? Cast(spell, foe.Id, now) : Hold.Instance;
    }

    private BotDecision Cast(SpellDefinition spell, string? targetId, DateTimeOffset now)
    {
        if (now < _nextSpell) return Hold.Instance;
        _nextSpell = now + RetrySpell;
        return targetId is null
            ? new SendMessage(WebMessageTypes.SpellRequest, new { spellId = spell.Id })
            : new SendMessage(WebMessageTypes.SpellRequest, new { spellId = spell.Id, targetId });
    }

    private SpellDefinition? UsableSpell(BotView view, DateTimeOffset now, Func<uint, bool> kind) =>
        view.KnownSpells
            .Where(kind)
            .Where(id => !view.CooldownUntil.TryGetValue(id, out var until) || until <= now)
            .Where(id => !_setAside.TryGetValue(id, out var until) || until <= now)
            .Select(id => knowledge.Spells[id])
            .Where(spell => (spell.PowerCost ?? 0) <= view.Power && spell.CastTimeMs <= 3000)
            .OrderByDescending(spell => (spell.EffectDamage ?? 0) + (spell.EffectHeal ?? 0) + (spell.AuraPeriodicDamage ?? 0))
            .ThenBy(spell => spell.Id)
            .FirstOrDefault();

    private BotDecision? Recover(BotView view, DateTimeOffset now)
    {
        if (view.HealthFraction >= 0.8f) _rested = false;
        if (view.HealthFraction >= 0.45f || view.InCombat) return null;
        if (UsableSpell(view, now, knowledge.IsHeal) is { } heal) return Cast(heal, null, now);
        if (_rested) return now < _restUntil ? Hold.Instance : null;
        _rested = true;
        _restUntil = now + RestFor;
        return Hold.Instance;
    }

    private BotDecision? Loot(BotView view, DateTimeOffset now)
    {
        if (view.Loot is { } window && window.CorpseId == _lootingId)
        {
            if (window.Money > 0) return new SendMessage(WebMessageTypes.LootTakeMoney, new { networkId = window.CorpseId });
            if (LootRows(view, window, now) is { } take) return take;
            view.Loot = null;
            Shun(window.CorpseId, now);
            _lootingId = null;
            return new SendMessage(WebMessageTypes.LootClose, new { networkId = window.CorpseId });
        }

        var corpse = view.Units.Values
            .Where(unit => unit is { Dead: true, Lootable: true } && view.KilledByMe.Contains(unit.Id) && !Shunned(unit.Id, now))
            .Where(unit => Flat(view.Position, unit.Position) <= SightRange)
            .OrderBy(unit => Flat(view.Position, unit.Position))
            .FirstOrDefault();
        if (corpse is null) return null;
        if (!InReach(view.Position, corpse.Position)) return new WalkTo(corpse.Position, MeleeReach * 0.6f, corpse.Id, InteractReach);

        // One try per corpse: if no window opens it is shunned and the bot moves on.
        Shun(corpse.Id, now);
        _lootingId = corpse.Id;
        _triedSlots.Clear();
        return new SendMessage(WebMessageTypes.LootOpen, new { networkId = corpse.Id });
    }

    private BotDecision? Follow(BotView view)
    {
        if (view.PartyLeaderId is not { } leader) return null;
        if (view.Units.TryGetValue(leader, out var unit))
            return Flat(view.Position, unit.Position) > FollowSlack ? new WalkTo(unit.Position, FollowSlack / 2) : null;
        return FollowOutOfSight(view, leader);
    }

    private BotDecision? HandIn(BotView view, DateTimeOffset now)
    {
        foreach (var quest in view.Quests.Values.Where(quest => quest.Ready).OrderBy(quest => quest.Id))
        {
            var finisher = view.Units.Values
                .Where(unit => view.GiverStatus.GetValueOrDefault(unit.Id) == QuestStates.ReadyToHandIn
                    && !Shunned($"{unit.Id}|{quest.Id}", now) && !Shunned(unit.Id, now))
                .OrderBy(unit => Flat(view.Position, unit.Position))
                .FirstOrDefault();
            if (finisher is not null)
            {
                if (!InReach(view.Position, finisher.Position))
                    return new WalkTo(finisher.Position, TalkReach * 0.6f, finisher.Id, InteractReach);
                Shun($"{finisher.Id}|{quest.Id}", now);
                Shun($"handIn:{quest.Id}", now);
                return new SendMessage(WebMessageTypes.QuestComplete, new { networkId = finisher.Id, questId = quest.Id, rewardIndex = 0 });
            }

            if (quest.HandInZone is { } zone && zone.MapId == view.MapId && !InReach(view.Position, zone.Center)
                && !Shunned($"handIn:{quest.Id}", now))
                return new WalkTo(zone.Center, TalkReach, $"handIn:{quest.Id}", InteractReach);
        }

        return null;
    }

    private BotDecision? PickUpQuests(BotView view, DateTimeOffset now)
    {
        var heard = false;
        while (_talkingTo is { } giver && view.QuestOffers.Count > 0)
        {
            heard = true;
            var (offeredBy, questId) = view.QuestOffers[0];
            view.QuestOffers.RemoveAt(0);
            if (offeredBy == giver && CanDo(view, questId))
                return new SendMessage(WebMessageTypes.QuestAccept, new { networkId = giver, questId });
        }

        // Nothing here this bot can do: it does not ask again for a while.
        if (heard && _talkingTo is { } passed) Shun(passed, now, SetAsideFor);

        _talkingTo = null;
        if (ReadQuestItem(view, now) is { } read) return read;

        // An object that starts a quest wears no mark, so the bot looks at any it passes, now and then.
        var nearest = view.Units.Values
            .Where(unit => (view.GiverStatus.GetValueOrDefault(unit.Id) == QuestStates.Offered
                    || (unit.Kind == UnitKind.StaticObject && knowledge.StartsQuests(unit.Entry)))
                && !Shunned(unit.Id, now))
            .Where(unit => unit.Kind != UnitKind.StaticObject || Flat(view.Position, unit.Position) <= SightRange)
            .OrderBy(unit => Flat(view.Position, unit.Position))
            .FirstOrDefault();
        if (nearest is null) return null;
        if (!InReach(view.Position, nearest.Position))
            return new WalkTo(nearest.Position, TalkReach * 0.6f, nearest.Id, InteractReach);

        Shun(nearest.Id, now, nearest.Kind == UnitKind.StaticObject ? SetAsideFor : null);
        _talkingTo = nearest.Id;
        view.QuestOffers.Clear();
        return new SendMessage(WebMessageTypes.QuestList, new { networkId = nearest.Id });
    }

    // An item that starts a quest is read, and the quest it offers taken like a giver's.
    private BotDecision? ReadQuestItem(BotView view, DateTimeOffset now)
    {
        foreach (var item in view.Items.Values.Where(item => item.Area == InventoryAreas.Bag).OrderBy(item => item.Index))
        {
            if (knowledge.QuestStarterItem(item.Entry) is not { QuestId: { } questId }
                || view.Quests.ContainsKey(questId) || !CanDo(view, questId)) continue;
            var key = $"item:{item.Entry}";
            if (Shunned(key, now)) continue;
            Shun(key, now, SetAsideFor);
            _talkingTo = key;
            view.QuestOffers.Clear();
            return new SendMessage(WebMessageTypes.InventoryUse, new InventoryUsePayload(
                new StorageSlotPayload(item.Area, item.Index), item.Id));
        }

        return null;
    }

    private BotDecision? WorkOnQuests(BotView view, DateTimeOffset now)
    {
        if (AbandonImpossible(view) is { } abandon) return abandon;

        (QuestZone Spot, string Key)? goTo = null;
        foreach (var quest in view.Quests.Values.Where(quest => !quest.Ready && !SetAside(quest, now)).OrderBy(quest => quest.Id))
        {
            var (creatures, objects, spells) = knowledge.WantedFor(quest);
            if (CastForQuest(view, quest, spells, now) is { } cast) return cast;
            var thing = view.Units.Values
                .Where(unit => !unit.Dead && !Shunned(unit.Id, now)
                    && ((unit.Kind == UnitKind.Creature && creatures.Contains(unit.Entry) && !LeftToOthers(view, unit))
                        || (unit.Kind == UnitKind.StaticObject && objects.Contains(unit.Entry))))
                .Where(unit => Flat(view.Position, unit.Position) <= SightRange)
                .OrderBy(unit => Flat(view.Position, unit.Position))
                .FirstOrDefault();
            if (thing is { Kind: UnitKind.Creature })
            {
                _pull = thing.Id;
                return Fight(view, thing, now);
            }

            if (thing is not null)
            {
                if (!InReach(view.Position, thing.Position))
                    return new WalkTo(thing.Position, TalkReach * 0.6f, thing.Id, InteractReach);
                Shun(thing.Id, now);
                return new SendMessage(WebMessageTypes.StaticObjectInteract, new { networkId = thing.Id });
            }

            goTo ??= NextHuntingGround(view, quest, creatures, now);
        }

        if (goTo is not { } ground) return null;
        return new WalkTo(ground.Spot.Center, Math.Max(ground.Spot.Radius * 0.5f, TalkReach), ground.Key,
            Math.Max(ground.Spot.Radius, TalkReach));
    }

    // Where a quest's creatures might be: the server's guide areas, then every place
    // they spawn. A place found empty is left for a few minutes and the next one
    // tried, so a bot works round the creature's range rather than waiting in one spot.
    private (QuestZone Spot, string Key)? NextHuntingGround(BotView view, QuestState quest, HashSet<uint> creatures, DateTimeOffset now)
    {
        var grounds = quest.GuideZones.Where(zone => zone.MapId == view.MapId)
            .Concat(knowledge.SpawnsOf(view.MapId, creatures).Select(at => new QuestZone(view.MapId, at, HuntRadius)));
        foreach (var ground in grounds.OrderBy(zone => Flat(view.Position, zone.Center)))
        {
            var key = $"ground:{(int)MathF.Floor(ground.Center.X / HuntRadius)},{(int)MathF.Floor(ground.Center.Z / HuntRadius)}";
            if (Shunned(key, now)) continue;
            if (Flat(view.Position, ground.Center) > Math.Max(ground.Radius, TalkReach)) return (ground, key);
            Shun(key, now, EmptyGroundRest);
        }

        return null;
    }

    // A quest wanting a spell the bot does not have can never be finished: a player drops it.
    private BotDecision? AbandonImpossible(BotView view)
    {
        var hopeless = view.Quests.Values.FirstOrDefault(quest => !CanDo(view, quest.Id));
        if (hopeless is null) return null;
        _abandoned.Add(hopeless.Id);
        return new SendMessage(WebMessageTypes.QuestDestroy, new { slot = hopeless.Slot, questId = hopeless.Id });
    }

    private bool CanDo(BotView view, uint questId) =>
        !_abandoned.Contains(questId) && knowledge.SpellsWanted(questId).All(view.KnownSpells.Contains);

    // A quest that has gone nowhere for a while is put aside, and the bot does something else first.
    private bool SetAside(QuestState quest, DateTimeOffset now)
    {
        var done = quest.Objectives.Sum(objective => objective.Done);
        if (!_questProgress.TryGetValue(quest.Id, out var progress) || progress.Done != done)
        {
            _questProgress[quest.Id] = (done, now);
            return Shunned($"quest:{quest.Id}", now);
        }

        if (now - progress.Since >= StalledAfter)
        {
            _questProgress[quest.Id] = (done, now);
            Shun($"quest:{quest.Id}", now, SetAsideFor);
        }

        return Shunned($"quest:{quest.Id}", now);
    }

    private BotDecision? CastForQuest(BotView view, QuestState quest, HashSet<uint> spells, DateTimeOffset now)
    {
        foreach (var spellId in spells.Where(view.KnownSpells.Contains).Order())
        {
            if (!knowledge.Spells.TryGetValue(spellId, out var spell)) continue;
            if (knowledge.AimsAtSelf(spellId))
                return (spell.PowerCost ?? 0) <= view.Power ? Cast(spell, null, now) : Hold.Instance;

            var target = view.Units.Values
                .Where(unit => unit is { Kind: UnitKind.Creature, Dead: false } && !Shunned($"cast:{quest.Id}:{unit.Id}", now)
                    && !Shunned(unit.Id, now) && unit.OffersQuests == false && unit.NpcFlags == 0
                    && (unit.NeverFightsBack || !LeftToOthers(view, unit)))
                .Where(unit => Flat(view.Position, unit.Position) <= SightRange)
                .OrderBy(unit => unit.NeverFightsBack ? 0 : 1)
                .ThenBy(unit => Flat(view.Position, unit.Position))
                .FirstOrDefault();
            if (target is null) continue;

            var reach = Math.Max(spell.MaxRange, MeleeReach);
            if (Distance(view.Position, target.Position) > reach * 0.85f)
                return new WalkTo(target.Position, reach * 0.6f, target.Id);
            if ((spell.PowerCost ?? 0) > view.Power || now < _nextSpell) return Hold.Instance;
            Shun($"cast:{quest.Id}:{target.Id}", now, TimeSpan.FromSeconds(30));
            return Cast(spell, target.Id, now);
        }

        return null;
    }

    private BotDecision? Grind(BotView view, DateTimeOffset now)
    {
        var prey = view.Units.Values
            .Where(unit => view.IsHostileCreature(unit) && !unit.NeverFightsBack && !Shunned(unit.Id, now)
                && unit.Level <= view.Level + settings.GrindLevelsAbove
                && LevelingService.KillXp(view.Level, unit.Level) > 0
                && !LeftToOthers(view, unit))
            .Where(unit => Flat(view.Position, unit.Position) <= SightRange)
            .OrderBy(unit => Flat(view.Position, unit.Position))
            .FirstOrDefault();
        if (prey is null) return null;
        _pull = prey.Id;
        return Fight(view, prey, now);
    }

    // Random bots group up with other bots near their level they come across.
    // Off by default: a bot in a bot's group cannot be invited by a player.
    private BotDecision? Team(BotView view, DateTimeOffset now)
    {
        if (!settings.GroupWithBots) return null;
        if (view.InParty && view.PartyLeaderId != view.SelfId) return null;
        if (now < _nextInvite) return null;
        _nextInvite = now + InviteEvery;
        if (view.PartyMemberIds.Count >= 3) return null;
        var mate = view.Units.Values
            .Where(unit => unit.Kind == UnitKind.Player && unit.Name is not null && isBot(unit.Name)
                && !view.PartyMemberIds.Contains(unit.Id) && Math.Abs(unit.Level - view.Level) <= 3
                && Flat(view.Position, unit.Position) <= 20f)
            .OrderBy(_ => random.Next())
            .FirstOrDefault();
        return mate is null ? null : new SendMessage(WebMessageTypes.PartyInvite, new { networkId = mate.Id });
    }

    private BotDecision Wander(BotView view, DateTimeOffset now)
    {
        if (now < _idleUntil) return Hold.Instance;
        if (_wanderTo is { } spot && Flat(view.Position, spot) > 1f) return new WalkTo(spot, 0.5f, Wandering);
        if (_wanderTo is not null)
        {
            _wanderTo = null;
            _idleUntil = now + TimeSpan.FromSeconds(2 + random.NextDouble() * 6);
            BetweenTasks(now);
            return Hold.Instance;
        }

        var angle = random.NextDouble() * Math.PI * 2;
        var reach = 8 + random.NextDouble() * 17;
        _wanderTo = new Position(
            view.Position.X + (float)(Math.Cos(angle) * reach), view.Position.Y,
            view.Position.Z + (float)(Math.Sin(angle) * reach));
        return new WalkTo(_wanderTo.Value, 0.5f, Wandering);
    }

    private void Shun(string key, DateTimeOffset now, TimeSpan? @for = null) => _shunned[key] = now + (@for ?? ShunFor);

    private bool Shunned(string key, DateTimeOffset now) => _shunned.TryGetValue(key, out var until) && until > now;

    // Across and up: a creature on a ledge above is not in reach because it stands overhead.
    public static float Distance(Position from, Position to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var dz = to.Z - from.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static bool InReach(Position from, Position to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var dz = to.Z - from.Z;
        return dx * dx + dy * dy + dz * dz <= InteractReach * InteractReach;
    }

    public static float Flat(Position from, Position to)
    {
        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
