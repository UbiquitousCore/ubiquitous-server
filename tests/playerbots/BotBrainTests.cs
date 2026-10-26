using Ubiquitous.Game;
using Ubiquitous.Modules;
using Ubiquitous.Modules.Playerbots;
using Ubiquitous.Protocol.Web;
using WirePosition = Ubiquitous.Protocol.Web.Position;

namespace Ubiquitous.Playerbots.Tests;

public sealed class BotBrainTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Self = "player:self";

    private static BotBrain Brain(Func<string, bool>? isBot = null, int doubleUpPercent = 0) =>
        new(new BotKnowledge(PlayerbotWorldTests.QuestContent()),
            PlayerbotSettings.From(new ModuleSettings(new Dictionary<string, string>
            {
                ["doubleUpPercent"] = $"{doubleUpPercent}",
                ["idleBreaks"] = "false",
            })),
            new Random(1), isBot ?? (_ => false));

    private static BotView InWorld(float x = 0, float z = 0)
    {
        var view = new BotView();
        Hear(view, WebMessageTypes.WorldEntered, new WorldEnteredPayload(1, new WirePosition(x, 0, z), WebNetworkId.FromString(Self)));
        Hear(view, WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(new() { ["level"] = 1, ["health"] = 100, ["maxHealth"] = 100, ["power"] = 100, ["maxPower"] = 100 }));
        Hear(view, WebMessageTypes.QuestStatuses, new QuestStatusesPayload([]));
        return view;
    }

    private static void Hear(BotView view, string type, object payload, DateTimeOffset? at = null) =>
        view.Apply(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 0, payload)), at ?? Start);

    private static void Creature(BotView view, int id, uint entry, float x, float z, int faction = 0, bool offersQuests = false, int level = 1) =>
        Hear(view, WebMessageTypes.EntitySpawned, new EntitySpawnedPayload(
            WebNetworkId.FromString($"Creature:{id}"), (int)entry, new WirePosition(x, 0, z), 0,
            OffersQuests: offersQuests ? true : null, Level: level, Health: 10, MaxHealth: 10, Faction: faction));

    // The first turn in the world refreshes quest marks; tests start after it.
    private static BotDecision After(BotBrain brain, BotView view, DateTimeOffset now)
    {
        brain.Decide(view, now);
        return brain.Decide(view, now);
    }

    [Fact]
    public void ADeadBot_WaitsAMomentThenReleasesToTheGraveyard()
    {
        var brain = Brain();
        var view = InWorld();
        Hear(view, WebMessageTypes.PlayerDeath, new PlayerDeathPayload(null, 0));

        Assert.IsType<Hold>(brain.Decide(view, Start));
        var release = Assert.IsType<SendMessage>(brain.Decide(view, Start.AddSeconds(6)));
        Assert.Equal(WebMessageTypes.PlayerUnstuck, release.Type);
    }

    [Fact]
    public void APartyInvite_IsAcceptedWhenNotInAParty()
    {
        var brain = Brain();
        var view = InWorld();
        Hear(view, WebMessageTypes.PartyInvited, new PartyInvitedPayload(WebNetworkId.FromString("player:hero"), "Hero"));

        var answer = Assert.IsType<SendMessage>(brain.Decide(view, Start));

        Assert.Equal(WebMessageTypes.PartyAccept, answer.Type);
    }

    [Fact]
    public void ACreatureHittingTheBot_IsFoughtBeforeAnythingElse()
    {
        var brain = Brain();
        var view = InWorld();
        Creature(view, 1, 7001, 2, 0, faction: 1, offersQuests: true);
        Hear(view, WebMessageTypes.QuestStatuses, new QuestStatusesPayload([new(WebNetworkId.FromString("Creature:1"), QuestStates.Offered)]));
        Creature(view, 2, 7002, 1, 0);
        Hear(view, WebMessageTypes.AttackSwingResult, new AttackSwingResultPayload(
            WebNetworkId.FromString("Creature:2"), WebNetworkId.FromString(Self), "hit", 3, 97));

        var decision = Assert.IsType<SendMessage>(brain.Decide(view, Start));

        Assert.Equal(WebMessageTypes.AttackEngage, decision.Type);
        Assert.Contains("Creature:2", WebMessageJson.ToElement(decision.Payload).GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAttackerOutOfReach_IsWalkedTo()
    {
        var brain = Brain();
        var view = InWorld();
        Creature(view, 2, 7002, 20, 0);
        Hear(view, WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
            WebNetworkId.FromString("Creature:2"), WebNetworkId.FromString(Self)));

        var walk = Assert.IsType<WalkTo>(brain.Decide(view, Start));

        Assert.Equal(20f, walk.Target.X);
    }

    [Fact]
    public void AQuestGiver_IsWalkedToAskedAndItsOfferTaken()
    {
        var brain = Brain();
        var view = InWorld();
        Creature(view, 1, 7001, 20, 0, faction: 1, offersQuests: true);
        Hear(view, WebMessageTypes.QuestStatuses, new QuestStatusesPayload([new(WebNetworkId.FromString("Creature:1"), QuestStates.Offered)]));

        Assert.IsType<WalkTo>(After(brain, view, Start));
        view.Position = new Ubiquitous.Common.Position(18, 0, 0);
        var ask = Assert.IsType<SendMessage>(brain.Decide(view, Start));
        Assert.Equal(WebMessageTypes.QuestList, ask.Type);

        Hear(view, WebMessageTypes.QuestDetails, new QuestDetailsPayload(WebNetworkId.FromString("Creature:1"), 6001, 1, 2, QuestStates.Offered, 1));
        var accept = Assert.IsType<SendMessage>(brain.Decide(view, Start));
        Assert.Equal(WebMessageTypes.QuestAccept, accept.Type);
    }

    [Fact]
    public void AReadyQuest_IsHandedInAtTheGiverMarkedComplete()
    {
        var brain = Brain();
        var view = InWorld();
        Creature(view, 1, 7001, 2, 0, faction: 1, offersQuests: true);
        Hear(view, WebMessageTypes.QuestStatuses, new QuestStatusesPayload([new(WebNetworkId.FromString("Creature:1"), QuestStates.ReadyToHandIn)]));
        Hear(view, WebMessageTypes.QuestUpdated, new QuestUpdatePayload(6001, 1, 2, QuestStates.ReadyToHandIn, 0, true, []));

        var handIn = Assert.IsType<SendMessage>(After(brain, view, Start));

        Assert.Equal(WebMessageTypes.QuestComplete, handIn.Type);
    }

    [Fact]
    public void Grinding_LeavesCreaturesWellAboveTheBotsLevelAlone()
    {
        var brain = Brain();
        var view = InWorld();
        Creature(view, 3, 9400, 5, 0, level: 10);

        Assert.IsNotType<SendMessage>(After(brain, view, Start));
    }

    [Fact]
    public void Commands_AreTakenOnlyFromThePartyLeader()
    {
        var brain = Brain();
        var view = InWorld();
        Hear(view, WebMessageTypes.PartyRoster, new PartyRosterPayload("p1",
        [
            new(WebNetworkId.FromString("player:hero"), "Hero", true, true),
            new(WebNetworkId.FromString(Self), "Bot", false, true),
            new(WebNetworkId.FromString("player:other"), "Other", false, true),
        ]));
        Hear(view, WebMessageTypes.ChatReceived, new ChatReceivedPayload("party", "Other", "stay"));
        brain.Decide(view, Start);
        Assert.Equal(BotMode.Follow, brain.Mode);

        Hear(view, WebMessageTypes.ChatReceived, new ChatReceivedPayload("party", "Hero", "stay"));
        var reply = Assert.IsType<SendMessage>(brain.Decide(view, Start));

        Assert.Equal(BotMode.Stay, brain.Mode);
        Assert.Equal(WebMessageTypes.PlayerChat, reply.Type);
    }

    [Fact]
    public void ARefusedSpell_IsSetAsideAndAnotherTried()
    {
        var brain = Brain();
        var view = InWorld();
        Hear(view, WebMessageTypes.SpellBook, new SpellBookPayload([Ubiquitous.Data.OperatorSpells.Finish, 100], []));
        Creature(view, 2, 7002, 1, 0);
        Hear(view, WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
            WebNetworkId.FromString("Creature:2"), WebNetworkId.FromString(Self)));
        Hear(view, WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
            WebNetworkId.FromString(Self), WebNetworkId.FromString("Creature:2")));
        Hear(view, WebMessageTypes.SpellFailed, new SpellFailedPayload(
            WebNetworkId.FromString(Self), Ubiquitous.Data.OperatorSpells.Finish, WireCodes.SpellRefusal.InvalidTarget));

        var cast = Assert.IsType<SendMessage>(brain.Decide(view, Start.AddSeconds(2)));

        Assert.Equal(WebMessageTypes.SpellRequest, cast.Type);
        Assert.Contains("\"spellId\":100", WebMessageJson.ToElement(cast.Payload).GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Bots_DoNotGroupWithEachOtherUnlessAllowed()
    {
        var view = InWorld();
        Hear(view, WebMessageTypes.EntitySpawned, new PlayerSpawnedPayload(WebNetworkId.FromString("player:other"),
            new WirePosition(3, 0, 0), 0, "Otherbot", 1, 1, 1, 0, 0, 1, 1, 1, 1, [], 100, 100, 0, 0));
        var alone = Brain(_ => true);
        var grouping = new BotBrain(new BotKnowledge(PlayerbotWorldTests.QuestContent()),
            PlayerbotSettings.From(new ModuleSettings(new Dictionary<string, string> { ["groupWithBots"] = "true", ["idleBreaks"] = "false" })),
            new Random(1), _ => true);

        var asked = Enumerable.Range(0, 5).Select(turn => alone.Decide(view, Start.AddSeconds(turn))).OfType<SendMessage>();
        var invited = Enumerable.Range(0, 5).Select(turn => grouping.Decide(view, Start.AddSeconds(turn))).OfType<SendMessage>();

        Assert.DoesNotContain(asked, message => message.Type == WebMessageTypes.PartyInvite);
        Assert.Contains(invited, message => message.Type == WebMessageTypes.PartyInvite);
    }

    private static GameContent PlayContent() => PlayerbotWorldTests.QuestContent() with
    {
        Items = new Dictionary<uint, Ubiquitous.Data.ItemDefinition>
        {
            [8001] = new(8001, "old-helm", 4, 0, 1, InventoryType: 1, SellPrice: 5, Armor: 2),
            [8002] = new(8002, "new-helm", 4, 0, 1, InventoryType: 1, SellPrice: 5, Armor: 10),
            [8003] = new(8003, "rag", 15, 0, 1, SellPrice: 1, Quality: 0),
        },
        TalentTrees =
        [
            new Ubiquitous.Data.TalentTreeDefinition(1, Ubiquitous.Data.PlayerClassIds.Warrior,
            [
                new Ubiquitous.Data.TalentDefinition(11, 0, 0, 0, [], [5001, 5002]),
                new Ubiquitous.Data.TalentDefinition(12, 1, 0, 5, [11], [5003]),
            ]),
        ],
        Stops = new Dictionary<uint, Ubiquitous.Data.TeleportStopDefinition>
        {
            [1] = new(1, 1, 30, 0, 0),
            [2] = new(2, 7, 0, 0, 0),
        },
        Routes = [new Ubiquitous.Data.TravelRouteDefinition(1, 1, 2, 10)],
        MapGates = [new Ubiquitous.Data.MapGateDefinition(1, 40, 44, -2, 2, 7, 0, 0, 0, 0)],
    };

    private static BotBrain PlayBrain() =>
        new(new BotKnowledge(PlayContent()), PlayerbotSettings.From(new ModuleSettings(
            new Dictionary<string, string> { ["idleBreaks"] = "false" })), new Random(1), _ => false);

    private static void Player(BotView view, string id, string name, float x, float z, int health = 100, int playerClass = 1) =>
        Hear(view, WebMessageTypes.EntitySpawned, new PlayerSpawnedPayload(WebNetworkId.FromString(id),
            new WirePosition(x, 0, z), 0, name, 1, playerClass, 1, 0, 0, 1, 1, 1, 1, [], health, 100, 0, 0));

    private static void Grouped(BotView view, string leaderId, params string[] others) =>
        Hear(view, WebMessageTypes.PartyRoster, new PartyRosterPayload("p1",
        [
            new(WebNetworkId.FromString(leaderId), "Lead", true, true),
            new(WebNetworkId.FromString(Self), "Bot", false, true),
            .. others.Select(id => new PartyRosterMemberPayload(WebNetworkId.FromString(id), id, false, true)),
        ]));

    // The brain only decides; a walk lands where it was going before the next turn.
    private static BotDecision Arrive(BotView view, BotDecision decision)
    {
        if (decision is WalkTo walk) view.Position = walk.Target;
        return decision;
    }

    private static string Text(BotDecision decision) =>
        WebMessageJson.ToElement(((SendMessage)decision).Payload).GetRawText();

    [Fact]
    public void Grinding_LeavesACreatureToAnotherPlayerFightingItOrNearerToIt()
    {
        var brain = Brain();
        var view = InWorld();
        Creature(view, 1, 7002, 3, 0);
        Creature(view, 2, 7002, -6, 0);
        Creature(view, 3, 7002, 0, 9);
        Player(view, "player:other", "Other", 4, 0);
        Hear(view, WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
            WebNetworkId.FromString("player:other"), WebNetworkId.FromString("Creature:1")));
        Player(view, "player:near", "Near", -6, 1);

        var decision = After(brain, view, Start);

        var walk = Assert.IsType<WalkTo>(decision);
        Assert.Equal(9f, walk.Target.Z);
    }

    [Fact]
    public void WithDoublingAlwaysAllowed_TheNearestClaimedCreatureIsTaken()
    {
        var brain = Brain(doubleUpPercent: 100);
        var view = InWorld();
        Creature(view, 1, 7002, 3, 0);
        Creature(view, 3, 7002, 0, 9);
        Player(view, "player:other", "Other", 4, 0);

        var walk = Assert.IsType<WalkTo>(After(brain, view, Start));

        Assert.Equal(3f, walk.Target.X);
    }

    [Fact]
    public void AGroupMatesFight_IsJoinedRatherThanLeftToThem()
    {
        var brain = Brain();
        var view = InWorld();
        Player(view, "player:lead", "Lead", 2, 0);
        Grouped(view, "player:lead");
        Creature(view, 1, 7002, 1, 0);
        Hear(view, WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
            WebNetworkId.FromString("player:lead"), WebNetworkId.FromString("Creature:1")));

        var decision = Assert.IsType<SendMessage>(brain.Decide(view, Start));

        Assert.Equal(WebMessageTypes.AttackEngage, decision.Type);
        Assert.Contains("Creature:1", Text(decision), StringComparison.Ordinal);
    }

    [Fact]
    public void AHealer_HealsTheMostHurtGroupMate()
    {
        var brain = Brain();
        var view = InWorld();
        view.Class = Ubiquitous.Data.PlayerClassIds.Monk;
        Hear(view, WebMessageTypes.SpellBook, new SpellBookPayload([20], []));
        Player(view, "player:lead", "Lead", 2, 0, health: 70);
        Player(view, "player:mate", "Mate", 1, 1, health: 30);
        Grouped(view, "player:lead", "player:mate");

        var heal = Assert.IsType<SendMessage>(brain.Decide(view, Start));

        Assert.Equal(WebMessageTypes.SpellRequest, heal.Type);
        Assert.Contains("player:mate", Text(heal), StringComparison.Ordinal);
    }

    [Fact]
    public void ATank_TakesOnWhatIsHittingAGroupMate()
    {
        var brain = Brain();
        var view = InWorld();
        view.Class = Ubiquitous.Data.PlayerClassIds.Warrior;
        Player(view, "player:lead", "Lead", 1, 0);
        Grouped(view, "player:lead");
        Creature(view, 1, 7002, 1.5f, 0);
        Hear(view, WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
            WebNetworkId.FromString("Creature:1"), WebNetworkId.FromString("player:lead")));

        var engage = Assert.IsType<SendMessage>(brain.Decide(view, Start));

        Assert.Equal(WebMessageTypes.AttackEngage, engage.Type);
    }

    [Theory]
    [InlineData("player:lead", true)]
    [InlineData("player:stranger", false)]
    public void ASummons_IsAcceptedFromAGroupMateOnly(string summoner, bool accepted)
    {
        var brain = PlayBrain();
        var view = InWorld();
        Grouped(view, "player:lead");
        Hear(view, WebMessageTypes.SummonAsked, new SummonAskedPayload(
            WebNetworkId.FromString(summoner), "Someone", 1, new WirePosition(100, 0, 100)));

        var answer = Assert.IsType<SendMessage>(brain.Decide(view, Start));

        Assert.Equal(WebMessageTypes.SummonAnswer, answer.Type);
        Assert.Equal($"{{\"accept\":{(accepted ? "true" : "false")}}}", Text(answer));
    }

    [Fact]
    public void ADuel_IsAcceptedAndTheOpponentFought()
    {
        var brain = Brain();
        var view = InWorld();
        Player(view, "player:hero", "Hero", 1, 0);
        Hear(view, WebMessageTypes.DuelChallenged, new DuelChallengedPayload(WebNetworkId.FromString("player:hero")));

        Assert.Equal(WebMessageTypes.DuelAccept, Assert.IsType<SendMessage>(brain.Decide(view, Start)).Type);
        Hear(view, WebMessageTypes.DuelStarted, new DuelStartedPayload(WebNetworkId.FromString("player:hero")));
        var engage = Assert.IsType<SendMessage>(brain.Decide(view, Start));

        Assert.Equal(WebMessageTypes.AttackEngage, engage.Type);
        Assert.Contains("player:hero", Text(engage), StringComparison.Ordinal);
    }

    [Fact]
    public void ADuelCountdown_OpensDistanceAndFacesTheChallengerBeforeFighting()
    {
        var brain = Brain();
        var view = InWorld();
        Player(view, "player:hero", "Hero", 1, 0);
        Hear(view, WebMessageTypes.DuelChallenged, new DuelChallengedPayload(WebNetworkId.FromString("player:hero")));
        Assert.Equal(WebMessageTypes.DuelAccept, Assert.IsType<SendMessage>(brain.Decide(view, Start)).Type);
        Hear(view, WebMessageTypes.DuelStarted, new DuelStartedPayload(WebNetworkId.FromString("player:hero"), 5), Start);

        var ready = Assert.IsType<WalkTo>(brain.Decide(view, Start));
        Assert.Equal("duel", ready.Purpose);
        Assert.True(MathF.Sqrt(MathF.Pow(ready.Target.X - 1, 2) + MathF.Pow(ready.Target.Z, 2)) >= 3.5f);

        Arrive(view, ready);
        var face = Assert.IsType<SendMessage>(brain.Decide(view, Start));
        Assert.Equal(WebMessageTypes.PlayerMove, face.Type);

        var closing = Assert.IsType<WalkTo>(brain.Decide(view, Start.AddSeconds(6)));
        Assert.Contains("hero", closing.Purpose, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuelCountdown_StopsEvenWhenTheChallengerIsUnseen()
    {
        var brain = Brain();
        var view = InWorld();
        Hear(view, WebMessageTypes.DuelChallenged, new DuelChallengedPayload(WebNetworkId.FromString("player:hero")));
        Assert.Equal(WebMessageTypes.DuelAccept, Assert.IsType<SendMessage>(brain.Decide(view, Start)).Type);
        Hear(view, WebMessageTypes.DuelStarted, new DuelStartedPayload(WebNetworkId.FromString("player:hero"), 5), Start);

        Assert.IsType<Hold>(brain.Decide(view, Start));
    }

    [Fact]
    public void Help_ListsTheLeaderCommands()
    {
        var brain = Brain();
        var view = InWorld();
        Grouped(view, "player:hero");
        Hear(view, WebMessageTypes.ChatReceived, new ChatReceivedPayload("party", "Lead", "help"));

        var reply = Assert.IsType<SendMessage>(brain.Decide(view, Start.AddSeconds(1)));

        Assert.Equal(WebMessageTypes.PlayerChat, reply.Type);
        Assert.Contains("Here are the commands you can use", Text(reply), StringComparison.Ordinal);
        Assert.Contains("attack (attack the leader", Text(reply), StringComparison.Ordinal);
    }

    [Fact]
    public void Trades_AreTakenOnlyFromGroupMatesAndConfirmedOnceTheyConfirm()
    {
        var brain = Brain();
        var view = InWorld();
        Grouped(view, "player:lead");
        Hear(view, WebMessageTypes.TradeRequested, new TradeRequestedPayload(WebNetworkId.FromString("player:stranger")));
        Assert.Equal(WebMessageTypes.TradeDecline, Assert.IsType<SendMessage>(brain.Decide(view, Start)).Type);

        Hear(view, WebMessageTypes.TradeRequested, new TradeRequestedPayload(WebNetworkId.FromString("player:lead")));
        Assert.Equal(WebMessageTypes.TradeAccept, Assert.IsType<SendMessage>(brain.Decide(view, Start)).Type);
        Hear(view, WebMessageTypes.TradeStarted, new TradeStartedPayload(WebNetworkId.FromString("player:lead")));
        Assert.IsType<Hold>(brain.Decide(view, Start));
        Hear(view, WebMessageTypes.TradeUpdated, new TradeUpdatedPayload(WebNetworkId.FromString("player:lead"), 0, [], true, false));

        Assert.Equal(WebMessageTypes.TradeConfirm, Assert.IsType<SendMessage>(brain.Decide(view, Start)).Type);
    }

    [Fact]
    public void ABetterItemInTheBag_IsWorn()
    {
        var brain = PlayBrain();
        var view = InWorld();
        view.Class = Ubiquitous.Data.PlayerClassIds.Warrior;
        Hear(view, WebMessageTypes.InventoryUpdated, new InventoryUpdatedPayload(16, 0,
        [
            new("worn", 8001, 1, new StorageSlotPayload(InventoryAreas.Equipment, 0)),
            new("bagged", 8002, 1, new StorageSlotPayload(InventoryAreas.Bag, 3)),
        ]));

        var equip = Assert.IsType<SendMessage>(brain.Decide(view, Start));

        Assert.Equal(WebMessageTypes.InventoryEquip, equip.Type);
        Assert.Contains("bagged", Text(equip), StringComparison.Ordinal);
    }

    [Fact]
    public void FullBags_AreEmptiedOfJunkAtAKnownVendor()
    {
        var brain = PlayBrain();
        var view = InWorld();
        view.Class = Ubiquitous.Data.PlayerClassIds.Warrior;
        Hear(view, WebMessageTypes.EntitySpawned, new EntitySpawnedPayload(WebNetworkId.FromString("Creature:9"), 9001,
            new WirePosition(20, 0, 0), 0, NpcFlags: (uint)Ubiquitous.Data.NpcFlags.Vendor, Faction: 1));
        Hear(view, WebMessageTypes.InventoryUpdated, new InventoryUpdatedPayload(2, 0,
            [new("rag", 8003, 1, new StorageSlotPayload(InventoryAreas.Bag, 0)), new("rag2", 8003, 1, new StorageSlotPayload(InventoryAreas.Bag, 1))]));

        Assert.IsType<WalkTo>(After(brain, view, Start));
        view.Position = new Ubiquitous.Common.Position(18, 0, 0);
        var sell = Assert.IsType<SendMessage>(brain.Decide(view, Start));

        Assert.Equal(WebMessageTypes.VendorSell, sell.Type);
        Assert.Contains("\"itemId\":\"rag\"", Text(sell), StringComparison.Ordinal);
    }

    [Fact]
    public void TalentPoints_AreSpentLowestTierFirst()
    {
        var brain = PlayBrain();
        var view = InWorld();
        view.Class = Ubiquitous.Data.PlayerClassIds.Warrior;
        Hear(view, WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(new() { ["level"] = 12 }));

        var learn = Assert.IsType<SendMessage>(brain.Decide(view, Start));

        Assert.Equal(WebMessageTypes.TalentLearn, learn.Type);
        Assert.Contains("\"talentId\":11", Text(learn), StringComparison.Ordinal);
    }

    [Fact]
    public void ALeaderWhoLeftThroughAMapGate_IsFollowedThroughIt()
    {
        var brain = PlayBrain();
        var view = InWorld(30, 0);
        Player(view, "player:lead", "Lead", 41, 0);
        Grouped(view, "player:lead");
        Hear(view, WebMessageTypes.EntityRemoved, new EntityRemovedPayload(WebNetworkId.FromString("player:lead")));

        var walk = Assert.IsType<WalkTo>(brain.Decide(view, Start));

        Assert.Equal(42f, walk.Target.X);
        Assert.Equal(0f, walk.Target.Z);
    }

    [Fact]
    public void ABotWithNothingLeftToDo_TakesTheBusSomewhereElse()
    {
        var brain = PlayBrain();
        var view = InWorld(29, 0);
        Hear(view, WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(new() { ["gold"] = 50 }));
        brain.Decide(view, Start);

        var decisions = Enumerable.Range(0, 3).Select(turn => brain.Decide(view, Start.AddMinutes(4).AddSeconds(turn))).ToList();

        var board = Assert.Single(decisions.OfType<SendMessage>(), message => message.Type == WebMessageTypes.BusConfirm);
        Assert.Contains("\"to\":2", Text(board), StringComparison.Ordinal);
    }

    [Fact]
    public void Now_And_Then_ABotStopsToTalkOrStepsAwayAndThenCarriesOn()
    {
        var brain = new BotBrain(new BotKnowledge(PlayerbotWorldTests.QuestContent()),
            PlayerbotSettings.From(new ModuleSettings()), new Random(2), _ => false);
        var view = InWorld();

        var turns = Enumerable.Range(0, 3600).Select(turn => (At: Start.AddSeconds(turn), Decision: Arrive(view, brain.Decide(view, Start.AddSeconds(turn))))).ToList();

        Assert.Contains(turns, turn => turn.Decision is SendMessage { Type: WebMessageTypes.PlayerEmote });
        var resting = turns.Count(turn => brain.OnABreak(turn.At) && turn.Decision is Hold or SendMessage { Type: WebMessageTypes.PlayerEmote });
        Assert.InRange(resting, 30, 3300);
        Assert.Contains(turns.Skip(3000), turn => turn.Decision is WalkTo);
    }

    [Fact]
    public void ABotOnABreak_StillFightsBack()
    {
        var brain = new BotBrain(new BotKnowledge(PlayerbotWorldTests.QuestContent()),
            PlayerbotSettings.From(new ModuleSettings()), new Random(2), _ => false);
        var view = InWorld();
        var at = Enumerable.Range(0, 3600).Select(turn => Start.AddSeconds(turn))
            .First(now => { Arrive(view, brain.Decide(view, now)); return brain.OnABreak(now); });
        Creature(view, 2, 7002, view.Position.X + 1, view.Position.Z);
        Hear(view, WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
            WebNetworkId.FromString("Creature:2"), WebNetworkId.FromString(Self)));

        Assert.Equal(WebMessageTypes.AttackEngage, Assert.IsType<SendMessage>(brain.Decide(view, at)).Type);
    }

    [Fact]
    public void AnotherPlayersRevival_DoesNotMoveTheBot()
    {
        var view = InWorld(5, 5);
        Player(view, "player:other", "Other", 8, 8, health: 0);

        Hear(view, WebMessageTypes.PlayerRevived, new PlayerRevivedPayload(WebNetworkId.FromString("player:other"),
            100, 100, 0, 0, 1, new WirePosition(-90, 7, -4), 0));

        Assert.Equal(5f, view.Position.X);
        Assert.Equal(-90f, view.Units["player:other"].Position.X);
    }

    [Fact]
    public void AWalkToAQuestGiver_IsNeverBrokenOffForABreak()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var brain = new BotBrain(new BotKnowledge(PlayerbotWorldTests.QuestContent()),
                PlayerbotSettings.From(new ModuleSettings()), new Random(seed), _ => false);
            var view = InWorld();
            Creature(view, 1, 7001, 200, 0, faction: 1, offersQuests: true);
            Hear(view, WebMessageTypes.QuestStatuses, new QuestStatusesPayload([new(WebNetworkId.FromString("Creature:1"), QuestStates.Offered)]));

            for (var turn = 0; turn < 600 && view.Position.X < 200 - BotBrain.InteractReach - 2; turn++)
            {
                var decision = brain.Decide(view, Start.AddSeconds(turn));
                Assert.True(decision is WalkTo or SendMessage { Type: WebMessageTypes.QuestRefreshStatuses }, $"seed {seed} turn {turn}: {decision}");
                if (decision is WalkTo walk) view.Position = new Ubiquitous.Common.Position(view.Position.X + 2, 0, 0);
            }
        }
    }

    [Fact]
    public void AnItemQuestWithNoGuide_SendsTheBotToWhereTheItemsDroppersLive()
    {
        var content = PlayerbotWorldTests.QuestContent() with
        {
            Quests = [new Ubiquitous.Data.QuestDefinition(6100, 7001, 1, 2, 1,
                [new Ubiquitous.Data.QuestObjectiveDefinition(0, Ubiquitous.Data.QuestObjectiveKind.Held, GoalCount: 3, ObjectiveItem: 8100)])],
            LootTables = new Dictionary<uint, Ubiquitous.Data.CreatureLootTableDefinition>
            {
                [7002] = new(7002, [new Ubiquitous.Data.LootGroupDefinition(1, 1, [new Ubiquitous.Data.LootTableItemDefinition(8100, 50, 1, 1)])]),
            },
        };
        var brain = new BotBrain(new BotKnowledge(content, [new Ubiquitous.Data.CreatureSpawnDefinition(1, 1, 7002, 300f, 0f, 40f, 0f)]),
            PlayerbotSettings.From(new ModuleSettings(new Dictionary<string, string> { ["idleBreaks"] = "false" })), new Random(1), _ => false);
        var view = InWorld();
        Hear(view, WebMessageTypes.QuestUpdated, new QuestUpdatePayload(6100, 1, 2, QuestStates.Underway, 0, false,
            [new QuestObjectivePayload(0, 0, 0, 0, 8100, 3, 3, 0)]));

        var walk = Assert.IsType<WalkTo>(After(brain, view, Start));

        Assert.Equal(300f, walk.Target.X);
        Assert.Equal(40f, walk.Target.Z);
    }

    private static GameContent LootContent() => PlayerbotWorldTests.QuestContent() with
    {
        Quests = [new Ubiquitous.Data.QuestDefinition(6200, 7001, 1, 2, 1,
            [new Ubiquitous.Data.QuestObjectiveDefinition(0, Ubiquitous.Data.QuestObjectiveKind.Held, GoalCount: 2, ObjectiveItem: 8302)])],
        Items = new Dictionary<uint, Ubiquitous.Data.ItemDefinition>
        {
            [8301] = new(8301, "rusty-sword", 2, 0, 1, InventoryType: 13, SellPrice: 3, Quality: 1, MinDamage: 1, MaxDamage: 2, SpeedMs: 2000),
            [8302] = new(8302, "wolf-pelt", 15, 0, 1, SellPrice: 1, Quality: 1),
            [8303] = new(8303, "pebble", 15, 0, 1, SellPrice: 1, Quality: 1),
        },
    };

    private static (BotBrain Brain, BotView View) Looter(string style, int bagSlots, params (string Id, int Entry)[] bag)
    {
        var brain = new BotBrain(new BotKnowledge(LootContent()), PlayerbotSettings.From(new ModuleSettings(
            new Dictionary<string, string> { ["idleBreaks"] = "false", ["lootStyle"] = style })), new Random(1), _ => false);
        var view = InWorld();
        view.Class = Ubiquitous.Data.PlayerClassIds.Ranger;
        Hear(view, WebMessageTypes.QuestUpdated, new QuestUpdatePayload(6200, 1, 2, QuestStates.Underway, 0, false,
            [new QuestObjectivePayload(0, 0, 0, 0, 8302, 2, 2, 0)]));
        Hear(view, WebMessageTypes.InventoryUpdated, new InventoryUpdatedPayload(bagSlots, 0,
            [.. bag.Select((item, index) => new InventoryItemPayload(item.Id, item.Entry, 1, new StorageSlotPayload(InventoryAreas.Bag, index)))]));
        Creature(view, 2, 7002, 1, 0);
        Hear(view, WebMessageTypes.AttackSwingResult, new AttackSwingResultPayload(
            WebNetworkId.FromString(Self), WebNetworkId.FromString("Creature:2"), "hit", 5, 0));
        Hear(view, WebMessageTypes.UnitDied, new UnitDiedPayload(WebNetworkId.FromString("Creature:2")));
        Hear(view, WebMessageTypes.EntityLootable, new EntityLootablePayload(WebNetworkId.FromString("Creature:2"), WireCodes.Loot.Lootable));
        Assert.Equal(WebMessageTypes.LootOpen, Assert.IsType<SendMessage>(brain.Decide(view, Start)).Type);
        Hear(view, WebMessageTypes.LootContents, new LootContentsPayload(WebNetworkId.FromString("Creature:2"), 0,
            [new LootRowPayload(0, "a", 8301, 1), new LootRowPayload(1, "b", 8302, 1)]));
        return (brain, view);
    }

    private static List<string> LootTurns(BotBrain brain, BotView view) =>
        [.. Enumerable.Range(0, 4).Select(_ => brain.Decide(view, Start)).OfType<SendMessage>()
            .TakeWhile(message => message.Type != WebMessageTypes.LootClose).Select(message => $"{message.Type} {Text(message)}")];

    [Fact]
    public void APickyRanger_LeavesAPoorSwordButTakesWhatItsQuestNeeds()
    {
        var (brain, view) = Looter("picky", 12);

        var turns = LootTurns(brain, view);

        Assert.Equal([$"{WebMessageTypes.LootTake} {{\"networkId\":\"Creature:2\",\"slot\":1}}"], turns);
    }

    [Fact]
    public void AHoarder_TakesEverything()
    {
        var (brain, view) = Looter("hoarder", 12);

        Assert.Equal(2, LootTurns(brain, view).Count(turn => turn.StartsWith(WebMessageTypes.LootTake, StringComparison.Ordinal)));
    }

    [Fact]
    public void WithFullBags_ABotThrowsOutItsCheapestThingForWhatAQuestNeeds()
    {
        var (brain, view) = Looter("picky", 2, ("pebble", 8303), ("sword", 8301));

        var first = Assert.IsType<SendMessage>(brain.Decide(view, Start));

        Assert.Equal(WebMessageTypes.InventoryDestroy, first.Type);
        Assert.Contains("\"itemId\":\"pebble\"", Text(first), StringComparison.Ordinal);
    }

    private static GameContent PlanContent() => PlayerbotWorldTests.QuestContent() with
    {
        Quests =
        [
            new Ubiquitous.Data.QuestDefinition(6400, 7001, 1, 2, 1,
                [new Ubiquitous.Data.QuestObjectiveDefinition(0, Ubiquitous.Data.QuestObjectiveKind.Triggered, GoalCount: 1, ObjectiveSpell: 9999)]),
            new Ubiquitous.Data.QuestDefinition(6401, 7001, 1, 2, 1,
                [new Ubiquitous.Data.QuestObjectiveDefinition(0, Ubiquitous.Data.QuestObjectiveKind.Killed, GoalCount: 5, ObjectiveCreature: 7002)]),
        ],
    };

    private static BotBrain Planner() =>
        new(new BotKnowledge(PlanContent(), [new Ubiquitous.Data.CreatureSpawnDefinition(1, 1, 7002, 0f, 0f, 0f, 0f),
                new Ubiquitous.Data.CreatureSpawnDefinition(2, 1, 7002, 100f, 0f, 0f, 0f)]),
            PlayerbotSettings.From(new ModuleSettings(new Dictionary<string, string> { ["idleBreaks"] = "false" })),
            new Random(1), _ => false);

    private static void Holds(BotView view, uint questId, int slot, int done = 0) =>
        Hear(view, WebMessageTypes.QuestUpdated, new QuestUpdatePayload(questId, 1, 2, QuestStates.Underway, slot, false,
            [new QuestObjectivePayload(0, done, 0, 0, 0, 0, 5, 0)]));

    [Fact]
    public void AQuestNeedingASpellTheBotLacks_IsAbandoned()
    {
        var brain = Planner();
        var view = InWorld();
        Holds(view, 6400, slot: 3);

        var abandon = Assert.IsType<SendMessage>(After(brain, view, Start));

        Assert.Equal(WebMessageTypes.QuestDestroy, abandon.Type);
        Assert.Equal("{\"slot\":3,\"questId\":6400}", Text(abandon));
    }

    [Fact]
    public void AQuestNeedingASpellTheBotLacks_IsNotTaken()
    {
        var brain = Planner();
        var view = InWorld();
        Creature(view, 1, 7001, 2, 0, faction: 1, offersQuests: true);
        Hear(view, WebMessageTypes.QuestStatuses, new QuestStatusesPayload([new(WebNetworkId.FromString("Creature:1"), QuestStates.Offered)]));
        Assert.Equal(WebMessageTypes.QuestList, Assert.IsType<SendMessage>(After(brain, view, Start)).Type);

        Hear(view, WebMessageTypes.QuestDetails, new QuestDetailsPayload(WebNetworkId.FromString("Creature:1"), 6400, 1, 2, QuestStates.Offered, 1));

        Assert.IsNotType<SendMessage>(brain.Decide(view, Start));
    }

    [Fact]
    public void AnEmptyHuntingGround_IsLeftForTheNextPlaceTheCreatureLives()
    {
        var brain = Planner();
        var view = InWorld(1, 1);
        Holds(view, 6401, slot: 0);

        var walk = Assert.IsType<WalkTo>(After(brain, view, Start));

        Assert.Equal(100f, walk.Target.X);
    }

    [Fact]
    public void AQuestGoingNowhere_IsPutAsideForAWhile()
    {
        var brain = Planner();
        var view = InWorld(50, 50);
        Holds(view, 6401, slot: 0);
        Assert.IsType<WalkTo>(After(brain, view, Start));

        var later = brain.Decide(view, Start.AddMinutes(9));
        var stillAside = brain.Decide(view, Start.AddMinutes(12));

        Assert.False(later is WalkTo { Purpose: var purpose } && purpose!.StartsWith("ground:", StringComparison.Ordinal));
        Assert.False(stillAside is WalkTo { Purpose: var aside } && aside!.StartsWith("ground:", StringComparison.Ordinal));
    }

    [Fact]
    public void ACreatureOnTheGroundBelow_IsWalkedDownToRatherThanSwungAtFromOverhead()
    {
        var brain = Brain();
        var view = InWorld();
        Hear(view, WebMessageTypes.EntitySpawned, new EntitySpawnedPayload(WebNetworkId.FromString("Creature:2"), 7002,
            new WirePosition(0, -10, 0), 0, Level: 1, Health: 10, MaxHealth: 10, Faction: 0));
        Hear(view, WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(
            WebNetworkId.FromString("Creature:2"), WebNetworkId.FromString(Self)));

        var walk = Assert.IsType<WalkTo>(brain.Decide(view, Start));

        Assert.Equal(-10f, walk.Target.Y);
    }
}
