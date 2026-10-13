using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class StealthVisibilityTests
{
    private const uint Hide = 980;
    private const uint Bolt = 985;
    private const int HideMs = 60_000;

    private sealed record Side(PlaySession Session, List<string> Inbox, string Id);

    private static readonly GameContent Content = GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [Hide] = new(Hide, "spell-980", 0, 0, 0f, PowerCost: 0, AuraStealth: true, DurationMs: HideMs),
            [Bolt] = new(Bolt, "spell-985", 0, 0, 30f, PowerCost: 0, EffectDamage: 5),
        },
        StartingSpells = [.. GameContent.Development.StartingSpells, Hide, Bolt],
    };

    [Fact]
    public async Task GoingIntoStealth_TakesTheBodyOffAnOutsidersScreen_AndNothingItDoesFollows()
    {
        var (hub, sides) = await WorldAsync("s1", "Ayla", "Borin");
        var (ayla, borin) = (sides[0], sides[1]);

        await CastAsync(hub, ayla, Hide, ayla.Id);

        Assert.Single(Removals(borin.Inbox, ayla.Id));
        Assert.Empty(PayloadsOf(borin.Inbox, WebMessageTypes.AuraApplied));
        borin.Inbox.Clear();

        await SendAsync(hub, ayla, WebMessageTypes.PlayerMove, new PlayerMovePayload(5, 2, 3, 0, true, MovementState.Walking));
        await TickAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));

        Assert.DoesNotContain(borin.Inbox, message => message.Contains($"\"{ayla.Id}\""));
    }

    [Fact]
    public async Task AnOutsider_CannotSelectAttackCastAtOrAskAnythingOfAStealthedPlayer()
    {
        var (hub, sides) = await WorldAsync("s2", "Ayla", "Borin");
        var (ayla, borin) = (sides[0], sides[1]);
        await SendAsync(hub, ayla, WebMessageTypes.PvpFlagSet, new { flagged = true });
        await SendAsync(hub, borin, WebMessageTypes.PvpFlagSet, new { flagged = true });
        await CastAsync(hub, ayla, Hide, ayla.Id);

        Assert.Contains(await SendAsync(hub, borin, WebMessageTypes.TargetChange, new { networkId = ayla.Id }), IsRefusal);
        Assert.Contains(await SendAsync(hub, borin, WebMessageTypes.AttackEngage, new { targetId = ayla.Id }), IsRefusal);
        var cast = await CastAsync(hub, borin, Bolt, ayla.Id);
        Assert.Contains(cast, message => message.Contains("\"type\":\"spell.failed\""));
        Assert.Contains(await SendAsync(hub, borin, WebMessageTypes.DuelChallenge, new { targetId = ayla.Id }), IsRefusal);
        Assert.Contains(await SendAsync(hub, borin, WebMessageTypes.TradeRequest, new { targetId = ayla.Id }), IsRefusal);
        Assert.Contains(await SendAsync(hub, borin, WebMessageTypes.PartyInvite, new { networkId = ayla.Id }), IsRefusal);
        await TickAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(ayla.Inbox, message => message.Contains("\"type\":\"spell.outcome\""));
    }

    [Fact]
    public async Task AnOutsiderAlreadyFightingAPlayerWhoGoesIntoStealth_StopsSwinging()
    {
        var (hub, sides) = await WorldAsync("s9", "Ayla", "Borin");
        var (ayla, borin) = (sides[0], sides[1]);
        await SendAsync(hub, ayla, WebMessageTypes.PvpFlagSet, new { flagged = true });
        await SendAsync(hub, borin, WebMessageTypes.PvpFlagSet, new { flagged = true });
        Assert.DoesNotContain(await SendAsync(hub, borin, WebMessageTypes.AttackEngage, new { targetId = ayla.Id }), IsRefusal);

        await CastAsync(hub, ayla, Hide, ayla.Id);
        await TickAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(3));

        Assert.Contains(PayloadsOf(borin.Inbox, WebMessageTypes.AttackHalted),
            halted => halted.GetProperty("networkId").GetString() == borin.Id);
        Assert.Empty(AurasEnded(ayla.Inbox, ayla.Id, Hide));
    }

    [Fact]
    public async Task StealthRunningOut_SpawnsTheBodyOnTheOutsidersScreenAgain()
    {
        var (hub, sides) = await WorldAsync("s3", "Ayla", "Borin");
        var (ayla, borin) = (sides[0], sides[1]);
        await CastAsync(hub, ayla, Hide, ayla.Id);
        borin.Inbox.Clear();

        await TickAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(HideMs + 1000));

        Assert.Single(Spawns(borin.Inbox, ayla.Id));
    }

    [Fact]
    public async Task TheStealthedPlayersTeam_StillSeesThemFaded_AndCanSelectThem()
    {
        var (hub, sides) = await WorldAsync("s4", "Ayla", "Tam", "Oren");
        var (ayla, tam, oren) = (sides[0], sides[1], sides[2]);
        await TeamAsync(hub, ayla, tam);

        await CastAsync(hub, ayla, Hide, ayla.Id);
        await TickAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));

        Assert.Empty(Removals(tam.Inbox, ayla.Id));
        Assert.Contains(PayloadsOf(tam.Inbox, WebMessageTypes.EntityValuesChanged), update =>
            update.GetProperty("networkId").GetString() == ayla.Id
            && update.GetProperty("changed").TryGetProperty(WireCodes.UnitValue.Stealthed, out var value) && value.GetInt32() == 1);
        Assert.DoesNotContain(await SendAsync(hub, tam, WebMessageTypes.TargetChange, new { networkId = ayla.Id }), IsRefusal);
        Assert.Single(Removals(oren.Inbox, ayla.Id));
    }

    [Fact]
    public async Task SomeoneArrivingWhileAPlayerIsStealthed_IsNotShownThem_UnlessOnTheirTeam()
    {
        var (hub, sides) = await WorldAsync("s5", "Ayla", "Tam");
        var (ayla, tam) = (sides[0], sides[1]);
        await TeamAsync(hub, ayla, tam);
        await CastAsync(hub, ayla, Hide, ayla.Id);

        var late = await JoinAsync(hub, "s5", "Late");
        var reloaded = await JoinAsync(hub, "s5", "Tam");

        Assert.Empty(Spawns(late.Inbox, ayla.Id));
        Assert.Single(Spawns(reloaded.Inbox, ayla.Id));
        Assert.Contains(PayloadsOf(reloaded.Inbox, WebMessageTypes.EntityValuesChanged), update =>
            update.GetProperty("networkId").GetString() == ayla.Id);
    }

    [Fact]
    public async Task Attacking_EndsStealth_AndTheBodyComesBack()
    {
        var (hub, sides) = await WorldAsync("s6", "Ayla", "Borin");
        var (ayla, borin) = (sides[0], sides[1]);
        await CastAsync(hub, ayla, Hide, ayla.Id);
        borin.Inbox.Clear();

        var engaged = await SendAsync(hub, ayla, WebMessageTypes.AttackEngage, new { targetId = "Creature:1" });

        Assert.Contains(engaged, message => message.Contains("\"type\":\"attack.meleeStart\""));
        Assert.Single(AurasEnded(ayla.Inbox, ayla.Id, Hide));
        Assert.Single(Spawns(borin.Inbox, ayla.Id));
    }

    [Fact]
    public async Task StartingAHarmfulCast_EndsStealth()
    {
        var (hub, sides) = await WorldAsync("s7", "Ayla", "Borin");
        var (ayla, borin) = (sides[0], sides[1]);
        await CastAsync(hub, ayla, Hide, ayla.Id);
        borin.Inbox.Clear();

        await CastAsync(hub, ayla, Bolt, "Creature:1");

        Assert.Single(AurasEnded(ayla.Inbox, ayla.Id, Hide));
        Assert.Single(Spawns(borin.Inbox, ayla.Id));
        var order = borin.Inbox.Select(message => JsonDocument.Parse(message).RootElement.GetProperty("type").GetString()).ToList();
        Assert.True(order.IndexOf(WebMessageTypes.EntitySpawned) < order.IndexOf(WebMessageTypes.SpellReleased));
    }

    [Fact]
    public async Task BeingHurt_EndsStealth()
    {
        var (hub, sides) = await WorldAsync("s8", "Ayla", "Tam", "Oren");
        var (ayla, tam, oren) = (sides[0], sides[1], sides[2]);
        await TeamAsync(hub, ayla, tam);
        await SendAsync(hub, tam, WebMessageTypes.DuelChallenge, new { targetId = ayla.Id });
        await TickAsync(hub, DateTimeOffset.UtcNow);
        await SendAsync(hub, ayla, WebMessageTypes.DuelAccept, new { });
        await TickAsync(hub, DateTimeOffset.UtcNow);
        await CastAsync(hub, ayla, Hide, ayla.Id);
        Assert.Single(Removals(oren.Inbox, ayla.Id));
        oren.Inbox.Clear();

        await CastAsync(hub, tam, Bolt, ayla.Id);
        await TickAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(10));
        await TickAsync(hub, DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(20));

        Assert.Contains(PayloadsOf(ayla.Inbox, WebMessageTypes.SpellOutcome), outcome =>
            outcome.GetProperty("targetId").GetString() == ayla.Id && outcome.GetProperty("damage").GetInt32() > 0);
        Assert.Single(AurasEnded(ayla.Inbox, ayla.Id, Hide));
        Assert.Single(Spawns(oren.Inbox, ayla.Id));
    }

    private static bool IsRefusal(string message) => message.Contains("\"type\":\"protocol.error\"");

    private static async Task<(PlaySessionHub Hub, Side[] Sides)> WorldAsync(string tag, params string[] names)
    {
        var hub = new PlaySessionHub();
        var sides = new List<Side>();
        foreach (var name in names) sides.Add(await JoinAsync(hub, tag, name));
        foreach (var side in sides) side.Inbox.Clear();
        return (hub, [.. sides]);
    }

    private static readonly DuelService Duels = new(countdown: TimeSpan.Zero);
    private static readonly PvpFlagService Flags = new();
    private static readonly PlayerBlowRelay Blows = new();

    private static async Task<Side> JoinAsync(PlaySessionHub hub, string tag, string name)
    {
        var guid = $"char-{tag}{name}";
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            creatureSpawns: [new CreatureSpawnDefinition(1001, 13, 9001, 3, 2, 3, 0)],
            content: Content, combatRollUnit: () => 1.0, spellRollUnit: () => 0.5,
            duels: Duels, pvpFlags: Flags, blows: Blows, parties: TestSessions.PartiesOf(hub));
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });
        var side = new Side(session, inbox, $"player:{guid}");
        await SendAsync(hub, side, WebMessageTypes.AccountLogin, new LoginRequest("developer", "dev-secret"));
        await SendAsync(hub, side, WebMessageTypes.CharactersChoose, new ChooseCharacterRequest(name));
        return side;
    }

    private static async Task TeamAsync(PlaySessionHub hub, Side leader, Side member)
    {
        await SendAsync(hub, leader, WebMessageTypes.PartyInvite, new { networkId = member.Id });
        await SendAsync(hub, member, WebMessageTypes.PartyAccept, new { });
    }

    private static Task<IReadOnlyList<string>> CastAsync(PlaySessionHub hub, Side side, uint spellId, string targetId) =>
        SendAsync(hub, side, WebMessageTypes.SpellRequest, new { spellId, targetId });

    private static Task TickAsync(PlaySessionHub hub, DateTimeOffset now) =>
        hub.TickAsync(session => session.TickCombat(now));

    private static async Task<IReadOnlyList<string>> SendAsync(PlaySessionHub hub, Side side, string type, object payload)
    {
        IReadOnlyList<string> replies = [];
        await hub.SendInOrderAsync(side.Session, async () => replies = await side.Session.HandleTextAsync(
            WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 1, payload))));
        return replies;
    }

    private static IEnumerable<JsonElement> PayloadsOf(IEnumerable<string> inbox, string type) =>
        inbox.Select(message => JsonDocument.Parse(message).RootElement)
            .Where(root => root.GetProperty("type").GetString() == type)
            .Select(root => root.GetProperty("payload").Clone());

    private static IEnumerable<JsonElement> Removals(IEnumerable<string> inbox, string id) =>
        PayloadsOf(inbox, WebMessageTypes.EntityRemoved).Where(p => p.GetProperty("networkId").GetString() == id);

    private static IEnumerable<JsonElement> Spawns(IEnumerable<string> inbox, string id) =>
        PayloadsOf(inbox, WebMessageTypes.EntitySpawned).Where(p => p.GetProperty("networkId").GetString() == id);

    private static IEnumerable<JsonElement> AurasEnded(IEnumerable<string> inbox, string unit, uint spellId) =>
        PayloadsOf(inbox, WebMessageTypes.AuraEnded).Where(p =>
            p.GetProperty("unitId").GetString() == unit && p.GetProperty("spellId").GetUInt32() == spellId);
}
