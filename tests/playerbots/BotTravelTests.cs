using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Modules;
using Ubiquitous.Modules.Playerbots;
using Ubiquitous.Protocol.Web;
using WirePosition = Ubiquitous.Protocol.Web.Position;

namespace Ubiquitous.Playerbots.Tests;

// Where a bot goes next and how it starts quests nobody hands it.
public sealed class BotTravelTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Self = "player:self";
    private const uint Rat = 7002;
    private const uint Brute = 7003;
    private const uint Ferryman = 7100;
    private const uint Note = 8100;
    private const uint Shrine = 900;

    private static GameContent Content() => PlayerbotWorldTests.QuestContent() with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(PlayerbotWorldTests.QuestContent().Creatures)
        {
            [Brute] = new(Brute, "brute", 20, 500, FactionType: UnitFactionTypes.Hostile),
            [Ferryman] = new(Ferryman, "ferryman", 5, 100, FactionType: UnitFactionTypes.Townsfolk),
        },
        Items = new Dictionary<uint, ItemDefinition> { [Note] = new(Note, "note", 12, 0, 1, QuestId: 6001) },
        QuestStartersByStaticObject = [new StaticObjectQuestBindingDefinition(Shrine, 6001)],
    };

    private static BotBrain Brain(GameContent content, IReadOnlyList<CreatureSpawnDefinition> spawns) =>
        new(new BotKnowledge(content, spawns), PlayerbotSettings.From(new ModuleSettings(
            new Dictionary<string, string> { ["idleBreaks"] = "false" })), new Random(1), _ => false);

    private static BotView InWorld(int level = 1, float x = 0, float z = 0)
    {
        var view = new BotView();
        Hear(view, WebMessageTypes.WorldEntered, new WorldEnteredPayload(1, new WirePosition(x, 0, z), WebNetworkId.FromString(Self)));
        Hear(view, WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(new()
        {
            ["level"] = level,
            ["health"] = 100,
            ["maxHealth"] = 100,
            ["power"] = 100,
            ["maxPower"] = 100,
            ["gold"] = 500,
        }));
        Hear(view, WebMessageTypes.QuestStatuses, new QuestStatusesPayload([]));
        return view;
    }

    private static void Hear(BotView view, string type, object payload) =>
        view.Apply(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 0, payload)), Start);

    private static string Text(BotDecision decision) =>
        WebMessageJson.ToElement(((SendMessage)decision).Payload).GetRawText();

    // The first turn refreshes quest marks; the turns after it decide.
    private static List<BotDecision> Turns(BotBrain brain, BotView view, DateTimeOffset at, int count = 4) =>
        [.. Enumerable.Range(0, count).Select(turn => brain.Decide(view, at.AddSeconds(turn)))];

    [Fact]
    public void AQuestItemInTheBag_IsReadAndItsQuestTaken()
    {
        var brain = Brain(Content(), []);
        var view = InWorld();
        Hear(view, WebMessageTypes.InventoryUpdated, new InventoryUpdatedPayload(12, 0,
            [new InventoryItemPayload("note-1", (int)Note, 1, new StorageSlotPayload(InventoryAreas.Bag, 3))]));

        var read = Assert.Single(Turns(brain, view, Start, 2).OfType<SendMessage>(), turn => turn.Type == WebMessageTypes.InventoryUse);
        Assert.Contains("\"itemId\":\"note-1\"", Text(read), StringComparison.Ordinal);

        Hear(view, WebMessageTypes.QuestDetails, new QuestDetailsPayload(WebNetworkId.FromString($"item:{Note}"), 6001, 1, 2, QuestStates.Offered, 1));
        var accept = Assert.IsType<SendMessage>(brain.Decide(view, Start.AddSeconds(3)));
        Assert.Equal(WebMessageTypes.QuestAccept, accept.Type);
        Assert.Contains($"\"networkId\":\"item:{Note}\"", Text(accept), StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectThatStartsAQuest_IsAskedForIt()
    {
        var brain = Brain(Content(), []);
        var view = InWorld();
        Hear(view, WebMessageTypes.EntitySpawned, new EntitySpawnedPayload(
            WebNetworkId.FromString("StaticObject:4"), (int)Shrine, new WirePosition(3, 0, 0), 0, Kind: "staticObject"));

        var ask = Assert.Single(Turns(brain, view, Start, 2).OfType<SendMessage>(), turn => turn.Type == WebMessageTypes.QuestList);

        Assert.Contains("StaticObject:4", Text(ask), StringComparison.Ordinal);
    }

    [Fact]
    public void ABoredBot_TakesTheTeleportAConversationOffersWhenItLandsNearestItsLevel()
    {
        var content = Content() with
        {
            Npcs = new Dictionary<uint, NpcDefinition>(Content().Npcs) { [Ferryman] = new(Ferryman, NpcFlags.Gossip, GreetingMenuId: 70) },
            GossipMenus = new Dictionary<uint, GossipMenuDefinition>
            {
                [70] = new(70, [], [], [GossipScripts.SendToTownOfSilence, 0, 0, 0, 0, 0]),
            },
            Stops = new Dictionary<uint, TeleportStopDefinition>
            {
                [GossipScripts.TownOfSilenceSpot] = new(GossipScripts.TownOfSilenceSpot, 2, 500, 0, 500),
            },
        };
        CreatureSpawnDefinition[] spawns =
        [
            new(1, 1, Ferryman, 10, 0, 0, 0),
            new(2, 2, Rat, 505, 0, 500, 0),
        ];
        var brain = Brain(content, spawns);
        var view = InWorld();
        Hear(view, WebMessageTypes.EntitySpawned, new EntitySpawnedPayload(
            WebNetworkId.FromString("Creature:9"), (int)Ferryman, new WirePosition(10, 0, 0), 0, Faction: UnitFactionTypes.Townsfolk));
        brain.Decide(view, Start);

        var bored = Start.AddMinutes(4);
        SendMessage? pick = null;
        for (var turn = 0; turn < 6 && pick is null; turn++)
        {
            switch (brain.Decide(view, bored.AddSeconds(turn)))
            {
                case WalkTo walk:
                    Assert.Equal(10f, walk.Target.X);
                    view.Position = walk.Target;
                    break;
                case SendMessage { Type: WebMessageTypes.NpcPickOption } picked:
                    pick = picked;
                    break;
            }
        }

        Assert.NotNull(pick);
        Assert.Equal("{\"networkId\":\"Creature:9\",\"menuId\":70,\"choice\":1}", Text(pick));
    }

    private static GameContent TwoFares() => Content() with
    {
        Stops = new Dictionary<uint, TeleportStopDefinition>
        {
            [1] = new(1, 1, 30, 0, 0),
            [2] = new(2, 7, 0, 0, 0),
            [3] = new(3, 4, 0, 0, 0),
        },
        Routes = [new TravelRouteDefinition(1, 1, 2, 10), new TravelRouteDefinition(2, 1, 3, 10)],
    };

    private static readonly CreatureSpawnDefinition[] HighAndLow =
    [
        new(1, 1, Rat, 2, 0, 2, 0),
        new(2, 7, Brute, 5, 0, 5, 0),
        new(3, 4, Rat, 5, 0, 5, 0),
    ];

    [Fact]
    public void ABoredBot_TakesTheBusToThePlaceNearestItsLevel()
    {
        for (var seed = 0; seed < 5; seed++)
        {
            var brain = new BotBrain(new BotKnowledge(TwoFares(), HighAndLow), PlayerbotSettings.From(new ModuleSettings(
                new Dictionary<string, string> { ["idleBreaks"] = "false" })), new Random(seed), _ => false);
            var view = InWorld(x: 29);
            brain.Decide(view, Start);

            var board = Assert.Single(Turns(brain, view, Start.AddMinutes(4)).OfType<SendMessage>(),
                turn => turn.Type == WebMessageTypes.BusConfirm);

            Assert.Contains("\"to\":3", Text(board), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ABotFarAboveEverythingAroundIt_LeavesAtOnceForAPlaceItsLevel()
    {
        var brain = Brain(TwoFares(), HighAndLow);
        var view = InWorld(level: 20, x: 29);

        var board = Assert.Single(Turns(brain, view, Start).OfType<SendMessage>(), turn => turn.Type == WebMessageTypes.BusConfirm);

        Assert.Contains("\"to\":2", Text(board), StringComparison.Ordinal);
    }

    [Fact]
    public void Grinding_LeavesCreaturesThatGiveNoExperienceAlone()
    {
        var brain = Brain(Content(), []);
        var view = InWorld(level: 20);
        Hear(view, WebMessageTypes.EntitySpawned, new EntitySpawnedPayload(
            WebNetworkId.FromString("Creature:3"), (int)Rat, new WirePosition(3, 0, 0), 0, Level: 1, Health: 10, MaxHealth: 10));

        Assert.DoesNotContain(Turns(brain, view, Start).OfType<SendMessage>(), turn => turn.Type == WebMessageTypes.AttackEngage);
    }
}
