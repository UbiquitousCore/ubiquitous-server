using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class DeadPlayerTests
{
    private const uint WingSpell = 2269;
    private const uint Wing = 5869;
    private const uint BreadSpell = 9601;
    private const uint Bread = 9602;
    private const uint Mend = 9603;

    private static GameContent Content(uint item) => GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9500] = new(9500, "wolf", 3, 100, MinDamage: 50, MaxDamage: 60,
                FactionType: UnitFactionTypes.Hostile),
        },
        Maps = new Dictionary<ushort, MapDefinition>
        {
            [13] = new(13, "test", RecallMapId: 13, RecallX: 1, RecallY: 2, RecallZ: 3),
        },
        Graveyards = [],
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [WingSpell] = new(WingSpell, "Teleport to Silence", 0, 0, 0f,
                EffectTarget: SpellEffectTargets.Self, EffectTeleportStopId: 2),
            [BreadSpell] = new(BreadSpell, "Bread", 0, 0, 0f, DurationMs: 18000,
                EffectTarget: SpellEffectTargets.Self, AuraEatPerTick: 30),
            [Mend] = new(Mend, "Mend", 0, 1000, 0f, PowerCost: 1, EffectHeal: 10,
                EffectTarget: SpellEffectTargets.Self),
        },
        Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
        {
            [Wing] = new(Wing, "Teleport: Silence", 0, 3, 0, OnUseSpellId: WingSpell, MaxStack: 5),
            [Bread] = new(Bread, "Bread", 0, 5, 0, OnUseSpellId: BreadSpell, MaxStack: 20),
        },
        StartingItems = [new StartingItemStack(item, 2)],
    };

    private static async Task<PlaySession> KilledAsync(uint item)
    {
        var session = TestSessions.Create(new InMemoryAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]) { LearnedSpells = [Mend] }]),
            [new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0)],
            content: Content(item), combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 30 && session.IsAlive; i++)
        {
            now += TimeSpan.FromSeconds(2);
            session.TickCombat(now);
        }

        Assert.False(session.IsAlive, "expected the wolf to kill the player");
        return session;
    }

    private static Task<IReadOnlyList<string>> UseAsync(PlaySession session) =>
        SendAsync(session, WebMessageTypes.InventoryUse, 3,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item" });

    private static Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, int sequence, object payload) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload)));

    private static bool Is(string message, string type) => message.Contains($"\"type\":\"{type}\"");

    [Fact]
    public async Task ADeadPlayer_CannotUseAWing()
    {
        var session = await KilledAsync(Wing);
        var lyingAt = session.PlayerPosition;

        var used = await UseAsync(session);

        Assert.Contains("You are dead", Assert.Single(used));
        Assert.Equal(lyingAt, session.PlayerPosition);
        Assert.False(session.IsAlive);
    }

    [Fact]
    public async Task ADeadPlayer_CannotEat()
    {
        var session = await KilledAsync(Bread);

        var used = await UseAsync(session);

        Assert.Contains("You are dead", Assert.Single(used));
    }

    [Fact]
    public async Task ADeadPlayer_CannotCast()
    {
        var session = await KilledAsync(Wing);

        var cast = await SendAsync(session, WebMessageTypes.SpellRequest, 3, new { spellId = Mend });

        var refused = Assert.Single(cast);
        Assert.True(Is(refused, WebMessageTypes.SpellFailed), refused);
        Assert.Contains(WireCodes.SpellRefusal.Incapacitated, refused);
    }

    [Fact]
    public async Task ADeadPlayer_IsHeldWhereTheyFell()
    {
        var session = await KilledAsync(Wing);
        var lyingAt = session.PlayerPosition!.Value;

        var moved = await SendAsync(session, WebMessageTypes.PlayerMove, 3,
            new { x = lyingAt.X + 2, y = lyingAt.Y, z = lyingAt.Z, yaw = 0, moving = true });

        var reply = Assert.Single(moved);
        Assert.True(Is(reply, WebMessageTypes.MoveAccepted), reply);
        Assert.Equal(lyingAt, session.PlayerPosition);
    }

    [Fact]
    public async Task ADeadPlayer_CanStillTalkAndRelease()
    {
        var session = await KilledAsync(Wing);

        var said = await SendAsync(session, WebMessageTypes.PlayerChat, 3, new { channel = "say", text = "help" });
        var released = await SendAsync(session, WebMessageTypes.PlayerUnstuck, 4, new { });

        Assert.DoesNotContain(said, message => message.Contains("You are dead"));
        Assert.Contains(released, message => Is(message, WebMessageTypes.PlayerRevived));
        Assert.True(session.IsAlive);
    }

    [Fact]
    public async Task OnceReleased_TheWingWorksAgain()
    {
        var session = await KilledAsync(Wing);
        await SendAsync(session, WebMessageTypes.PlayerUnstuck, 4, new { });

        var used = await UseAsync(session);

        Assert.Contains(used, message => Is(message, WebMessageTypes.WorldEntered));
    }
}
