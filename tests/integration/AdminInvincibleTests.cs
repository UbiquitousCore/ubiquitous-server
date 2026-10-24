using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class AdminInvincibleTests
{
    // A creature that hits hard and soon, standing next to the character.
    private static readonly GameContent Content = GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9001] = new(9001, "creature-9001", 3, 200000, MinDamage: 20, MaxDamage: 20),
        },
    };

    private static async Task<PlaySession> EnterAsync(bool admin)
    {
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret", isAdmin: admin),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            content: Content, spellRollUnit: () => 1.0, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static Task<IReadOnlyList<string>> SetAsync(PlaySession session, bool on) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AdminSetInvincible, 2, new { on })));

    // What the creature does to the character over a few seconds of fighting.
    private static async Task<int> DamageTakenAsync(PlaySession session)
    {
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:1" })));
        var at = DateTimeOffset.UtcNow;
        var taken = 0;
        for (var second = 1; second <= 8; second++)
        {
            foreach (var message in session.TickCombat(at + TimeSpan.FromSeconds(second)))
            {
                if (!message.Contains($"\"type\":\"{WebMessageTypes.AttackSwingResult}\"")) continue;
                using var document = JsonDocument.Parse(message);
                var payload = document.RootElement.GetProperty("payload");
                if (payload.GetProperty("targetId").ToString().StartsWith("player:", StringComparison.Ordinal))
                    taken += payload.GetProperty("damage").GetInt32();
            }
        }

        return taken;
    }

    [Fact]
    public async Task WithoutIt_TheCreatureHurtsTheCharacter()
    {
        var session = await EnterAsync(admin: true);

        Assert.True(await DamageTakenAsync(session) > 0);
    }

    [Fact]
    public async Task AnAdministrator_TakesNoDamageWhileItIsOn_AndIsToldItIs()
    {
        var session = await EnterAsync(admin: true);

        var told = await SetAsync(session, true);
        var taken = await DamageTakenAsync(session);

        Assert.Contains(told, message => message.Contains(WebMessageTypes.AdminInvincible) && message.Contains("\"on\":true"));
        Assert.Equal(0, taken);
    }

    [Fact]
    public async Task TurnedOffAgain_TheCreatureHurtsOnceMore()
    {
        var session = await EnterAsync(admin: true);
        await SetAsync(session, true);
        await SetAsync(session, false);

        Assert.True(await DamageTakenAsync(session) > 0);
    }

    [Fact]
    public async Task AnyoneElse_IsRefused_AndStillTakesDamage()
    {
        var session = await EnterAsync(admin: false);

        var refused = await SetAsync(session, true);

        Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.Forbidden));
        Assert.True(await DamageTakenAsync(session) > 0);
    }
}
