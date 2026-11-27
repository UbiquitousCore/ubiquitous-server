using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class WingTests
{
    private const uint WingSpell = 2269;
    private const uint LostWingSpell = 2270;
    private const uint Wing = 5869;
    private const uint LostWing = 5872;
    private const uint Destination = 2;

    private static GameContent Content(uint wing = Wing, int count = 2) => GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [WingSpell] = new(WingSpell, "Teleport to Silence", 0, 0, 0f,
                EffectTarget: SpellEffectTargets.Self, EffectTeleportStopId: Destination),
            [LostWingSpell] = new(LostWingSpell, "Teleport to Nowhere", 0, 0, 0f,
                EffectTarget: SpellEffectTargets.Self, EffectTeleportStopId: 999),
        },
        Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
        {
            [Wing] = new(Wing, "Teleport: Silence", 0, 3, 0, OnUseSpellId: WingSpell,
                OnUseCooldownSeconds: 60, MaxStack: 5),
            [LostWing] = new(LostWing, "Teleport: Nowhere", 0, 3, 0, OnUseSpellId: LostWingSpell, MaxStack: 5),
        },
        StartingItems = [new StartingItemStack(wing, count)],
    };

    private static readonly CharacterRecord Character = new(
        "char-a", 1, "Ayla", 1, PlayerRaceIds.Human, PlayerClassIds.Warrior, 4, 5, 6, 7, 8,
        MapId: 13, X: 0, Y: 0, Z: 0, Level: 1, Equipment: []);

    private static async Task<PlaySession> EnterAsync(GameContent content)
    {
        var session = TestSessions.Create(
            new InMemoryAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), content: content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static Task<IReadOnlyList<string>> UseAsync(PlaySession session) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 2, new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

    private static bool Is(string message, string type) => message.Contains($"\"type\":\"{type}\"");

    [Fact]
    public async Task UsingAWing_SendsThePlayerToItsStop_AndSpendsOne()
    {
        var session = await EnterAsync(Content());

        var used = await UseAsync(session);

        Assert.Contains(used, message => Is(message, WebMessageTypes.WorldEntered));
        Assert.Contains(used, message => Is(message, WebMessageTypes.InventoryStackChanged) && message.Contains("\"count\":1"));
        var stop = GameContent.Development.Stops[Destination];
        Assert.Equal((ushort?)stop.MapId, session.CurrentMapId);
        Assert.Equal(new Ubiquitous.Common.Position(stop.X, stop.Y, stop.Z), session.PlayerPosition);
    }

    [Fact]
    public async Task ASecondWing_WaitsForTheFirstOnesCooldown()
    {
        var session = await EnterAsync(Content());
        await UseAsync(session);

        var again = await UseAsync(session);

        Assert.Contains(again, message => message.Contains("not ready yet"));
        Assert.DoesNotContain(again, message => Is(message, WebMessageTypes.InventoryStackChanged));
    }

    [Fact]
    public async Task AWingToAStopTheWorldDoesNotHave_IsKeptAndGoesNowhere()
    {
        var session = await EnterAsync(Content(LostWing, count: 1));

        var used = await UseAsync(session);

        Assert.Contains(used, message => message.Contains("leads nowhere"));
        Assert.DoesNotContain(used, message => Is(message, WebMessageTypes.InventoryItemRemoved));
        Assert.Equal(new Ubiquitous.Common.Position(0f, 0f, 0f), session.PlayerPosition);
    }
}
