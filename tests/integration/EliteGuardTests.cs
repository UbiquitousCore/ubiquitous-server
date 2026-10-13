using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class EliteGuardTests
{
    [Fact]
    public async Task TickCombat_ALieutenantNoticesFromWhereAnOrdinaryCreatureWouldNot()
    {
        var ordinary = await SessionAsync(ContentWithCreature(
            new CreatureDefinition(9500, "wolf", 3, 100,
                FactionType: UnitFactionTypes.Hostile)),
            new CreatureSpawnDefinition(1001, 13, 9500, 13, 2, 3, 0));

        Assert.DoesNotContain(
            ordinary.TickCombat(DateTimeOffset.UtcNow),
            message => message.Contains("type\":\"attack.meleeStart"));

        var lieutenant = await SessionAsync(ContentWithCreature(
            new CreatureDefinition(9500, "wolf", 3, 100,
                FactionType: UnitFactionTypes.Hostile, Rank: CreatureRank.Elite)),
            new CreatureSpawnDefinition(1001, 13, 9500, 13, 2, 3, 0));

        Assert.Contains(
            lieutenant.TickCombat(DateTimeOffset.UtcNow),
            message => message.Contains("type\":\"attack.meleeStart"));
    }

    private static GameContent ContentWithCreature(CreatureDefinition creature) =>
        GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [creature.EntryId] = creature,
            },
        };

    private static async Task<PlaySession> SessionAsync(GameContent content, params CreatureSpawnDefinition[] spawns)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            spawns, content: content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
