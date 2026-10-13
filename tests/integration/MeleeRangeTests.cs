using Ubiquitous.Data;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class MeleeRangeTests
{
    [Fact]
    public async Task AttackStart_RefusesATargetThatIsVisibleButOutOfMeleeReach()
    {
        var session = await SessionAsync(new CreatureSpawnDefinition(1001, 13, 9001, 20, 2, 3, 0));

        var response = await AttackAsync(session);

        Assert.Contains("invalid_target", Assert.Single(response));
    }

    [Fact]
    public async Task AttackStart_AcceptsATargetWithinMeleeReach()
    {
        var session = await SessionAsync(new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));

        var response = await AttackAsync(session);

        Assert.Contains(response, message => message.Contains("attack.meleeStart"));
    }

    [Fact]
    public async Task AttackStart_AcceptsATargetRightAtTheEdgeOfMeleeReach()
    {
        var session = await SessionAsync(new CreatureSpawnDefinition(1001, 13, 9001, 3, 2, 5, 0));

        var response = await AttackAsync(session);

        Assert.Contains(response, message => message.Contains("attack.meleeStart"));
    }

    [Fact]
    public async Task AttackStart_RefusesATargetJustPastTheEdgeOfMeleeReach()
    {
        var session = await SessionAsync(new CreatureSpawnDefinition(1001, 13, 9001, 3.3f, 2, 5, 0));

        var response = await AttackAsync(session);

        Assert.Contains("invalid_target", Assert.Single(response));
    }

    [Fact]
    public async Task AttackStart_TheFightStandsButTheSwingStopsOnceOutOfReach()
    {
        var session = await SessionAsync(new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0));
        await AttackAsync(session);

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 3, new PlayerMovePayload(15, 2, 3, 0, true, MovementState.Walking))));

        var swing = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(swing, message => message.Contains("attack.swingResult"));
        Assert.DoesNotContain(swing, message => message.Contains("attack.exitCombat"));
    }

    private static async Task<IReadOnlyList<string>> AttackAsync(PlaySession session) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

    private static async Task<PlaySession> SessionAsync(CreatureSpawnDefinition spawn)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [spawn]);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
