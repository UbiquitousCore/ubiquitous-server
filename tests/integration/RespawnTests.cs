using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class RespawnTests
{
    [Fact]
    public async Task RelogSeesTheSameCorpseItNeverWatchedDie()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var characters = new InMemoryCharacterStore([Character("char-a", "Ayla")]);
        var content = Content();
        var (killer, _) = await JoinAsync(maps, characters, content, "Ayla", "char-a");
        await SendAsync(killer, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        var dead = false;
        for (var i = 0; i < 30 && !dead; i++)
        {
            now += TimeSpan.FromSeconds(2);
            dead = killer.TickCombat(now).Any(message => message.Contains("unit.died")
                && message.Contains("Creature:1"));
        }

        Assert.True(dead, "expected the kill");
        var (_, entered) = await JoinAsync(maps, characters, content, "Ayla", "char-a");

        var corpse = Assert.Single(entered, message => message.Contains("\"type\":\"entity.spawned\"")
            && message.Contains("\"networkId\":\"Creature:1\""));
        Assert.Contains("\"dead\":true", corpse);
        Assert.DoesNotContain(entered, message => message.Contains("\"type\":\"unit.died\""));
    }

    [Fact]
    public async Task ReloggedCorpseClearsAndRespawnsUnderANewId()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var characters = new InMemoryCharacterStore([Character("char-a", "Ayla")]);
        var content = Content();
        var (killer, _) = await JoinAsync(maps, characters, content, "Ayla", "char-a");
        await SendAsync(killer, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        var dead = false;
        for (var i = 0; i < 30 && !dead; i++)
        {
            now += TimeSpan.FromSeconds(2);
            dead = killer.TickCombat(now).Any(message => message.Contains("unit.died")
                && message.Contains("Creature:1"));
        }

        Assert.True(dead, "expected the kill");
        var (relogger, _) = await JoinAsync(maps, characters, content, "Ayla", "char-a");

        now += TimeSpan.FromSeconds(158);
        var cleared = relogger.TickCombat(now);
        Assert.Contains(cleared, message => message.Contains("\"type\":\"entity.removed\""));
        var respawned = Assert.Single(cleared, message => message.Contains("\"type\":\"entity.spawned\""));
        Assert.DoesNotContain("\"networkId\":\"Creature:1\"", respawned);
    }

    [Fact]
    public async Task ACorpseWithLootStaysAsLongForSomeoneWatchingAsForItsKiller()
    {
        var maps = new SharedMapRegistry(() => 0.9);
        var characters = new InMemoryCharacterStore([Character("char-a", "Ayla"), Character("char-b", "Bryn")]);
        var content = Content(withMoney: true);
        var (killer, _) = await JoinAsync(maps, characters, content, "Ayla", "char-a");
        var (watcher, _) = await JoinAsync(maps, characters, content, "Bryn", "char-b");
        await SendAsync(killer, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        string? death = null;
        for (var i = 0; i < 30 && death is null; i++)
        {
            now += TimeSpan.FromSeconds(2);
            death = killer.TickCombat(now).FirstOrDefault(message => message.Contains("unit.died")
                && message.Contains("Creature:1"));
        }

        Assert.NotNull(death);
        watcher.EncodeObserverEvent(OutboundEvent.From(death!)!);

        static bool Removed(IEnumerable<string> messages) => messages.Any(message =>
            message.Contains("\"type\":\"entity.removed\"") && message.Contains("Creature:1"));
        Assert.False(Removed(watcher.TickCombat(now + TimeSpan.FromSeconds(40))));
        Assert.False(Removed(killer.TickCombat(now + TimeSpan.FromSeconds(40))));
        Assert.True(Removed(watcher.TickCombat(now + TimeSpan.FromSeconds(151))));
        Assert.True(Removed(killer.TickCombat(now + TimeSpan.FromSeconds(151))));
    }

    private static CharacterRecord Character(string guid, string name) => new(
        guid, 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);

    private static GameContent Content(bool withMoney = false) => GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9500] = new(9500, "wolf", 3, 30, MinDamage: 1, MaxDamage: 2,
                RespawnTimeMinSeconds: 5, RespawnTimeMaxSeconds: 5,
                FactionType: UnitFactionTypes.Hostile,
                MoneyDropMin: withMoney ? 5 : null, MoneyDropMax: withMoney ? 5 : null),
        },
    };

    private static async Task<(PlaySession Session, IReadOnlyList<string> Entered)> JoinAsync(
        SharedMapRegistry maps, InMemoryCharacterStore characters, GameContent content,
        string name, string guid)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            characters,
            [new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0)],
            content: content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9,
            sharedMaps: maps);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        var entered = await SendAsync(session, WebMessageTypes.CharactersChoose, 1,
            new ChooseCharacterRequest(name));
        return (session, entered);
    }

    private static async Task<IReadOnlyList<string>> SendAsync(
        PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
}
