using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class GossipTeleportTests
{
    private static readonly GossipTeleportDefinition Offer = new(1, 900, 13, 200f, 4f, -150f, 1.5f, 0, 300);

    private static GameContent Content(int currency = 1000) => GameContent.Development with
    {
        StartingCurrency = currency,
        Npcs = new Dictionary<uint, NpcDefinition>(GameContent.Development.Npcs)
        {
            [9010] = new(9010, NpcFlags.Gossip, GreetingMenuId: 50, OffersQuests: true),
        },
        GossipMenus = new Dictionary<uint, GossipMenuDefinition>
        {
            [50] = new(50, [51, 52], []),
            [52] = new(52, [], [], [GossipScripts.SendToTownOfSilence, 0, 0, 0, 0, 0]),
            [51] = new(51, [], [Offer]),
            [60] = new(60, [], [Offer]),
        },
        Stops = new Dictionary<uint, TeleportStopDefinition>(GameContent.Development.Stops)
        {
            [GossipScripts.TownOfSilenceSpot] = new(GossipScripts.TownOfSilenceSpot, 13, -40f, 3f, 60f, 1.5f),
        },
    };

    private static readonly CharacterRecord Character = new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, MapId: 13, X: 0, Y: 0, Z: 0, Level: 1, Equipment: []);

    private static async Task<PlaySession> SessionAsync(GameContent content, float npcX = 3f)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]),
            [new CreatureSpawnDefinition(1001, 13, 9010, npcX, 0, 0, 0)], [], content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        return session;
    }

    private static Task<IReadOnlyList<string>> PickAsync(PlaySession session, uint menuId, int choice) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.NpcPickOption, 1, new { networkId = "Creature:1", menuId, choice })));

    [Fact]
    public async Task ASoldTeleportChargesThePriceAndMovesThePlayer()
    {
        var session = await SessionAsync(Content());

        var responses = await PickAsync(session, 51, 1);

        Assert.Contains(responses, message => message.Contains("\"type\":\"npc.choiceResult\"")
            && message.Contains("\"result\":\"teleported\""));
        Assert.Contains(responses, message => message.Contains("\"type\":\"player.updated\"")
            && message.Contains("\"gold\":700"));
        Assert.Contains(responses, message => message.Contains("\"type\":\"world.entered\""));
        Assert.Equal(new Ubiquitous.Common.Position(200f, 4f, -150f), session.PlayerPosition);
    }

    [Fact]
    public async Task TheChoiceThatRunsTheSilenceScriptMovesThePlayerThereForFree()
    {
        var session = await SessionAsync(Content());

        var responses = await PickAsync(session, 52, 1);

        Assert.Contains(responses, message => message.Contains("\"result\":\"teleported\""));
        Assert.DoesNotContain(responses, message => message.Contains("\"type\":\"player.updated\""));
        Assert.Equal(new Ubiquitous.Common.Position(-40f, 3f, 60f), session.PlayerPosition);
    }

    [Fact]
    public async Task APageTheGreetingDoesNotLeadToSellsNothing()
    {
        var session = await SessionAsync(Content());

        var response = Assert.Single(await PickAsync(session, 60, 1));

        Assert.Contains("invalid_gossip", response);
        Assert.Equal(new Ubiquitous.Common.Position(0f, 0f, 0f), session.PlayerPosition);
    }

    [Fact]
    public async Task APlayerWhoCannotAffordItIsToldSoAndStaysPut()
    {
        var session = await SessionAsync(Content(currency: 200));

        var response = Assert.Single(await PickAsync(session, 51, 1));

        Assert.Contains("\"type\":\"npc.choiceResult\"", response);
        Assert.Contains("\"result\":\"notEnoughMoney\"", response);
        Assert.Equal(new Ubiquitous.Common.Position(0f, 0f, 0f), session.PlayerPosition);

        Assert.Contains("notEnoughMoney", Assert.Single(await PickAsync(session, 51, 1)));
    }

    [Fact]
    public async Task AnNpcOutOfReachSellsNothing()
    {
        var session = await SessionAsync(Content(), npcX: 12f);

        var response = Assert.Single(await PickAsync(session, 51, 1));

        Assert.Contains("invalid_gossip", response);
        Assert.Contains("Too far away", response);
        Assert.Equal(new Ubiquitous.Common.Position(0f, 0f, 0f), session.PlayerPosition);
    }

    [Fact]
    public async Task AnNpcAtTheEdgeOfViewSellsNothing()
    {
        var session = await SessionAsync(Content(), npcX: 57f);

        var response = Assert.Single(await PickAsync(session, 51, 1));

        Assert.Contains("Too far away", response);
        Assert.Equal(new Ubiquitous.Common.Position(0f, 0f, 0f), session.PlayerPosition);
    }

    [Fact]
    public async Task AChoiceThatSellsNoTeleportSaysNothingHappens()
    {
        var session = await SessionAsync(Content());

        var response = Assert.Single(await PickAsync(session, 50, 2));

        Assert.Contains("\"result\":\"nothingHappens\"", response);
    }
}
