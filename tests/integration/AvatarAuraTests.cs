using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class AvatarAuraTests
{
    private const uint Cookie = 7309;
    private const uint Wand = 5945;
    private const uint CookieShape = 6727;
    private const uint WandShape = 2256;

    // A cookie turns the eater into another display for a minute; a wand's shape has no end of its own.
    private static readonly GameContent Content = GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [CookieShape] = new(CookieShape, "Change Boss (Ariel)", 0, 0, 0f,
                EffectTarget: SpellEffectTargets.Self, DurationMs: 60_000, AuraAvatarDisplayId: 2259),
            [WandShape] = new(WandShape, "Wand Goblin", 0, 0, 0f,
                EffectTarget: SpellEffectTargets.Self, AuraAvatarDisplayId: 1331),
        },
        Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
        {
            [Cookie] = new(Cookie, "item-7309", 0, 0, 0, OnUseSpellId: CookieShape),
            [Wand] = new(Wand, "item-5945", 0, 0, 0, OnUseSpellId: WandShape),
        },
        StartingItems = [new StartingItemStack(Cookie, 3), new StartingItemStack(Wand, 1)],
    };

    private static async Task<(PlaySession Session, List<string> Entry)> EnterAsync()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            content: Content, developmentEntry: true);
        var entry = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        return (session, [.. entry]);
    }

    // The bag slot and id the item sits under, read from the inventory the world was entered with.
    private static (int Index, string Id) Slot(IEnumerable<string> entry, uint entryId)
    {
        var inventory = entry.Last(m => m.Contains($"\"type\":\"{WebMessageTypes.InventoryUpdated}\""));
        using var document = JsonDocument.Parse(inventory);
        var item = document.RootElement.GetProperty("payload").GetProperty("items").EnumerateArray()
            .First(one => one.GetProperty("entry").GetUInt32() == entryId);
        return (item.GetProperty("slot").GetProperty("index").GetInt32(), item.GetProperty("id").GetString()!);
    }

    private static Task<IReadOnlyList<string>> UseAsync(PlaySession session, (int Index, string Id) slot) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 5, new { from = new { area = "bag", index = slot.Index }, itemId = slot.Id })));

    [Fact]
    public async Task EatingACookie_TurnsTheEaterIntoTheShape_ForAMinute_AndUsesTheCookieUp()
    {
        var (session, entry) = await EnterAsync();

        var eaten = await UseAsync(session, Slot(entry, Cookie));

        Assert.Contains(eaten, m => m.Contains($"\"type\":\"{WebMessageTypes.AuraApplied}\"")
            && m.Contains($"\"spellId\":{CookieShape}") && m.Contains("\"maxDuration\":60000"));
        Assert.Contains(eaten, m => m.Contains($"\"type\":\"{WebMessageTypes.InventoryStackChanged}\"")
            || m.Contains($"\"type\":\"{WebMessageTypes.InventoryItemRemoved}\""));
    }

    [Fact]
    public async Task TheShapeEndsOnItsOwn_WhenItsMinuteIsUp()
    {
        var (session, entry) = await EnterAsync();
        await UseAsync(session, Slot(entry, Cookie));

        var early = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30));
        var late = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(75));

        Assert.DoesNotContain(early, m => m.Contains($"\"type\":\"{WebMessageTypes.AuraEnded}\""));
        Assert.Contains(late, m => m.Contains($"\"type\":\"{WebMessageTypes.AuraEnded}\"") && m.Contains($"\"spellId\":{CookieShape}"));
    }

    [Fact]
    public async Task AnotherShape_TakesTheFirstOff()
    {
        var (session, entry) = await EnterAsync();
        await UseAsync(session, Slot(entry, Cookie));

        var wanded = await UseAsync(session, Slot(entry, Wand));

        Assert.Contains(wanded, m => m.Contains($"\"type\":\"{WebMessageTypes.AuraEnded}\"") && m.Contains($"\"spellId\":{CookieShape}"));
        Assert.Contains(wanded, m => m.Contains($"\"type\":\"{WebMessageTypes.AuraApplied}\"") && m.Contains($"\"spellId\":{WandShape}"));
    }

    [Fact]
    public async Task ASecondCookie_RefreshesTheMinuteInsteadOfTakingTheShapeOff()
    {
        var (session, entry) = await EnterAsync();
        var cookie = Slot(entry, Cookie);
        await UseAsync(session, cookie);

        var again = await UseAsync(session, cookie);
        var later = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(45));

        Assert.DoesNotContain(again, m => m.Contains($"\"type\":\"{WebMessageTypes.AuraEnded}\""));
        Assert.Contains(again, m => m.Contains($"\"type\":\"{WebMessageTypes.AuraApplied}\"") && m.Contains($"\"spellId\":{CookieShape}"));
        Assert.DoesNotContain(later, m => m.Contains($"\"type\":\"{WebMessageTypes.AuraEnded}\""));
    }

    [Fact]
    public async Task AShapeWithNoEnd_ComesOffTheSecondTimeItIsCast()
    {
        // Two wands, so the second cast has something to use.
        var content = Content with { StartingItems = [new StartingItemStack(Wand, 2)] };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], content: content, developmentEntry: true);
        var entry = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        var wand = Slot(entry, Wand);
        await UseAsync(session, wand);

        var again = await UseAsync(session, wand);

        Assert.Contains(again, m => m.Contains($"\"type\":\"{WebMessageTypes.AuraEnded}\"") && m.Contains($"\"spellId\":{WandShape}"));
    }
}
