using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class CombatExitTests
{
    [Fact]
    public async Task MobKilled_PlayerExitsCombat()
    {
        var session = await SessionAsync();
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        List<string> killTick = [];
        for (var i = 0; i < 30; i++)
        {
            now += TimeSpan.FromSeconds(2);
            killTick = session.TickCombat(now).ToList();
            if (killTick.Any(message => message.Contains("unit.died") && message.Contains("Creature:1")))
                break;
        }

        Assert.Contains(killTick, message => message.Contains("\"type\":\"attack.halted\""));
        Assert.Contains(killTick, message => message.Contains("\"type\":\"attack.exitCombat\"")
            && message.Contains("player:char-a"));

        var after = session.TickCombat(now + TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(after, message => message.Contains("\"attackerId\":\"player:char-a\"")
            && message.Contains("\"type\":\"attack.swingResult\""));
    }

    [Fact]
    public async Task RemoteKill_ObserverExitsCombat()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var hub = new PlaySessionHub();
        var (ayla, _) = await JoinAsync(hub, maps, "exit1", "Ayla", "char-a", 1);
        var (borin, _) = await JoinAsync(hub, maps, "exit2", "Borin", "char-b", 2);
        await SendAsync(hub, ayla, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });
        await SendAsync(hub, borin, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        var dead = false;
        for (var i = 0; i < 30 && !dead; i++)
        {
            now += TimeSpan.FromSeconds(2);
            var responses = ayla.TickCombat(now).ToList();
            await hub.BroadcastAsync(ayla, responses);
            dead = responses.Any(message => message.Contains("unit.died") && message.Contains("Creature:1"));
        }

        Assert.True(dead, "expected Ayla to land the killing blow");
        var exit = borin.TickCombat(now + TimeSpan.FromSeconds(2));
        Assert.Contains(exit, message => message.Contains("\"type\":\"attack.halted\""));
        Assert.Contains(exit, message => message.Contains("\"type\":\"attack.exitCombat\"")
            && message.Contains("player:char-b"));
    }

    private static GameContent Content() => GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9500] = new(9500, "wolf", 3, 60, MinDamage: 1, MaxDamage: 2,
                FactionType: UnitFactionTypes.Hostile),
        },
    };

    private static readonly CreatureSpawnDefinition[] Spawns =
        [new(1001, 13, 9500, 2, 2, 4, 0)];

    private static async Task<PlaySession> SessionAsync()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            Spawns, content: Content(), combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        return session;
    }

    private static async Task<(PlaySession Session, List<string> Inbox)> JoinAsync(
        PlaySessionHub hub, SharedMapRegistry maps, string guid, string name, string charGuid, int accountId)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p", accountId),
            new InMemoryCharacterStore([new CharacterRecord(
                charGuid, accountId, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            Spawns, content: Content(), combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9,
            sharedMaps: maps);
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });
        await SendAsync(hub, session, WebMessageTypes.AccountLogin, 0, new LoginRequest(guid, "p"));
        await SendAsync(hub, session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name));
        return (session, inbox);
    }

    private static async Task<IReadOnlyList<string>> SendAsync(
        PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));

    private static async Task<IReadOnlyList<string>> SendAsync(
        PlaySessionHub hub, PlaySession session, string type, int sequence, object payload)
    {
        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
        var presence = await hub.BroadcastAsync(session, responses);
        return [.. responses, .. presence];
    }
}
