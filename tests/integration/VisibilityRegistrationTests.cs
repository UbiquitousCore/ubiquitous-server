using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class VisibilityRegistrationTests
{
    private static readonly CreatureSpawnDefinition[] Spawns =
    [
        new(1, 13, 9001, 80f, 2f, 3f, 0f),
    ];

    [Fact]
    public async Task WalkingIntoRange_MakesTheCreatureAttackable()
    {
        var session = await SessionAsync();

        var moved = await WalkAsync(session, 1f, 78f);

        Assert.Contains(moved, message => message.Contains("\"type\":\"entity.spawned\""));
        var attack = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 4, new { targetId = "Creature:1" })));
        Assert.Contains(attack, message => message.Contains("attack.meleeStart"));
    }

    [Fact]
    public async Task Spawn_SaysWhenTheCreatureOffersQuests()
    {
        var content = Ubiquitous.Game.GameContent.Development with
        {
            Npcs = new Dictionary<uint, NpcDefinition>(Ubiquitous.Game.GameContent.Development.Npcs)
            {
                [9001] = new(9001, NpcFlags.Gossip, OffersQuests: true),
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1, 13, 9001, 2f, 2f, 3f, 0f)], content: content);

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        var chooseResp = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var spawned = Assert.Single(chooseResp, message => message.Contains("\"type\":\"entity.spawned\""));
        Assert.Contains("\"npcFlags\":1", spawned);
        Assert.Contains("\"offersQuests\":true", spawned);
    }

    [Fact]
    public async Task Spawn_CarriesNoNpcFlagsKeyAtAllForAPlainCreature()
    {
        var content = Ubiquitous.Game.GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(Ubiquitous.Game.GameContent.Development.Creatures)
            {
                [9401] = new(9401, "creature-9401", 3, 100),
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1, 13, 9401, 2f, 2f, 3f, 0f)], content: content);

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        var chooseResp = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var spawned = Assert.Single(chooseResp, message => message.Contains("\"type\":\"entity.spawned\""));
        Assert.DoesNotContain("npcFlags", spawned);
    }

    [Fact]
    public async Task Spawn_CarriesThePlacementsStandingAnimationWhenItHasOne()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [
                new CreatureSpawnDefinition(1, 13, 9001, 2f, 2f, 3f, 0f, AnimationId: 4321),
                new CreatureSpawnDefinition(2, 13, 9001, 3f, 2f, 3f, 0f),
            ]);

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        var chooseResp = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var spawned = chooseResp.Where(message => message.Contains("\"type\":\"entity.spawned\"")).ToList();
        Assert.Contains(spawned, message => message.Contains("\"Creature:1\"") && message.Contains("\"animationId\":4321"));
        Assert.Contains(spawned, message => message.Contains("\"Creature:2\"") && !message.Contains("animationId"));
    }

    [Fact]
    public async Task Spawn_SaysWhenTheCreatureNeverFightsBack()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9001] = new(9001, "creature-9001", 3, 100, RawExtraFlags: 1u << 7),
                [9003] = new(9003, "creature-9003", 1, 100, RawExtraFlags: 1u << 15),
                [9002] = new(9002, "creature-9002", 3, 100),
            },
            CreatureScripts = new Dictionary<uint, string> { [9003] = "creature.trainingDummy" },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [
                new CreatureSpawnDefinition(1, 13, 9001, 2f, 2f, 3f, 0f),
                new CreatureSpawnDefinition(2, 13, 9003, 3f, 2f, 3f, 0f),
                new CreatureSpawnDefinition(3, 13, 9002, 4f, 2f, 3f, 0f),
            ],
            content: content);

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        var chooseResp = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var spawned = chooseResp.Where(message => message.Contains("\"type\":\"entity.spawned\"")).ToList();
        Assert.Contains(spawned, message => message.Contains("\"Creature:1\"") && message.Contains("\"neverFightsBack\":true"));
        Assert.Contains(spawned, message => message.Contains("\"Creature:2\"") && message.Contains("\"neverFightsBack\":true"));
        Assert.Contains(spawned, message => message.Contains("\"Creature:3\"") && !message.Contains("neverFightsBack"));
    }

    [Fact]
    public async Task WalkingOutOfRange_MakesTheCreatureUntouchableAgain()
    {
        var session = await SessionAsync();
        await WalkAsync(session, 1f, 60f);

        var back = await WalkAsync(session, 60f, 1f);

        Assert.Contains(back, message => message.Contains("\"type\":\"entity.removed\""));
        var attack = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 9, new { targetId = "Creature:1" })));
        Assert.Contains("invalid_target", Assert.Single(attack));
    }

    private static async Task<List<string>> WalkAsync(PlaySession session, float from, float to)
    {
        var responses = new List<string>();
        var position = from;
        while (Math.Abs(position - to) > 0.001f)
        {
            position = Math.Abs(to - position) <= 5f ? to : position + (to > position ? 5f : -5f);
            responses.AddRange(await session.HandleTextAsync(WebMessageJson.Serialize(
                WebMessageEnvelope.Create(WebMessageTypes.PlayerMove, 3,
                    new PlayerMovePayload(position, 2, 3, 0, true, MovementState.Walking)))));
        }

        return responses;
    }

    private static async Task<PlaySession> SessionAsync()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            Spawns);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
