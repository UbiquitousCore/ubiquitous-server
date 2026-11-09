using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class MealTests
{
    private const uint HardBread = 303;
    private const uint Loaf = 3722;

    private static readonly GameContent Content = GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [HardBread] = new(HardBread, "Hard bread", 0, 0, 0f, EffectTarget: SpellEffectTargets.Self,
                DurationMs: 20_000, AuraEatPerTick: 30, AuraDrinkPerTick: 60),
        },
        Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
        {
            [Loaf] = new(Loaf, "Pilot Bread", 0, 4, 0, OnUseSpellId: HardBread),
        },
        StartingItems = [new StartingItemStack(Loaf, 1)],
    };

    private static async Task<PlaySession> EnterAsync()
    {
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]) { Health = 10 }]),
            content: Content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static Task<IReadOnlyList<string>> EatAsync(PlaySession session) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 2, new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

    private static bool Is(string message, string type) => message.Contains($"\"type\":\"{type}\"");

    [Fact]
    public async Task EatingStartsTheMeal_AndSpendsTheFood()
    {
        var session = await EnterAsync();

        var eaten = await EatAsync(session);

        Assert.Contains(eaten, m => Is(m, WebMessageTypes.AuraApplied) && m.Contains($"\"spellId\":{HardBread}"));
        Assert.Contains(eaten, m => Is(m, WebMessageTypes.InventoryItemRemoved));
    }

    [Fact]
    public async Task EachFourSeconds_PaysHealthBack()
    {
        var session = await EnterAsync();
        await EatAsync(session);

        var paid = session.TickCombat(DateTimeOffset.UtcNow.AddSeconds(4.5))
            .Concat(session.TickCombat(DateTimeOffset.UtcNow.AddSeconds(6))).ToList();

        var values = paid.Last(m => Is(m, WebMessageTypes.PlayerValuesChanged) && m.Contains("\"health\""));
        using var document = JsonDocument.Parse(values);
        var health = document.RootElement.GetProperty("payload").GetProperty("changed").GetProperty("health").GetInt32();
        Assert.True(health >= 40, $"health was {health}");
    }

    [Fact]
    public async Task TheMealEndsAfterItsTwentySeconds()
    {
        var session = await EnterAsync();
        await EatAsync(session);

        var early = session.TickCombat(DateTimeOffset.UtcNow.AddSeconds(10));
        var over = session.TickCombat(DateTimeOffset.UtcNow.AddSeconds(21));

        Assert.DoesNotContain(early, m => Is(m, WebMessageTypes.AuraEnded));
        Assert.Contains(over, m => Is(m, WebMessageTypes.AuraEnded) && m.Contains($"\"spellId\":{HardBread}"));
    }

    [Fact]
    public async Task EveryoneIsToldWhenTheEaterSitsAndGetsUp()
    {
        var session = await EnterAsync();
        await EatAsync(session);

        var sat = session.TickCombat(DateTimeOffset.UtcNow.AddSeconds(2));
        var rose = session.TickCombat(DateTimeOffset.UtcNow.AddSeconds(21))
            .Concat(session.TickCombat(DateTimeOffset.UtcNow.AddSeconds(23))).ToList();

        Assert.Contains(sat, m => Is(m, WebMessageTypes.EntityValuesChanged) && m.Contains("\"eating\":1"));
        Assert.Contains(rose, m => Is(m, WebMessageTypes.EntityValuesChanged) && m.Contains("\"eating\":0"));
    }

    [Fact]
    public async Task MovingEndsTheMeal()
    {
        var session = await EnterAsync();
        await EatAsync(session);

        var moved = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 3, new PlayerMovePayload(1, 2, 3.5f, 0, true, MovementState.Walking))));

        Assert.Contains(moved, m => Is(m, WebMessageTypes.AuraEnded));
    }
}
