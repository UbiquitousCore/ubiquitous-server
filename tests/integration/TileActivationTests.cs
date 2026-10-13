using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class TileActivationTests
{
    [Fact]
    public async Task TickCombat_LeavesABeyondRadiusWandererSleeping()
    {
        var session = await SessionAsync(
            new CreatureSpawnDefinition(1001, 13, 9001, 50, 2, 3, 0, IdleBehaviour: 1, WanderRadius: 10));
        var now = DateTimeOffset.UtcNow;
        session.TickCombat(now);

        var tick = session.TickCombat(now + CreatureWanderService.PauseDuration + CreatureWanderService.PauseDuration);

        Assert.DoesNotContain(tick, message => message.Contains("\"type\":\"entity.moved\""));
    }

    private static async Task<PlaySession> SessionAsync(params CreatureSpawnDefinition[] spawns)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            spawns, content: GameContent.Development, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
