using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class MiningTests
{
    private static readonly StaticObjectSpawnDefinition[] Locations =
    [
        new(3001, 13, MiningRules.LocationEntry, 2f, 2f, 4f, 0f),
        new(3002, 13, MiningRules.LocationEntry, 3f, 2f, 4f, 0f),
        new(3003, 13, MiningRules.LocationEntry, 4f, 2f, 4f, 0f),
        new(3004, 13, MiningRules.LocationEntry, 5f, 2f, 4f, 0f),
    ];

    private static readonly uint OreItem = MiningOres.Bronze.ItemId;

    private sealed record Miner(PlaySession Session, FakeTimeProvider Clock, string NodeId);

    [Fact]
    public async Task EnteringTheWorld_ShowsTheActiveLocationAsOreAndHidesTheMarkers()
    {
        var (_, entered) = await EnterAsync();

        var spawned = entered.Where(m => m.Contains("\"type\":\"entity.spawned\"")).ToArray();
        var node = Assert.Single(spawned, m => m.Contains("\"kind\":\"staticObject\""));
        Assert.Contains($"\"entry\":{MiningOres.Bronze.NodeEntry}", node);
        Assert.DoesNotContain($"\"entry\":{MiningRules.LocationEntry}", node);
    }

    [Fact]
    public async Task TappingTheOre_StartsTheGatherCastWithTheMineSpellAndItsCastTime()
    {
        var miner = await MinerAsync();

        var tapped = await TapAsync(miner);

        var casting = Assert.Single(tapped);
        Assert.Contains("\"type\":\"spell.casting\"", casting);
        Assert.Contains($"\"spellId\":{MiningRules.GatherSpellId}", casting);
        Assert.Contains($"\"castTime\":{MiningRules.DefaultCastTimeMs}", casting);
    }

    [Fact]
    public async Task AMendelGathersInHalfTheCastTime()
    {
        var miner = await MinerAsync(race: PlayerRaceIds.Mendel);

        var tapped = await TapAsync(miner);

        Assert.Contains($"\"castTime\":{MiningRules.DefaultCastTimeMs / 2}", Assert.Single(tapped));
    }

    [Fact]
    public async Task TheCastTimeComesFromTheMineSpellRowWhenThereIsOne()
    {
        var gather = new SpellDefinition(MiningRules.GatherSpellId, "spell-23", CastTimeMs: 2000, CooldownMs: 0, MaxRange: 0f);
        var miner = await MinerAsync(spells: new Dictionary<uint, SpellDefinition> { [gather.Id] = gather });

        var tapped = await TapAsync(miner);

        Assert.Contains("\"castTime\":2000", Assert.Single(tapped));
    }

    [Fact]
    public async Task BeforeTheCastEnds_NothingHappens()
    {
        var miner = await MinerAsync();
        await TapAsync(miner);

        miner.Clock.Advance(TimeSpan.FromMilliseconds(MiningRules.DefaultCastTimeMs - 1));

        Assert.Empty(miner.Session.TickCombat(miner.Clock.GetUtcNow()));
    }

    [Fact]
    public async Task WhenTheCastEnds_TheLootWindowOpensWithTheOre_AndTheNodeStays()
    {
        var miner = await MinerAsync();
        await TapAsync(miner);

        var finished = await FinishAsync(miner);

        Assert.Contains(finished, m => m.Contains("\"type\":\"spell.released\""));
        var loot = Assert.Single(finished, m => m.Contains("\"type\":\"loot.contents\""));
        Assert.Contains($"\"networkId\":\"{miner.NodeId}\"", loot);
        Assert.Contains($"\"entry\":{OreItem}", loot);
        Assert.Contains(finished, m => m.Contains("\"type\":\"loot.busy\""));
        Assert.DoesNotContain(finished, m => m.Contains("entity.removed"));
    }

    [Fact]
    public async Task TakingTheOre_PutsItInTheBag_AndOnlyThenTheNodeLeaves()
    {
        var miner = await MinerAsync();
        await TapAsync(miner);
        await FinishAsync(miner);

        var taken = await miner.Session.HandleTextAsync(Message(WebMessageTypes.LootTake,
            new { networkId = miner.NodeId, slot = 0 }));

        Assert.Contains(taken, m => m.Contains("\"type\":\"loot.slotEmptied\""));
        Assert.Contains(taken, m => m.Contains("\"type\":\"inventory.itemAdded\"") && m.Contains($"\"entry\":{OreItem}"));
        Assert.DoesNotContain(taken, m => m.Contains("entity.lootable"));
        var removed = Assert.Single(taken, m => m.Contains("\"type\":\"entity.removed\""));
        Assert.Contains($"\"networkId\":\"{miner.NodeId}\"", removed);
    }

    [Fact]
    public async Task ClosingTheWindowWithoutTakingTheOre_LeavesTheNode_AndTappingAgainStartsANewCast()
    {
        var miner = await MinerAsync();
        await TapAsync(miner);
        await FinishAsync(miner);

        var closed = await miner.Session.HandleTextAsync(Message(WebMessageTypes.LootClose, new { networkId = miner.NodeId }));
        var again = await TapAsync(miner);

        Assert.DoesNotContain(closed, m => m.Contains("entity.removed"));
        Assert.Contains("\"type\":\"spell.casting\"", Assert.Single(again));
        var second = await FinishAsync(miner);
        Assert.Contains(second, m => m.Contains("\"type\":\"loot.contents\""));
    }

    [Fact]
    public async Task WalkingAway_InterruptsTheCast_AndNothingCarriesOn()
    {
        var miner = await MinerAsync();
        await TapAsync(miner);

        await miner.Session.HandleTextAsync(Message(WebMessageTypes.PlayerMove,
            new PlayerMovePayload(2.5, 2, 3.5, 0.5, true, MovementState.Walking)));
        miner.Clock.Advance(TimeSpan.FromSeconds(1));
        var interrupted = miner.Session.TickCombat(miner.Clock.GetUtcNow());
        miner.Clock.Advance(TimeSpan.FromSeconds(10));
        var later = miner.Session.TickCombat(miner.Clock.GetUtcNow());

        Assert.Contains(interrupted, m => m.Contains("\"type\":\"spell.interrupted\""));
        Assert.DoesNotContain(later, m => m.Contains("loot.contents"));
    }

    [Fact]
    public async Task TappingAfterAnInterruption_StartsANewCast()
    {
        var miner = await MinerAsync();
        await TapAsync(miner);
        await miner.Session.HandleTextAsync(Message(WebMessageTypes.SpellAbort, new { }));

        var again = await TapAsync(miner);

        Assert.Contains("\"type\":\"spell.casting\"", Assert.Single(again));
    }

    [Fact]
    public async Task TheOreCannotBeTakenTwice()
    {
        var miner = await MinerAsync();
        await TapAsync(miner);
        await FinishAsync(miner);
        var take = Message(WebMessageTypes.LootTake, new { networkId = miner.NodeId, slot = 0 });
        await miner.Session.HandleTextAsync(take);

        var again = await miner.Session.HandleTextAsync(take);

        Assert.Empty(again);
    }

    [Fact]
    public async Task AbortingTheCast_StopsTheGathering()
    {
        var miner = await MinerAsync();
        await TapAsync(miner);

        var aborted = await miner.Session.HandleTextAsync(Message(WebMessageTypes.SpellAbort, new { }));

        Assert.Contains("\"type\":\"spell.interrupted\"", Assert.Single(aborted));
        miner.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(miner.Session.TickCombat(miner.Clock.GetUtcNow()));
    }

    [Fact]
    public async Task TappingWhileAlreadyGathering_SaysNothing_AndTheCastCarriesOn()
    {
        var miner = await MinerAsync();
        await TapAsync(miner);

        var again = await TapAsync(miner);
        var finished = await FinishAsync(miner);

        Assert.Empty(again);
        Assert.Contains(finished, m => m.Contains("\"type\":\"loot.contents\""));
    }

    [Fact]
    public async Task TheNextActiveLocationAppearsOnceTheOreIsTaken()
    {
        var miner = await MinerAsync();
        await TapAsync(miner);
        await FinishAsync(miner);
        await miner.Session.HandleTextAsync(Message(WebMessageTypes.LootTake, new { networkId = miner.NodeId, slot = 0 }));

        var later = miner.Session.TickCombat(miner.Clock.GetUtcNow());

        var spawned = Assert.Single(later, m => m.Contains("\"type\":\"entity.spawned\""));
        Assert.DoesNotContain($"\"networkId\":\"{miner.NodeId}\"", spawned);
        Assert.Contains("\"kind\":\"staticObject\"", spawned);
    }

    [Fact]
    public async Task ABagWithNoRoom_RefusesTheOreAndLeavesItInTheWindow()
    {
        var miner = await MinerAsync(fullBag: true);
        await TapAsync(miner);
        await FinishAsync(miner);

        var taken = await miner.Session.HandleTextAsync(Message(WebMessageTypes.LootTake,
            new { networkId = miner.NodeId, slot = 0 }));

        Assert.Contains("\"type\":\"item.refused\"", Assert.Single(taken));
        var reopened = await miner.Session.HandleTextAsync(Message(WebMessageTypes.LootOpen, new { networkId = miner.NodeId }));
        Assert.Contains(reopened, m => m.Contains("\"type\":\"loot.contents\"") && m.Contains($"\"entry\":{OreItem}"));
    }

    private static IReadOnlyDictionary<uint, SpellDefinition> Overridden(
        IReadOnlyDictionary<uint, SpellDefinition> known, IReadOnlyDictionary<uint, SpellDefinition> replacements)
    {
        var merged = new Dictionary<uint, SpellDefinition>(known);
        foreach (var (id, spell) in replacements) merged[id] = spell;
        return merged;
    }

    private static string Message(string type, object payload) =>
        WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 5, payload));

    private static async Task<IReadOnlyList<string>> TapAsync(Miner miner) =>
        await miner.Session.HandleTextAsync(Message(WebMessageTypes.StaticObjectInteract, new { networkId = miner.NodeId }));

    private static async Task<IReadOnlyList<string>> FinishAsync(Miner miner)
    {
        miner.Clock.Advance(TimeSpan.FromSeconds(MiningRules.DefaultCastTimeMs / 1000.0 + 1));
        return miner.Session.TickCombat(miner.Clock.GetUtcNow());
    }

    private static async Task<Miner> MinerAsync(
        int race = PlayerRaceIds.Orc, IReadOnlyDictionary<uint, SpellDefinition>? spells = null, bool fullBag = false)
    {
        var (session, entered, clock) = await EnterWithClockAsync(race, spells, fullBag);
        var node = entered.Where(m => m.Contains("\"kind\":\"staticObject\""))
            .Select(m => JsonDocument.Parse(m).RootElement.GetProperty("payload").GetProperty("networkId").GetString()!)
            .Single();
        return new Miner(session, clock, node);
    }

    private static async Task<(PlaySession Session, IReadOnlyList<string> Entered)> EnterAsync()
    {
        var (session, entered, _) = await EnterWithClockAsync(PlayerRaceIds.Orc, null, false);
        return (session, entered);
    }

    private static async Task<(PlaySession Session, IReadOnlyList<string> Entered, FakeTimeProvider Clock)> EnterWithClockAsync(
        int race, IReadOnlyDictionary<uint, SpellDefinition>? spells, bool fullBag)
    {
        var ore = new ItemDefinition(OreItem, "item-bronze-mineral", Class: 6, SubClass: 7, DisplayInfoId: 0);
        var baseContent = GameContent.Development;
        var content = baseContent with
        {
            Items = new Dictionary<uint, ItemDefinition>(baseContent.Items) { [ore.Id] = ore },
            Spells = spells is null ? baseContent.Spells : Overridden(baseContent.Spells, spells),
            StartingItems = fullBag
                ? [.. Enumerable.Range(0, 12).Select(index => new StartingItemStack(checked((uint)(100 + index)), 1))]
                : baseContent.StartingItems,
        };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 12, [100, 200]) with { Race = race }]),
            [], Locations, content, wanderRollUnit: () => 0.0, time: clock);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        var entered = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return (session, entered, clock);
    }
}
