using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PetTests
{
    private const uint Tortoise = 1709;
    private const uint Piglet = 1711;
    private const uint TortoiseItem = 5826;
    private const uint TortoiseCreature = 2508;
    private const uint PigletCreature = 2510;

    private static readonly GameContent Content = GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [Tortoise] = new(Tortoise, "Little Tortoise", 0, 0, 0f,
                EffectTarget: SpellEffectTargets.Self, EffectSummonCreatureId: TortoiseCreature),
            [Piglet] = new(Piglet, "Boar Piglet", 0, 0, 0f,
                EffectTarget: SpellEffectTargets.Self, EffectSummonCreatureId: PigletCreature),
        },
        Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
        {
            [TortoiseItem] = new(TortoiseItem, "Little Tortoise", 12, 2, 0, OnUseSpellId: Tortoise),
        },
        StartingItems = [new StartingItemStack(TortoiseItem, 1)],
    };

    [Fact]
    public async Task UsingAPetItem_PutsThePetInTheBook_AndSpendsTheItem()
    {
        var session = await EnterAsync();

        var used = await UseItemAsync(session);

        Assert.Contains(used, message => Is(message, WebMessageTypes.SpellLearned) && message.Contains($"[{Tortoise}]"));
        Assert.Contains(used, message => Is(message, WebMessageTypes.InventoryItemRemoved));
    }

    [Fact]
    public async Task APetAlreadyKnown_KeepsItsItem()
    {
        var session = await EnterAsync([Tortoise]);

        var used = await UseItemAsync(session);

        Assert.Contains(used, message => message.Contains("already know"));
        Assert.DoesNotContain(used, message => Is(message, WebMessageTypes.InventoryItemRemoved));
    }

    [Fact]
    public async Task CastingAKnownPet_PutsItOut()
    {
        var session = await EnterAsync([Tortoise]);

        var cast = await CastAsync(session, Tortoise);

        Assert.DoesNotContain(cast, message => Is(message, WebMessageTypes.SpellFailed));
        Assert.Equal(TortoiseCreature, session.PetEntry);
    }

    [Fact]
    public async Task ASecondPet_ReplacesTheFirst()
    {
        var session = await EnterAsync([Tortoise, Piglet]);

        await CastAsync(session, Tortoise);
        await CastAsync(session, Piglet);

        Assert.Equal(PigletCreature, session.PetEntry);
    }

    [Fact]
    public async Task CastingThePetThatIsOut_SendsItAway()
    {
        var session = await EnterAsync([Tortoise]);
        await CastAsync(session, Tortoise);

        await CastAsync(session, Tortoise);
        var sent = session.TickCombat(DateTimeOffset.UtcNow.AddSeconds(2));

        Assert.Null(session.PetEntry);
        Assert.Contains(sent, message => Is(message, WebMessageTypes.EntityValuesChanged) && message.Contains("\"pet\":0"));
    }

    private static CharacterRecord Owner(uint[] pets) => new(
        "char-owner", 1, "Ayla", 1, PlayerRaceIds.Human, PlayerClassIds.Warrior, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 20, [100, 200]) { LearnedSpells = pets };

    private static bool Is(string message, string type) => message.Contains($"\"type\":\"{type}\"");

    private static async Task<PlaySession> EnterAsync(uint[]? pets = null)
    {
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Owner(pets ?? [])]), content: Content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static Task<IReadOnlyList<string>> UseItemAsync(PlaySession session) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 2, new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

    private static Task<IReadOnlyList<string>> CastAsync(PlaySession session, uint spellId) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 3, new { spellId })));
}
