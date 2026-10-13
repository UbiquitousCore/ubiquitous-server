using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class HeldAuraTests
{
    private const uint Slow = 7401;
    private const uint Shield = 7402;
    private const uint Hide = 7403;
    private const int SlowMs = 10_000;
    private const int ShieldMs = 300_000;
    private const int HideMs = 60_000;

    private sealed record Side(PlaySession Session, List<string> Inbox, string Id);

    private static readonly GameContent Content = GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [Slow] = new(Slow, "spell-7401", 0, 0, 30f, PowerCost: 0, AuraSlowPercent: 50, DurationMs: SlowMs),
            [Shield] = new(Shield, "spell-7402", 0, 0, 0f, PowerCost: 0, DurationMs: ShieldMs,
                AuraAbsorbAmount: 15, AuraAbsorbPercent: 100),
            [Hide] = new(Hide, "spell-7403", 0, 0, 0f, PowerCost: 0, AuraStealth: true, DurationMs: HideMs),
        },
        StartingSpells = [.. GameContent.Development.StartingSpells, Slow, Shield, Hide],
    };

    [Fact]
    public async Task APlayerWalkingUpToACreatureSomeoneElseSlowed_IsToldItIsSlowed_WithTheTimeItHasLeft()
    {
        var (hub, maps) = (new PlaySessionHub(), new SharedMapRegistry());
        var ayla = await JoinAsync(hub, maps, "h1", "Ayla");
        var borin = await JoinAsync(hub, maps, "h1", "Borin", x: 100);
        Assert.Empty(Spawns(borin.Inbox, "Creature:1"));

        await CastAsync(hub, ayla, Slow, "Creature:1");
        Assert.Empty(PayloadsOf(ayla.Inbox, WebMessageTypes.SpellFailed));
        for (var x = 85; x >= 1; x -= 12) await MoveAsync(hub, borin, x);

        var worn = Assert.Single(Spawns(borin.Inbox, "Creature:1")).GetProperty("auras");
        var slow = Assert.Single(worn.EnumerateArray());
        Assert.Equal(Slow, slow.GetProperty("spellId").GetUInt32());
        Assert.Equal(ayla.Id, slow.GetProperty("casterId").GetString());
        Assert.Equal(1, slow.GetProperty("harmful").GetInt32());
        Assert.Equal(SlowMs, slow.GetProperty("maxDuration").GetInt32());
        Assert.InRange(slow.GetProperty("remaining").GetInt32(), 1, SlowMs);
        Assert.False(Assert.Single(Spawns(borin.Inbox, "Creature:2")).TryGetProperty("auras", out _));
    }

    [Fact]
    public async Task ACreatureWhoseSlowWasTakenOff_ArrivesWearingNothing()
    {
        var (hub, maps) = (new PlaySessionHub(), new SharedMapRegistry());
        var ayla = await JoinAsync(hub, maps, "h2", "Ayla");
        var borin = await JoinAsync(hub, maps, "h2", "Borin", x: 100);
        await CastAsync(hub, ayla, Slow, "Creature:1");
        await hub.TickAsync(session => session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(SlowMs + 100)));
        Assert.NotEmpty(PayloadsOf(ayla.Inbox, WebMessageTypes.AuraEnded));

        for (var x = 85; x >= 1; x -= 12) await MoveAsync(hub, borin, x);

        Assert.False(Assert.Single(Spawns(borin.Inbox, "Creature:1")).TryGetProperty("auras", out _));
    }

    [Fact]
    public async Task SomeoneArrivingBesideAShieldedPlayer_IsToldTheyAreShielded()
    {
        var (hub, maps) = (new PlaySessionHub(), new SharedMapRegistry());
        var ayla = await JoinAsync(hub, maps, "h3", "Ayla");
        await CastAsync(hub, ayla, Shield, ayla.Id);
        Assert.Empty(PayloadsOf(ayla.Inbox, WebMessageTypes.SpellFailed));

        var borin = await JoinAsync(hub, maps, "h3", "Borin");

        var shield = Assert.Single(Assert.Single(Spawns(borin.Inbox, ayla.Id)).GetProperty("auras").EnumerateArray());
        Assert.Equal(Shield, shield.GetProperty("spellId").GetUInt32());
        Assert.Equal(ayla.Id, shield.GetProperty("casterId").GetString());
        Assert.Equal(0, shield.GetProperty("harmful").GetInt32());
        Assert.InRange(shield.GetProperty("remaining").GetInt32(), ShieldMs - 5000, ShieldMs);
        Assert.False(Assert.Single(Spawns(ayla.Inbox, borin.Id)).TryGetProperty("auras", out _));
    }

    [Fact]
    public async Task APlayerComingOutOfStealth_ComesBackOnAnOutsidersScreenStillShielded()
    {
        var (hub, maps) = (new PlaySessionHub(), new SharedMapRegistry());
        var ayla = await JoinAsync(hub, maps, "h4", "Ayla");
        var borin = await JoinAsync(hub, maps, "h4", "Borin");
        await CastAsync(hub, ayla, Shield, ayla.Id);
        await CastAsync(hub, ayla, Hide, ayla.Id);
        borin.Inbox.Clear();

        await hub.TickAsync(session => session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(HideMs + 1000)));

        var worn = Assert.Single(Spawns(borin.Inbox, ayla.Id)).GetProperty("auras").EnumerateArray()
            .Select(aura => aura.GetProperty("spellId").GetUInt32());
        Assert.Equal([Shield], worn);
    }

    [Fact]
    public async Task EnteringTheWorld_TellsTheHeroWhatItWears_WhichAfterACrossingIsNothing()
    {
        var (hub, maps) = (new PlaySessionHub(), new SharedMapRegistry());
        var ayla = await JoinAsync(hub, maps, "h5", "Ayla");
        Assert.False(Assert.Single(PayloadsOf(ayla.Inbox, WebMessageTypes.WorldEntered)).TryGetProperty("auras", out _));
        var borin = await JoinAsync(hub, maps, "h5", "Borin");
        await CastAsync(hub, ayla, Shield, ayla.Id);
        ayla.Inbox.Clear();

        await SendAsync(hub, ayla, WebMessageTypes.BusConfirm, new { from = 1, to = 3 });

        var entered = Assert.Single(PayloadsOf(ayla.Inbox, WebMessageTypes.WorldEntered));
        Assert.Equal(14, entered.GetProperty("map").GetInt32());
        Assert.False(entered.TryGetProperty("auras", out _));

        Assert.Empty(maps.For(13).Auras.On(ayla.Id, DateTimeOffset.UtcNow));
        Assert.Single(PayloadsOf(borin.Inbox, WebMessageTypes.EntityRemoved), p => p.GetProperty("networkId").GetString() == ayla.Id);
    }

    private static async Task<Side> JoinAsync(PlaySessionHub hub, SharedMapRegistry maps, string tag, string name, float x = 1)
    {
        var guid = $"char-{tag}{name}";
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, x, 2, 3, 4, [100, 200])]),
            TravelSession.Spawns, TravelSession.StaticSpawns,
            content: Content, combatRollUnit: () => 1.0, spellRollUnit: () => 0.5, sharedMaps: maps);
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });
        var side = new Side(session, inbox, $"player:{guid}");
        await SendAsync(hub, side, WebMessageTypes.AccountLogin, new LoginRequest("developer", "dev-secret"));
        await SendAsync(hub, side, WebMessageTypes.CharactersChoose, new ChooseCharacterRequest(name));
        return side;
    }

    private static Task CastAsync(PlaySessionHub hub, Side side, uint spellId, string targetId) =>
        SendAsync(hub, side, WebMessageTypes.SpellRequest, new { spellId, targetId });

    private static Task MoveAsync(PlaySessionHub hub, Side side, float x) =>
        SendAsync(hub, side, WebMessageTypes.PlayerMove, new { x, y = 2f, z = 3f, yaw = 0f, moving = true, state = 0, speed = 7f });

    private static Task SendAsync(PlaySessionHub hub, Side side, string type, object payload) =>
        hub.SendInOrderAsync(side.Session, () => side.Session.HandleTextAsync(
            WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 1, payload))));

    private static IEnumerable<JsonElement> PayloadsOf(IEnumerable<string> inbox, string type) =>
        inbox.Select(message => JsonDocument.Parse(message).RootElement)
            .Where(root => root.GetProperty("type").GetString() == type)
            .Select(root => root.GetProperty("payload").Clone());

    private static IEnumerable<JsonElement> Spawns(IEnumerable<string> inbox, string id) =>
        PayloadsOf(inbox, WebMessageTypes.EntitySpawned).Where(p => p.GetProperty("networkId").GetString() == id);
}
