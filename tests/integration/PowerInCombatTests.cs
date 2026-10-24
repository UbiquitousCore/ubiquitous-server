using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PowerInCombatTests
{
    private static readonly GameContent Content = GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9001] = new(9001, "creature-9001", 3, 100, NoticeDistance: 5, MinDamage: 200, MaxDamage: 200),
        },
    };

    private static async Task<PlaySession> SessionAsync(int classId)
    {
        var character = new CharacterRecord("char-a", 1, "Ayla", 1, 2, classId, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([character]), [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            content: Content, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static List<int> PowerValues(IEnumerable<string> messages) =>
        messages.Select(m => JsonDocument.Parse(m).RootElement)
            .Where(m => m.GetProperty("type").GetString() == WebMessageTypes.PlayerValuesChanged
                && m.GetProperty("payload").GetProperty("changed").TryGetProperty(WireCodes.UnitValue.Power, out _))
            .Select(m => m.GetProperty("payload").GetProperty("changed").GetProperty(WireCodes.UnitValue.Power).GetInt32())
            .ToList();

    private static IReadOnlyList<string> Fight(PlaySession session, int stepMs)
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new List<string>();
        for (var i = 0; i < 100; i++) messages.AddRange(session.TickCombat(now + TimeSpan.FromMilliseconds(stepMs * i)));
        return messages;
    }

    [Fact]
    public async Task ABlowTakenCostsARangerVigor_AndRaisesAWarriorsMorale()
    {
        var ranger = PowerValues(Fight(await SessionAsync(PlayerClassIds.Ranger), stepMs: 100));
        var warrior = PowerValues(Fight(await SessionAsync(PlayerClassIds.Warrior), stepMs: 100));

        Assert.Contains(ranger, power => power < 100);
        Assert.Contains(warrior, power => power > SpellService.MoraleRest);
    }
}
