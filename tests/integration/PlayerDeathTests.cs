using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PlayerDeathTests
{
    [Fact]
    public async Task TickCombat_MobWandersAgainAfterKillingThePlayer()
    {
        var session = await SessionAsync();
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        var dead = false;
        for (var i = 0; i < 30 && !dead; i++)
        {
            now += TimeSpan.FromSeconds(2);
            dead = session.TickCombat(now).Any(message => message.Contains("unit.died")
                && message.Contains("player:char-a"));
        }

        Assert.True(dead, "expected the wolf to kill the player");
        session.TickCombat(now + TimeSpan.FromSeconds(3));
        for (var i = 4; i <= 60; i++) session.TickWorld(now + TimeSpan.FromSeconds(i));
        var settled = now + TimeSpan.FromSeconds(60);
        session.TickCombat(settled);
        var wanderTick = session.TickCombat(settled + CreatureWanderService.PauseDuration);

        Assert.Contains(wanderTick, message => message.Contains("\"type\":\"entity.moved\"")
            && message.Contains("\"target\""));
    }

    [Fact]
    public async Task TickCombat_NothingReaggrosTheCorpse()
    {
        var session = await SessionAsync();
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        var dead = false;
        for (var i = 0; i < 30 && !dead; i++)
        {
            now += TimeSpan.FromSeconds(2);
            dead = session.TickCombat(now).Any(message => message.Contains("unit.died")
                && message.Contains("player:char-a"));
        }

        Assert.True(dead, "expected the wolf to kill the player");
        for (var i = 0; i < 4; i++)
        {
            now += TimeSpan.FromSeconds(3);
            var tick = session.TickCombat(now);
            Assert.DoesNotContain(tick, message => message.Contains("attack.meleeStart"));
        }
    }

    [Fact]
    public async Task SosRelease_RevivesAndRefights()
    {
        var session = await SessionAsync();
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        var dead = false;
        for (var i = 0; i < 30 && !dead; i++)
        {
            now += TimeSpan.FromSeconds(2);
            dead = session.TickCombat(now).Any(message => message.Contains("unit.died")
                && message.Contains("player:char-a"));
        }

        Assert.True(dead, "expected the wolf to kill the player");
        var released = await SendAsync(session, WebMessageTypes.PlayerUnstuck, 3, new { });
        Assert.Contains(released, message => message.Contains("player.revived"));

        session.TickWorld(now);
        session.TickWorld(now + TimeSpan.FromMinutes(1));
        var reagro = session.TickCombat(now + TimeSpan.FromSeconds(2));
        Assert.Contains(reagro, message => message.Contains("attack.meleeStart"));
    }

    private static GameContent Content() => GameContent.Development with
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
    };

    [Fact]
    public async Task KillerFacesHomeWhenItNeverLeft()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1001, 13, 9500, 1, 2, 3, 0)],
            content: Content(), combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        List<string> deathTick = [];
        for (var i = 0; i < 30; i++)
        {
            now += TimeSpan.FromSeconds(2);
            deathTick = session.TickCombat(now).ToList();
            if (deathTick.Any(message => message.Contains("unit.died") && message.Contains("player:char-a")))
                break;
        }

        Assert.Contains(deathTick, message => message.Contains("\"type\":\"entity.moved\"")
            && message.Contains("\"moving\":false")
            && message.Contains("\"yaw\":0"));
    }

    [Fact]
    public async Task SaveCharacter_PutsACharacterWhoDiedAtTheGraveyardRatherThanOnTheirCorpse()
    {
        var characters = new InMemoryCharacterStore([new CharacterRecord(
            "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            characters,
            [new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0, IdleBehaviour: 1, WanderRadius: 10)],
            content: Content(), combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var now = DateTimeOffset.UtcNow;
        var dead = false;
        for (var i = 0; i < 30 && !dead; i++)
        {
            now += TimeSpan.FromSeconds(2);
            dead = session.TickCombat(now).Any(message => message.Contains("unit.died")
                && message.Contains("player:char-a"));
        }

        Assert.True(dead, "expected the wolf to kill the player");
        var corpse = await characters.GetByGuidAsync("char-a");
        await session.SaveCharacterAsync();
        var saved = await characters.GetByGuidAsync("char-a");

        Assert.NotNull(saved);
        var grave = GraveyardLookup.Resolve(
            Content(), corpse!.MapId, new Ubiquitous.Common.Position(corpse.X, corpse.Y, corpse.Z));
        Assert.Equal(grave.MapId, saved!.MapId);
        Assert.Equal(grave.Position.X, saved.X, precision: 3);
        Assert.Equal(grave.Position.Z, saved.Z, precision: 3);
    }

    private static async Task<PlaySession> SessionAsync()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0, IdleBehaviour: 1, WanderRadius: 10)],
            content: Content(), combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        return session;
    }

    private static async Task<IReadOnlyList<string>> SendAsync(
        PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
}
