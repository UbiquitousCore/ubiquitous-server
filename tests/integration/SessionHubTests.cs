using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class SessionHubTests
{
    [Fact]
    public async Task Broadcast_SendsWorldEventsToOtherSessionsOnly()
    {
        var hub = new PlaySessionHub();
        var first = TestSessions.Create(
            new Ubiquitous.Persistence.DevelopmentAccountStore("a", "p"),
            new Ubiquitous.Persistence.InMemoryCharacterStore([]));
        var second = TestSessions.Create(
            new Ubiquitous.Persistence.DevelopmentAccountStore("b", "p"),
            new Ubiquitous.Persistence.InMemoryCharacterStore([]));
        var received = new List<string>();
        hub.Register(first, _ => Task.CompletedTask);
        hub.Register(second, message => { received.Add(message); return Task.CompletedTask; });

        await hub.BroadcastAsync(first,
        [
            "{\"type\":\"entity.moved\",\"sequence\":0,\"payload\":{}}",
            "{\"type\":\"chat.received\",\"sequence\":1,\"payload\":{}}",
        ]);

        Assert.Single(received);
        Assert.Contains("entity.moved", received[0]);
        Assert.Contains("\"sequence\":0", received[0]);
    }

    [Fact]
    public async Task Tick_SendsPumpResultsToOwnerAndWorldEventsToObservers()
    {
        var hub = new PlaySessionHub();
        var first = TestSessions.Create(
            new Ubiquitous.Persistence.DevelopmentAccountStore("a", "p"),
            new Ubiquitous.Persistence.InMemoryCharacterStore([]));
        var second = TestSessions.Create(
            new Ubiquitous.Persistence.DevelopmentAccountStore("b", "p"),
            new Ubiquitous.Persistence.InMemoryCharacterStore([]));
        var owner = new List<string>();
        var observer = new List<string>();
        hub.Register(first, message => { owner.Add(message); return Task.CompletedTask; });
        hub.Register(second, message => { observer.Add(message); return Task.CompletedTask; });

        await hub.TickAsync(_ =>
        [
            "{\"type\":\"attack.swingResult\",\"sequence\":0,\"payload\":{}}",
            "{\"type\":\"chat.received\",\"sequence\":1,\"payload\":{}}",
        ]);

        Assert.Equal(3, owner.Count);
        Assert.Equal(3, observer.Count);
        Assert.Contains(observer, message => message.Contains("attack.swingResult"));
    }

    [Fact]
    public async Task Broadcast_DoesNotFanOutCreatureVisibilityToOtherSessions()
    {
        var hub = new PlaySessionHub();
        var first = TestSessions.Create(
            new Ubiquitous.Persistence.DevelopmentAccountStore("a", "p"),
            new Ubiquitous.Persistence.InMemoryCharacterStore([]));
        var second = TestSessions.Create(
            new Ubiquitous.Persistence.DevelopmentAccountStore("b", "p"),
            new Ubiquitous.Persistence.InMemoryCharacterStore([]));
        var received = new List<string>();
        hub.Register(first, _ => Task.CompletedTask);
        hub.Register(second, message => { received.Add(message); return Task.CompletedTask; });

        await hub.BroadcastAsync(first,
        [
            "{\"type\":\"entity.spawned\",\"sequence\":0,\"payload\":{\"networkId\":\"Creature:1\"}}",
            "{\"type\":\"entity.removed\",\"sequence\":1,\"payload\":{\"networkId\":\"Creature:1\"}}",
            "{\"type\":\"entity.removed\",\"sequence\":2,\"payload\":{\"networkId\":\"StaticObject:1\"}}",
            "{\"type\":\"entity.removed\",\"sequence\":3,\"payload\":{\"networkId\":\"player:abc\"}}",
            "{\"type\":\"entity.moved\",\"sequence\":4,\"payload\":{}}",
        ]);

        Assert.Equal(2, received.Count);
        Assert.Contains(received, message => message.Contains("player:abc"));
        Assert.Contains(received, message => message.Contains("entity.moved"));
    }

    [Fact]
    public async Task ObserverEncoding_UsesItsOwnSequenceCounter()
    {
        var session = TestSessions.Create(
            new Ubiquitous.Persistence.DevelopmentAccountStore("a", "p"),
            new Ubiquitous.Persistence.InMemoryCharacterStore([]));
        var first = session.EncodeObserverEvent(
            OutboundEvent.From("{\"type\":\"entity.moved\",\"sequence\":99,\"payload\":{}}")!);
        var second = session.EncodeObserverEvent(
            OutboundEvent.From("{\"type\":\"entity.moved\",\"sequence\":100,\"payload\":{}}")!);

        Assert.Contains("\"sequence\":0", first);
        Assert.Contains("\"sequence\":1", second);
    }

    private static readonly string[] MapWideTypes =
    [
        WebMessageTypes.EntityMoved, WebMessageTypes.EntitySpawned, WebMessageTypes.EntityRemoved,
        WebMessageTypes.EntityValuesChanged, WebMessageTypes.EntityAppearanceChanged,
        WebMessageTypes.AttackMeleeStart, WebMessageTypes.AttackSwingResult, WebMessageTypes.AttackShotResult,
        WebMessageTypes.SpellOutcome, WebMessageTypes.SpellCasting, WebMessageTypes.SpellReleased,
        WebMessageTypes.SpellInterrupted, WebMessageTypes.SpellFailed, WebMessageTypes.AuraApplied,
        WebMessageTypes.AuraEnded, WebMessageTypes.AttackHalted, WebMessageTypes.AttackEnterCombat,
        WebMessageTypes.AttackExitCombat, WebMessageTypes.UnitDied, WebMessageTypes.PlayerRevived,
        WebMessageTypes.LootBusy, WebMessageTypes.LootSlotEmptied, WebMessageTypes.DungeonCompleted,
        WebMessageTypes.EmotePerformed,
    ];

    // Loot belongs to the session that won it, so nobody else sees the corpse sparkle.
    [Fact]
    public async Task Broadcast_KeepsACorpsesLootableStateToItsLooter()
    {
        var hub = new PlaySessionHub();
        var first = TestSessions.Create(
            new Ubiquitous.Persistence.DevelopmentAccountStore("a", "p"), new Ubiquitous.Persistence.InMemoryCharacterStore([]));
        var second = TestSessions.Create(
            new Ubiquitous.Persistence.DevelopmentAccountStore("b", "p"), new Ubiquitous.Persistence.InMemoryCharacterStore([]));
        var received = new List<string>();
        hub.Register(first, _ => Task.CompletedTask);
        hub.Register(second, message => { received.Add(message); return Task.CompletedTask; });

        await hub.BroadcastAsync(first,
            ["{\"type\":\"entity.lootable\",\"sequence\":0,\"payload\":{\"networkId\":\"Creature:1\",\"state\":\"lootable\"}}"]);

        Assert.Empty(received);
    }

    [Fact]
    public async Task Broadcast_ForwardsExactlyTheMapWideTypesToOthers()
    {
        var hub = new PlaySessionHub();
        var first = TestSessions.Create(
            new Ubiquitous.Persistence.DevelopmentAccountStore("a", "p"), new Ubiquitous.Persistence.InMemoryCharacterStore([]));
        var second = TestSessions.Create(
            new Ubiquitous.Persistence.DevelopmentAccountStore("b", "p"), new Ubiquitous.Persistence.InMemoryCharacterStore([]));
        var received = new List<string>();
        hub.Register(first, _ => Task.CompletedTask);
        hub.Register(second, message => { received.Add(message); return Task.CompletedTask; });
        string[] notMapWide =
        [
            WebMessageTypes.ChatWhisperReceived, WebMessageTypes.InventoryUpdated, WebMessageTypes.PlayerValuesChanged,
            WebMessageTypes.QuestLog, WebMessageTypes.TradeRequested,
        ];

        await hub.BroadcastAsync(first, [.. MapWideTypes.Concat(notMapWide)
            .Select(type => $"{{\"type\":\"{type}\",\"sequence\":0,\"payload\":{{}}}}")]);

        Assert.Equal(MapWideTypes, received.Select(message => OutboundEvent.From(message)!.Type));
    }

    [Fact]
    public async Task Broadcast_KeepsCreatureSpawnsAndRemovalsToTheSessionThatSawThem()
    {
        var hub = new PlaySessionHub();
        var first = TestSessions.Create(
            new Ubiquitous.Persistence.DevelopmentAccountStore("a", "p"), new Ubiquitous.Persistence.InMemoryCharacterStore([]));
        var second = TestSessions.Create(
            new Ubiquitous.Persistence.DevelopmentAccountStore("b", "p"), new Ubiquitous.Persistence.InMemoryCharacterStore([]));
        var received = new List<string>();
        hub.Register(first, _ => Task.CompletedTask);
        hub.Register(second, message => { received.Add(message); return Task.CompletedTask; });

        await hub.BroadcastAsync(first,
        [
            "{\"type\":\"entity.spawned\",\"sequence\":0,\"payload\":{\"networkId\":\"Creature:1\"}}",
            "{\"type\":\"entity.removed\",\"sequence\":1,\"payload\":{\"networkId\":\"StaticObject:1\"}}",
            "{\"type\":\"entity.spawned\",\"sequence\":2,\"payload\":{\"networkId\":\"player:abc\"}}",
        ]);

        var delivered = Assert.Single(received);
        Assert.Contains("player:abc", delivered);
    }
}
