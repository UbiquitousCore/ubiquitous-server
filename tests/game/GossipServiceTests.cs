using Ubiquitous.Data;

namespace Ubiquitous.Game.Tests;

public sealed class GossipServiceTests
{
    private static readonly GossipTeleportDefinition ToTown = new(2, 900, 13, -10f, 2f, 40f, 1.5f, 0, 250);
    private static readonly GossipTeleportDefinition ForGems = new(3, 900, 13, 0f, 0f, 0f, 0f, 1, 5);

    private static readonly Dictionary<uint, GossipMenuDefinition> Menus = new()
    {
        [10] = new(10, [11], []),
        [11] = new(11, [12], []),
        [12] = new(12, [], [ToTown, ForGems]),
        [20] = new(20, [], [ToTown with { Choice = 1 }]),
        [30] = new(30, [], [], [GossipScripts.SendToTownOfSilence, 0, 0, 0, 0, 0]),
    };

    private static readonly TeleportStopDefinition Silence = new(GossipScripts.TownOfSilenceSpot, 1, 10f, 2f, 30f, 1.5f);

    private static readonly Dictionary<uint, TeleportStopDefinition> Spots = new() { [Silence.Id] = Silence };

    private static readonly Dictionary<uint, NpcDefinition> Npcs = new()
    {
        [9001] = new(9001, NpcFlags.Gossip, GreetingMenuId: 10),
        [9002] = new(9002, NpcFlags.Gossip),
        [9003] = new(9003, NpcFlags.None, GreetingMenuId: 10),
        [9004] = new(9004, NpcFlags.Gossip, GreetingMenuId: 30),
    };

    private static GossipPick? Pick(uint entry, uint menu, int choice) =>
        new GossipService().Pick(new GossipRequest("Creature:1", menu, choice), entry, Npcs, Menus, Spots);

    [Fact]
    public void Hello_AcceptsNpcWithServiceFlags()
    {
        var result = new GossipService().Open("Creature:1", 9001,
            new Dictionary<uint, NpcDefinition> { [9001] = new(9001, NpcFlags.None, OffersQuests: true) });

        Assert.Equal(new GossipEvent("Creature:1", 9001), result);
    }

    [Fact]
    public void Hello_RejectsNonNpcEntry()
    {
        Assert.Null(new GossipService().Open("Creature:1", 9001,
            new Dictionary<uint, NpcDefinition> { [9001] = new(9001, NpcFlags.None) }));
    }

    [Fact]
    public void Pick_SellsTheTeleportThePageOffersForThatChoice()
    {
        Assert.Equal(new GossipPick(GossipPickKind.Teleport, ToTown), Pick(9001, 12, 2));
    }

    [Fact]
    public void Pick_AChoiceRunningTheSilenceScriptTeleportsFreeToTheTownOfSilence()
    {
        Assert.Equal(
            new GossipPick(GossipPickKind.Teleport, new GossipTeleportDefinition(1, 0, 1, 10f, 2f, 30f, 1.5f, 0, 0)),
            Pick(9004, 30, 1));
        Assert.Equal(new GossipPick(GossipPickKind.NothingHappens), Pick(9004, 30, 2));
    }

    [Fact]
    public void Pick_ReachesAPageOnlyThroughTheNpcsOwnGreeting()
    {
        Assert.Null(Pick(9001, 20, 1));
        Assert.Null(Pick(9002, 10, 1));
        Assert.Null(Pick(9003, 12, 2));
    }

    [Fact]
    public void Pick_AnyOtherChoiceOnAReachablePageDoesNothing()
    {
        Assert.Equal(new GossipPick(GossipPickKind.NothingHappens), Pick(9001, 10, 1));
        Assert.Equal(new GossipPick(GossipPickKind.NothingHappens), Pick(9001, 12, 1));
    }

    [Fact]
    public void Pick_ATeleportPricedInAnotherCurrencyDoesNothing()
    {
        Assert.Equal(new GossipPick(GossipPickKind.NothingHappens), Pick(9001, 12, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    public void Pick_RejectsASlotOutsideTheSix(int choice)
    {
        Assert.Null(Pick(9001, 12, choice));
    }
}
