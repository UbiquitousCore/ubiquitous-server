using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class UnitDiedPositionTests
{
    private static readonly GameContent Content = GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9500] = new(9500, "wolf", 3, 30, MinDamage: 1, MaxDamage: 1,
                FactionType: UnitFactionTypes.Hostile),
            [9501] = new(9501, "bear", 3, 3000, MinDamage: 60, MaxDamage: 60,
                FactionType: UnitFactionTypes.Hostile),
        },
    };

    [Fact]
    public async Task A_CreatureKilledByThePlayer_DiesWhereItStood()
    {
        var session = await SessionAsync(creature: 9500, health: null);
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var death = KillOrDie(session, "Creature:1");

        var pos = Payload(death).GetProperty("pos");
        Assert.Equal(2, pos.GetProperty("x").GetDouble(), 3);
        Assert.Equal(2, pos.GetProperty("y").GetDouble(), 3);
        Assert.Equal(4, pos.GetProperty("z").GetDouble(), 3);
    }

    [Fact]
    public async Task The_PlayerKilledByACreature_DiesWhereTheyStood()
    {
        var session = await SessionAsync(creature: 9501, health: 1);
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var death = KillOrDie(session, "player:char-a");

        var pos = Payload(death).GetProperty("pos");
        Assert.Equal(1, pos.GetProperty("x").GetDouble(), 3);
        Assert.Equal(2, pos.GetProperty("y").GetDouble(), 3);
        Assert.Equal(3, pos.GetProperty("z").GetDouble(), 3);
    }

    private static string KillOrDie(PlaySession session, string victim)
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 40; i++)
        {
            var death = session.TickCombat(now).FirstOrDefault(message =>
                message.Contains("\"type\":\"unit.died\"") && message.Contains($"\"networkId\":\"{victim}\""));
            if (death is not null) return death;
            now += TimeSpan.FromSeconds(2);
        }
        throw new Xunit.Sdk.XunitException($"expected {victim} to die");
    }

    private static JsonElement Payload(string message) =>
        JsonDocument.Parse(message).RootElement.GetProperty("payload");

    private static async Task<PlaySession> SessionAsync(uint creature, int? health)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200], Health: health)]),
            [new CreatureSpawnDefinition(1001, 13, creature, 2, 2, 4, 0)],
            content: Content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        return session;
    }

    private static async Task<IReadOnlyList<string>> SendAsync(
        PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
}
