using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;
using Ubiquitous.World;

namespace Ubiquitous.Integration.Tests;

// Crowd regressions: neither 200 stress bots nor 200 sessions sharing one
// map may hide real entities that stay in range. The server sends every
// in-range body; any hiding under a crowd happens downstream.
public sealed class CrowdVisibilityTests
{
    [Fact]
    public async Task StressCrowd_NeverRemovesNearbyRealEntities()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                // Unkillable, so nothing legitimately despawns during the run.
                [9500] = new(9500, "creature-dummy", 10, 1_000_000, RawExtraFlags: 128),
            },
        };
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret", isAdmin: true),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [
                new CreatureSpawnDefinition(1001, 13, 9500, 10, 2, 10, 0),
                new CreatureSpawnDefinition(1002, 13, 9001, 14, 2, 10, 0),
                new CreatureSpawnDefinition(1003, 13, 9001, 6, 2, 14, 0),
            ],
            staticObjectSpawns: [
                new StaticObjectSpawnDefinition(2001, 13, 9101, 12, 2, 12, 0),
                new StaticObjectSpawnDefinition(2002, 13, 9101, 8, 2, 8, 0),
            ],
            content: content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.5,
            developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));

        var all = new List<string>();
        all.AddRange(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla")))));

        var now = DateTimeOffset.UtcNow;
        all.AddRange(session.TickCombat(now));
        all.AddRange(await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.DevStressTest, 2, new { count = 200 }))));
        for (var i = 1; i <= 120; i++)
            all.AddRange(session.TickCombat(now + TimeSpan.FromSeconds(i)));

        var spawnedReal = NetworkIds(all, WebMessageTypes.EntitySpawned)
            .Where(id => !id.StartsWith("player:", StringComparison.Ordinal)).ToHashSet();
        var removedReal = NetworkIds(all, WebMessageTypes.EntityRemoved)
            .Where(id => !id.StartsWith("player:", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(spawnedReal);
        Assert.Empty(removedReal);
    }

    [Fact]
    public async Task SharedMapCrowd_NeverRemovesNearbyRealEntities()
    {
        const int sessions = 200;
        var maps = new SharedMapRegistry(() => 0.5);
        var content = TestSessions.WithTestPurse(GameContent.Development);
        var spawns = new List<CreatureSpawnDefinition>
        {
            new(1001, 13, 9001, 4, 2, 5, 0),
            new(1002, 13, 9001, -2, 2, 6, 0),
        };
        var statics = new List<StaticObjectSpawnDefinition>
        {
            new(2001, 13, 9101, 3, 2, 3, 0),
        };

        var created = new List<PlaySession>();
        for (var i = 0; i < sessions; i++)
        {
            var session = TestSessions.Create(
                new DevelopmentAccountStore($"developer-{i}", "dev-secret"),
                new InMemoryCharacterStore([new CharacterRecord(
                    $"char-{i}", 1, $"Ayla{i}", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
                spawns, staticObjectSpawns: statics, content: content,
                combatRollUnit: () => 1.0, wanderRollUnit: () => 0.5,
                sharedMaps: maps, developmentEntry: true);
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest($"developer-{i}", "dev-secret"))));
            created.Add(session);
        }

        var all = new List<string>();
        for (var i = 0; i < sessions; i++)
            all.AddRange(await created[i].HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest($"Ayla{i}")))));

        var now = DateTimeOffset.UtcNow;
        for (var tick = 1; tick <= 30; tick++)
            foreach (var session in created)
                all.AddRange(session.TickCombat(now + TimeSpan.FromSeconds(tick)));

        Assert.NotEmpty(NetworkIds(all, WebMessageTypes.EntitySpawned));
        Assert.Empty(NetworkIds(all, WebMessageTypes.EntityRemoved));
    }

    private static IEnumerable<string> NetworkIds(List<string> messages, string type) =>
        messages.Select(message => NetworkIdOf(message, type)).OfType<string>();

    private static string? NetworkIdOf(string message, string type)
    {
        var at = message.IndexOf($"\"type\":\"{type}\"", StringComparison.Ordinal);
        if (at < 0) return null;
        var id = message.IndexOf("\"networkId\":\"", at, StringComparison.Ordinal);
        if (id < 0) return null;
        id += "\"networkId\":\"".Length;
        var end = message.IndexOf('"', id);
        return end < 0 ? null : message[id..end];
    }
}
