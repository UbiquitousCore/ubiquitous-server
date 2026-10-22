using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class AccountCharacterTests
{
    [Fact]
    public async Task DevelopmentAccount_UsesNonZeroSuccessAndDeterministicFailureCodes()
    {
        var store = new AccountAuthenticator(new DevelopmentAccountStore("developer", "dev-secret"), iterations: TestSessions.TestPasswordIterations);

        var success = await store.AuthenticateAsync("developer", "dev-secret");
        var failure = await store.AuthenticateAsync("developer", "wrong");

        Assert.True(success.Ok);
        Assert.NotEqual(LoginCode.InvalidCredentials, success.Code);
        Assert.False(failure.Ok);
        Assert.Equal(LoginCode.InvalidCredentials, failure.Code);
        Assert.Equal("Invalid credentials.", failure.Reason);
    }

    [Fact]
    public async Task CharacterStore_ListsOnlyCharactersBelongingToAccount()
    {
        var store = new InMemoryCharacterStore(
        [
            Character("char-a", 1, "Ayla"),
            Character("char-b", 2, "Borin"),
        ]);

        var characters = await store.ListForAccountAsync(1);

        var character = Assert.Single(characters);
        Assert.Equal("char-a", character.Guid);
        Assert.Equal("Ayla", character.Name);
    }

    [Fact]
    public async Task PlaySession_LoginThenListCharacters_UsesClientMessageSequence()
    {
        var account = new DevelopmentAccountStore("developer", "dev-secret");
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var session = TestSessions.Create(account, characters, developmentEntry: true);

        var login = (await session.HandleTextAsync(
            WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret")))))[0];
        var list = (await session.HandleTextAsync(
            WebMessageJson.Serialize(WebMessageEnvelope.Create(WebMessageTypes.CharactersList, 1))))[0];

        Assert.Contains("account.loginResult", login);
        Assert.Contains("\"ok\":true", login);
        Assert.Contains("characters.listed", list);
        Assert.Contains("\"name\":\"Ayla\"", list);
    }

    [Fact]
    public async Task PlaySession_RejectsListBeforeLoginAndSecondLogin()
    {
        var account = new DevelopmentAccountStore("developer", "dev-secret");
        var session = TestSessions.Create(account, new InMemoryCharacterStore([]), developmentEntry: true);

        var beforeLogin = (await session.HandleTextAsync(
            WebMessageJson.Serialize(WebMessageEnvelope.Create(WebMessageTypes.CharactersList, 0))))[0];
        await session.HandleTextAsync(
            WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 1, new LoginRequest("developer", "dev-secret"))));
        var secondLogin = (await session.HandleTextAsync(
            WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 2, new LoginRequest("developer", "dev-secret")))))[0];

        Assert.Contains("not_authenticated", beforeLogin);
        Assert.Contains("invalid_state", secondLogin);
    }

    [Fact]
    public async Task PlaySession_ChoosingCharacter_EmitsLoadingThenEntered()
    {
        var account = new DevelopmentAccountStore("developer", "dev-secret");
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var session = TestSessions.Create(account, characters, developmentEntry: true);

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        Assert.True(responses.Count >= 4);
        Assert.Contains("world.loading", responses[0]);
        Assert.Contains("world.entered", responses[1]);
        Assert.DoesNotContain("characters.chosen", string.Join("\n", responses));
        Assert.Contains("player:char-a", responses[1]);
        Assert.Contains(responses, message => message.Contains("inventory.updated"));
        Assert.Contains(responses, message => message.Contains("quest.log"));
        Assert.Contains(responses, message => message.Contains("spell.book"));
        Assert.Contains("player.updated", responses.Last());
    }

    [Fact]
    public async Task PlaySession_MovementWithinStepEmitsEntityMove_AndLargeStepIsCorrected()
    {
        var account = new DevelopmentAccountStore("developer", "dev-secret");
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var session = TestSessions.Create(account, characters, developmentEntry: true);
        var login = WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret")));
        await session.HandleTextAsync(login);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var accepted = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove,
            2,
            new PlayerMovePayload(2, 2, 3, 0.5, true, MovementState.Walking))));
        var corrected = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove,
            3,
            new PlayerMovePayload(100, 100, 100, 1, true, MovementState.Walking))));

        Assert.Contains("entity.moved", accepted.Single());
        Assert.Contains("move.accepted", corrected.Single());
        Assert.Contains("\"pos\"", corrected.Single());
        Assert.Contains("\"y\"", corrected.Single());
    }

    [Fact]
    public async Task PlaySession_WalkingAcrossManyZonesOnOneMap_NeverEmitsAWorldChange_OnlyTheMapEdgeCorrects()
    {
        var content = GameContent.Development with
        {
            Maps = new Dictionary<ushort, MapDefinition>
            {
                [13] = new MapDefinition(13, "test-city", "world/city.wld", MinX: -704, MaxX: 320, MinZ: -448, MaxZ: 576),
            },
        };
        var account = new DevelopmentAccountStore("developer", "dev-secret");
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var session = TestSessions.Create(account, characters, content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var seq = 2;
        var responses = new List<string>();
        for (var x = 11; x <= 311; x += 10)
        {
            var reply = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.PlayerMove, seq++, new PlayerMovePayload(x, 2, 3, 0, true, MovementState.Walking))));
            responses.AddRange(reply);
        }

        Assert.All(responses, message => Assert.Contains("entity.moved", message));
        Assert.All(responses, message => Assert.DoesNotContain("world.loading", message));
        Assert.All(responses, message => Assert.DoesNotContain("world.entered", message));

        var atTheEdge = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, seq, new PlayerMovePayload(321, 2, 3, 0, true, MovementState.Walking))));

        Assert.Contains("move.accepted", atTheEdge.Single());
        Assert.DoesNotContain("entity.moved", atTheEdge.Single());
        Assert.DoesNotContain("world.loading", atTheEdge.Single());
        Assert.DoesNotContain("world.entered", atTheEdge.Single());
    }

    [Fact]
    public async Task PlaySession_WalkingIntoAConfiguredGate_FiresAMapChange()
    {
        var content = GameContent.Development with
        {
            MapGates =
            [
                new MapGateDefinition(
                    SourceMapId: 13, SourceMinX: -320, SourceMaxX: -255, SourceMinZ: 250, SourceMaxZ: 304,
                    DestinationMapId: 1, DestinationX: 40, DestinationY: 0, DestinationZ: 60, DestinationYaw: 1.2f),
            ],
        };
        var account = new DevelopmentAccountStore("developer", "dev-secret");
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var session = TestSessions.Create(account, characters, content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var outsideTheGate = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 2, new PlayerMovePayload(5, 2, 3, 0, true, MovementState.Walking))));

        Assert.Contains("entity.moved", outsideTheGate.Single());
        Assert.DoesNotContain("world.loading", outsideTheGate.Single());

        const int steps = 30;
        var seq = 3;
        for (var i = 1; i <= steps; i++)
        {
            var x = 5 + (-300 - 5) * i / (float)steps;
            var z = 3 + (245 - 3) * i / (float)steps;
            var reply = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.PlayerMove, seq++, new PlayerMovePayload(x, 0, z, 0, true, MovementState.Walking))));
            Assert.Contains("entity.moved", reply.Single());
        }

        var throughTheGate = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, seq, new PlayerMovePayload(-300, 0, 260, 0, true, MovementState.Walking))));

        Assert.DoesNotContain(throughTheGate, message => message.Contains("entity.moved"));
        Assert.Contains(throughTheGate, message => message.Contains("world.loading") && message.Contains("\"map\":1"));
        Assert.Contains(throughTheGate, message => message.Contains("world.entered") && message.Contains("\"map\":1"));
        Assert.Contains(throughTheGate, message => message.Contains("\"x\":40") && message.Contains("\"z\":60"));
    }

    [Fact]
    public async Task PlaySession_WalkingOutOfTheCityThroughTheCityGate_EntersTheForest()
    {
        var account = new DevelopmentAccountStore("developer", "dev-secret");
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var session = TestSessions.Create(account, characters, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        const int steps = 30;
        var seq = 2;
        for (var i = 1; i <= steps; i++)
        {
            var x = 1 + (-285 - 1) * i / (float)steps;
            var z = 3 + (355 - 3) * i / (float)steps;
            var reply = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.PlayerMove, seq++, new PlayerMovePayload(x, 0, z, 0, true, MovementState.Walking))));
            Assert.Contains("entity.moved", reply.Single());
        }

        var throughTheGate = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, seq, new PlayerMovePayload(-285, 0, 368, 0, true, MovementState.Walking))));

        Assert.DoesNotContain(throughTheGate, message => message.Contains("entity.moved"));
        Assert.Contains(throughTheGate, message => message.Contains("world.loading") && message.Contains("\"map\":1"));
        Assert.Contains(throughTheGate, message => message.Contains("world.entered") && message.Contains("\"map\":1"));
        Assert.Contains(throughTheGate, message => message.Contains("\"x\":-915") && message.Contains("\"z\":-293"));
    }

    [Fact]
    public async Task PlaySession_AdminAccount_HasTheAntiTeleportCapBypassedEntirely()
    {
        var account = new DevelopmentAccountStore("developer", "dev-secret", isAdmin: true);
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var session = TestSessions.Create(account, characters, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var farJump = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove,
            2,
            new PlayerMovePayload(10_000, 100, 10_000, 1, true, MovementState.Walking))));

        Assert.Contains("entity.moved", farJump.Single());
        Assert.DoesNotContain("move.accepted", farJump.Single());
    }

    [Fact]
    public async Task PlaySession_AdminAccountJumpingStraightOverTheCityGate_StillEntersTheForest()
    {
        var account = new DevelopmentAccountStore("developer", "dev-secret", isAdmin: true);
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var session = TestSessions.Create(account, characters, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var approach = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 2, new PlayerMovePayload(-285, 0, 350, 0, true, MovementState.Walking))));
        Assert.Contains("entity.moved", approach.Single());

        var straightOverIt = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 3, new PlayerMovePayload(-285, 0, 400, 0, true, MovementState.Walking))));

        Assert.DoesNotContain(straightOverIt, message => message.Contains("entity.moved"));
        Assert.Contains(straightOverIt, message => message.Contains("world.loading") && message.Contains("\"map\":1"));
        Assert.Contains(straightOverIt, message => message.Contains("world.entered") && message.Contains("\"map\":1"));
        Assert.Contains(straightOverIt, message => message.Contains("\"x\":-915") && message.Contains("\"z\":-293"));
    }

    [Fact]
    public async Task NewSession_DoesNotInheritSelectedCharacterOrEntityIds()
    {
        var account = new DevelopmentAccountStore("developer", "dev-secret");
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var spawns = new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) };

        var first = TestSessions.Create(account, characters, spawns, developmentEntry: true);
        await first.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        var firstEntry = await first.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var second = TestSessions.Create(account, characters, spawns, developmentEntry: true);
        var beforeLogin = await second.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersList, 0)));
        await second.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 1, new LoginRequest("developer", "dev-secret"))));
        var secondEntry = await second.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 2, new ChooseCharacterRequest("Ayla"))));

        Assert.Contains("world.entered", firstEntry[1]);
        Assert.Contains(firstEntry, m => m.Contains("Creature:1"));
        Assert.Contains("not_authenticated", beforeLogin.Single());
        Assert.Contains(secondEntry, m => m.Contains("Creature:1"));
    }

    [Fact]
    public async Task PlaySession_ChatAfterWorldEntry_EmitsChatReceived()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerChat, 2, new ChatPayload("say", " hello "))));

        Assert.Contains("chat.received", responses.Single());
        Assert.Contains("\"sender\":\"Ayla\"", responses.Single());
        Assert.Contains("\"text\":\"hello\"", responses.Single());
    }

    [Fact]
    public async Task PlaySession_TargetBeforeWorldEntry_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.TargetChange, 0, new { networkId = 7 })));

        Assert.Contains("not_in_world", responses.Single());
    }

    [Fact]
    public async Task PlaySession_GossipHelloForVisibleNpc_EmitsMenu()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.NpcTalk, 2, new { networkId = "Creature:1" })));

        Assert.Contains("npc.gossip", response.Single());
        Assert.Contains("\"entry\":9001", response.Single());
    }

    [Fact]
    public async Task PlaySession_GossipBackForVisibleNpc_ReopensTheSameMenu()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.NpcPreviousPage, 2, new { networkId = "Creature:1" })));

        Assert.Contains("npc.gossip", response.Single());
        Assert.Contains("\"entry\":9001", response.Single());
    }

    [Fact]
    public async Task PlaySession_GossipPickOnAPageTheNpcDoesNotOffer_IsRefused()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.NpcPickOption, 2, new { networkId = "Creature:1", menuId = 1, choice = 1 })));

        Assert.Contains("invalid_gossip", response.Single());
        Assert.Contains("not offered here", response.Single());
    }

    [Fact]
    public async Task PlaySession_GossipBackForANpcThatIsNoLongerVisible_IsRefused()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.NpcPreviousPage, 2, new { networkId = "Creature:1" })));

        Assert.Contains("invalid_gossip", response.Single());
    }

    [Fact]
    public async Task PlaySession_QuestStatusForVisibleNpc_EmitsAvailableStatus()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestCheckMark, 2, new { networkId = "Creature:1" })));

        Assert.Contains("quest.statuses", response.Single());
        Assert.Contains("offered", response.Single());
    }

    [Fact]
    public async Task PlaySession_QuestListForVisibleNpc_EmitsNumericTextIds()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestList, 2, new { networkId = "Creature:1" })));

        Assert.Contains("quest.details", response.Single());
        Assert.Contains("12001", response.Single());
    }

    [Fact]
    public async Task PlaySession_QuestAccept_EmitsPartialUpdatedQuest()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5001 })));

        Assert.Contains("quest.updated", response.Single());
        Assert.Contains("underway", response.Single());
        Assert.Contains("12001", response.Single());
    }

    [Fact]
    public async Task PlaySession_QuestCompleteAfterAccept_EmitsFinishedUpdate()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5001 })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 3, new { networkId = "Creature:1", questId = 5001, rewardIndex = 0 })));

        Assert.Contains(response, message => message.Contains("quest.updated")
            && message.Contains("complete"));
        Assert.Equal(2, response.Count(message => message.Contains("inventory.itemAdded")));
        Assert.Contains(response, message => message.Contains("player.updated")
            && message.Contains("\"gold\":5250"));
    }

    [Fact]
    public async Task PlaySession_QuestDestroyAfterAccept_EmitsQuestRemoved()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5001 })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestDestroy, 3, new { slot = 0, questId = 5001 })));

        Assert.Contains("quest.removed", response.Single());
        Assert.Contains("5001", response.Single());
    }

    [Fact]
    public async Task PlaySession_QuestDestroyWithStaleSlot_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5001 })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestDestroy, 3, new { slot = 7, questId = 5001 })));

        Assert.Contains("invalid_quest", response.Single());
    }

    [Fact]
    public async Task PlaySession_InventoryMove_EmitsItemMoved()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryMove, 0,
            new { from = new { area = "bag", index = 0 }, to = new { area = "bag", index = 3 } })));

        Assert.Contains("not_in_world", response.Single());
    }

    [Fact]
    public async Task PlaySession_InventoryEquipBeforeWorldEntry_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryEquip, 0,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item", equip = true })));

        Assert.Contains("not_in_world", response.Single());
    }

    [Fact]
    public async Task PlaySession_InventoryDestroyBeforeWorldEntry_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryDestroy, 0,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item", count = 1 })));
        Assert.Contains("not_in_world", response.Single());
    }

    [Fact]
    public async Task PlaySession_InventorySplitBeforeWorldEntry_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventorySplit, 0,
            new
            {
                from = new { area = "bag", index = 0 },
                itemId = "starter-item",
                count = 1,
                to = new { area = "bag", index = 1 }
            })));
        Assert.Contains("not_in_world", response.Single());
    }

    [Fact]
    public async Task PlaySession_InventorySortBeforeWorldEntry_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventorySort, 0, new { area = "bag" })));
        Assert.Contains("not_in_world", response.Single());
    }

    [Fact]
    public async Task PlaySession_InventorySort_PacksTheBagAndResendsTheSnapshot()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventorySort, 1, new { area = "bag" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"inventory.sorted\"") && m.Contains("\"ok\":true") && m.Contains("\"area\":\"bag\""));
        Assert.Contains(responses, m => m.Contains("\"type\":\"inventory.updated\""));
    }

    [Fact]
    public async Task PlaySession_InventorySort_AlsoSortsTheBank()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventorySort, 1, new { area = "bank" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"inventory.sorted\"") && m.Contains("\"ok\":true") && m.Contains("\"area\":\"bank\""));
        Assert.Contains(responses, m => m.Contains("\"type\":\"inventory.updated\""));
    }

    [Fact]
    public async Task PlaySession_InventorySort_RefusesAnUnrecognizedArea()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventorySort, 1, new { area = "vault" })));

        var reply = Assert.Single(responses);
        Assert.Contains("\"type\":\"inventory.sorted\"", reply);
        Assert.Contains("\"ok\":false", reply);
    }

    [Fact]
    public async Task PlaySession_InventoryOpenBankBeforeWorldEntry_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryOpenBank, 0, new { networkId = "StaticObject:1" })));
        Assert.Contains("not_in_world", response.Single());
    }

    [Fact]
    public async Task PlaySession_InventoryOpenBank_RefusesABodyThatIsNotTheBank()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            new CreatureSpawnDefinition[] { new(1001, 13, 9001, 2, 2, 4, 0) }, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryOpenBank, 1, new { networkId = "Creature:1" })));

        var reply = Assert.Single(responses);
        Assert.Contains("\"type\":\"inventory.bankOpen\"", reply);
        Assert.Contains("\"open\":false", reply);
    }

    [Fact]
    public async Task PlaySession_InventoryOpenBank_OpensTheDevBankStaticObject()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            staticObjectSpawns: new[] { new StaticObjectSpawnDefinition(2001, 13, 9100, 2, 2, 4, 0) }, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryOpenBank, 1, new { networkId = "StaticObject:1" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"inventory.bankOpen\"") && m.Contains("\"open\":true"));
        Assert.Contains(responses, m => m.Contains("\"type\":\"inventory.bankUpdated\"") && m.Contains("\"bankSlots\":24"));
    }

    [Fact]
    public async Task PlaySession_InventoryUpgradeStorage_ClimbsOneRungAndCharges()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryExpand, 1, new { area = "bag", slots = 18 })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"inventory.capacityChanged\"") && m.Contains("\"area\":\"bag\"") && m.Contains("\"capacity\":18"));
        Assert.Contains(responses, m => m.Contains("\"type\":\"player.updated\""));
    }

    [Fact]
    public async Task PlaySession_EnterAsCharacter_KnowsItsOwnClassesSpells()
    {
        var ranger = Character("char-a", 1, "Ayla") with { Race = PlayerRaceIds.Human, Class = PlayerClassIds.Ranger };
        var warrior = Character("char-b", 1, "Kael") with { Race = PlayerRaceIds.Human, Class = PlayerClassIds.Warrior };
        var store = new InMemoryCharacterStore([ranger, warrior]);

        async Task<string> SpellsOf(string guid)
        {
            var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), store, developmentEntry: true);
            var said = await session.HandleTextAsync(WebMessageJson.Serialize(
                WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = guid })));
            return said.First(m => m.Contains("\"type\":\"spell.book\""));
        }

        var theirs = await SpellsOf("char-a");
        var his = await SpellsOf("char-b");
        Assert.NotEqual(theirs, his);

        var rangerBook = StartingLoadouts.For(PlayerRaceIds.Human, PlayerClassIds.Ranger)!;
        var warriorBook = StartingLoadouts.For(PlayerRaceIds.Human, PlayerClassIds.Warrior)!;
        Assert.Contains(rangerBook.Spells[0].ToString(), theirs);
        Assert.Contains(warriorBook.Spells[0].ToString(), his);
    }

    [Fact]
    public async Task PlaySession_InventoryUpgradeStorage_WritesTheRoomItBoughtToTheCharacter()
    {
        var store = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), store, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryExpand, 1, new { area = "bag", slots = 18 })));

        var saved = await store.GetByGuidAsync("char-a");
        Assert.Equal(18, saved!.BagSlots);
    }

    [Fact]
    public async Task PlaySession_EnterAsCharacter_KeepsTheRoomAlreadyBought()
    {
        var bought = Character("char-a", 1, "Ayla") with { BagSlots = 18 };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([bought]), developmentEntry: true);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"inventory.updated\"") && m.Contains("\"bagSlots\":18"));

        var again = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryExpand, 1, new { area = "bag", slots = 18 })));
        Assert.DoesNotContain(again, m => m.Contains("\"type\":\"inventory.capacityChanged\""));

        var next = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryExpand, 2, new { area = "bag", slots = 24 })));
        Assert.Contains(next, m => m.Contains("\"type\":\"inventory.capacityChanged\"") && m.Contains("\"capacity\":24"));
    }

    [Fact]
    public async Task PlaySession_InventoryUpgradeStorage_RefusesSkippingARung()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryExpand, 1, new { area = "bag", slots = 24 })));

        Assert.Contains("\"code\":\"upgrade_refused\"", Assert.Single(responses));
    }

    [Fact]
    public async Task PlaySession_InventoryUseBeforeWorldEntry_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 0,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item", targetId = "Creature:1" })));
        Assert.Contains("not_in_world", response.Single());
    }

    [Fact]
    public async Task PlaySession_InventoryUse_CastsTheItemsOnUseSpellAndConsumesIt()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            spellRollUnit: () => 1.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 1,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item", targetId = "Creature:1" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"damage\":20"));
        Assert.Contains(responses, m => m.Contains("\"type\":\"inventory.itemRemoved\""));
    }

    [Fact]
    public async Task PlaySession_InventoryUse_RefusesAnItemWithNoOnUseSpell()
    {
        var contentWithoutOnUse = GameContent.Development with
        {
            Items = new Dictionary<uint, ItemDefinition> { [100] = new(100, "item-100", 0, 0, 100) },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: contentWithoutOnUse, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 1,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item", targetId = "Creature:1" })));

        Assert.Contains("cannot_use", Assert.Single(responses));
    }

    [Fact]
    public async Task PlaySession_InventoryUse_RefusesWithNoValidTarget()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 1,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item", targetId = "" })));

        Assert.Contains("cannot_use", Assert.Single(responses));
    }

    [Fact]
    public async Task PlaySession_InventoryUse_HealsWithNoTargetRequiredAndStillConsumes()
    {
        var healContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [105] = new(105, "spell-105", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 0, EffectHeal: 40),
            },
            Items = new Dictionary<uint, ItemDefinition>
            {
                [100] = new(100, "item-100", Class: 0, SubClass: 1, DisplayInfoId: 100, OnUseSpellId: 105),
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            content: healContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 1,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"result\":\"heal\""));
        Assert.Contains(responses, m => m.Contains("\"type\":\"inventory.itemRemoved\""));
    }

    [Fact]
    public async Task PlaySession_InventoryUse_RefusesAnItemBarredToTheCharactersRace()
    {
        var content = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [105] = new(105, "spell-105", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 0, EffectHeal: 40),
            },
            Items = new Dictionary<uint, ItemDefinition>
            {
                [100] = new(100, "item-100", Class: 0, SubClass: 1, DisplayInfoId: 100, OnUseSpellId: 105,
                    AllowableRaceMask: ItemRaceMask.BitFor(1)),
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 1,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

        Assert.Contains("You cannot use this item.", Assert.Single(responses));
    }

    [Fact]
    public async Task PlaySession_InventoryUse_KeepsAnItemThatCanBeWorn()
    {
        var wornContent = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
            {
                [105] = new(105, "spell-105", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 0, EffectHeal: 40),
            },
            Items = new Dictionary<uint, ItemDefinition>
            {
                [100] = new(100, "item-100", Class: 4, SubClass: 0, DisplayInfoId: 100, OnUseSpellId: 105, InventoryType: 12),
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            content: wornContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 1,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"spell.outcome\""));
        Assert.DoesNotContain(responses, m => m.Contains("\"type\":\"inventory.itemRemoved\""));
        Assert.DoesNotContain(responses, m => m.Contains("\"type\":\"inventory.stackChanged\""));
    }

    [Fact]
    public async Task PlaySession_InventoryUse_OffersTheItemsQuestInsteadOfCasting()
    {
        var questItemContent = GameContent.Development with
        {
            Items = new Dictionary<uint, ItemDefinition>
            {
                [100] = new(100, "item-100", Class: 10, SubClass: 0, DisplayInfoId: 100, QuestId: 5001),
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            content: questItemContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 1,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

        var reply = Assert.Single(responses);
        Assert.Contains("\"type\":\"quest.details\"", reply);
        Assert.Contains("\"giverId\":\"item:100\"", reply);
        Assert.Contains("\"id\":5001", reply);
        Assert.DoesNotContain(responses, m => m.Contains("\"type\":\"inventory.itemRemoved\""));
    }

    [Fact]
    public async Task PlaySession_InventoryUse_RefusesAQuestStarterItemForAQuestAlreadyHeld()
    {
        var questItemContent = GameContent.Development with
        {
            Items = new Dictionary<uint, ItemDefinition>
            {
                [100] = new(100, "item-100", Class: 10, SubClass: 0, DisplayInfoId: 100, QuestId: 5001),
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: questItemContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5001 })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 3,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

        Assert.Contains("cannot_use", Assert.Single(responses));
    }

    [Fact]
    public async Task PlaySession_QuestAccept_AcceptsFromTheQuestStarterItemThatOfferedIt()
    {
        var questItemContent = GameContent.Development with
        {
            Items = new Dictionary<uint, ItemDefinition>
            {
                [100] = new(100, "item-100", Class: 10, SubClass: 0, DisplayInfoId: 100, QuestId: 5001),
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            content: questItemContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 1,
            new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "item:100", questId = 5001 })));

        Assert.Contains("\"type\":\"quest.updated\"", Assert.Single(response));
    }

    [Fact]
    public async Task PlaySession_QuestAccept_RefusesAnItemGiverNamingTheWrongQuest()
    {
        var questItemContent = GameContent.Development with
        {
            Items = new Dictionary<uint, ItemDefinition>
            {
                [100] = new(100, "item-100", Class: 10, SubClass: 0, DisplayInfoId: 100, QuestId: 5001),
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            content: questItemContent, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 1, new { networkId = "item:100", questId = 9999 })));

        Assert.Contains("invalid_quest", Assert.Single(response));
    }

    [Fact]
    public async Task PlaySession_QuestList_OffersAQuestFromAStaticObjectStarter()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5003, null, 12007, 12008, 1)],
            QuestStartersByStaticObject = [new StaticObjectQuestBindingDefinition(9101, 5003)],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            staticObjectSpawns: new[] { new StaticObjectSpawnDefinition(2001, 13, 9101, 2, 2, 4, 0) },
            content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestList, 1, new { networkId = "StaticObject:1" })));

        var reply = Assert.Single(responses);
        Assert.Contains("\"type\":\"quest.details\"", reply);
        Assert.Contains("\"giverId\":\"StaticObject:1\"", reply);
        Assert.Contains("\"id\":5003", reply);
    }

    [Fact]
    public async Task PlaySession_QuestAccept_AcceptsFromAStaticObjectStarter()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5003, null, 12007, 12008, 1)],
            QuestStartersByStaticObject = [new StaticObjectQuestBindingDefinition(9101, 5003)],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            staticObjectSpawns: new[] { new StaticObjectSpawnDefinition(2001, 13, 9101, 2, 2, 4, 0) },
            content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 1, new { networkId = "StaticObject:1", questId = 5003 })));

        Assert.Contains("\"type\":\"quest.updated\"", Assert.Single(response));
    }

    [Fact]
    public async Task PlaySession_QuestComplete_FinishesAtAStaticObjectFinisher()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5003, null, 12007, 12008, 1)],
            QuestRewards = new Dictionary<uint, QuestRewardDefinition> { [5003] = new(5003, 10, [], []) },
            QuestStartersByStaticObject = [new StaticObjectQuestBindingDefinition(9101, 5003)],
            QuestFinishersByStaticObject = [new StaticObjectQuestBindingDefinition(9101, 5003)],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            staticObjectSpawns: new[] { new StaticObjectSpawnDefinition(2001, 13, 9101, 2, 2, 4, 0) },
            content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 1, new { networkId = "StaticObject:1", questId = 5003 })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 2, new { networkId = "StaticObject:1", questId = 5003 })));

        Assert.Contains(response, m => m.Contains("\"type\":\"quest.updated\""));
    }

    [Fact]
    public async Task PlaySession_QuestComplete_RefusesAStaticObjectThatIsOnlyAStarterNotAFinisher()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5003, null, 12007, 12008, 1)],
            QuestRewards = new Dictionary<uint, QuestRewardDefinition> { [5003] = new(5003, 10, [], []) },
            QuestStartersByStaticObject = [new StaticObjectQuestBindingDefinition(9101, 5003)],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            staticObjectSpawns: new[] { new StaticObjectSpawnDefinition(2001, 13, 9101, 2, 2, 4, 0) },
            content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 1, new { networkId = "StaticObject:1", questId = 5003 })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 2, new { networkId = "StaticObject:1", questId = 5003 })));

        Assert.Contains("invalid_quest", Assert.Single(response));
    }

    [Fact]
    public async Task PlaySession_QuestComplete_RefusesAnNpcThatIsNotTheFinisherWhenOneIsBound()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5003, 9001, 12007, 12008, 1)],
            QuestRewards = new Dictionary<uint, QuestRewardDefinition> { [5003] = new(5003, 10, [], []) },
            QuestFinishersByCreature = [new CreatureQuestBindingDefinition(9002, 5003)],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            new[]
            {
                new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0),
                new CreatureSpawnDefinition(1002, 13, 9002, 6, 6, 4, 0),
            },
            content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 1, new { networkId = "Creature:1", questId = 5003 })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 2, new { networkId = "Creature:1", questId = 5003 })));

        Assert.Contains("invalid_quest", Assert.Single(response));
    }

    [Fact]
    public async Task PlaySession_QuestComplete_AcceptsTheBoundCreatureFinisher()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5003, 9001, 12007, 12008, 1)],
            QuestRewards = new Dictionary<uint, QuestRewardDefinition> { [5003] = new(5003, 10, [], []) },
            QuestFinishersByCreature = [new CreatureQuestBindingDefinition(9002, 5003)],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            new[]
            {
                new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0),
                new CreatureSpawnDefinition(1002, 13, 9002, 6, 6, 4, 0),
            },
            content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 1, new { networkId = "Creature:1", questId = 5003 })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 2, new { networkId = "Creature:2", questId = 5003 })));

        Assert.Contains(response, m => m.Contains("\"type\":\"quest.updated\""));
    }

    [Fact]
    public async Task PlaySession_QuestComplete_StaysLenientForAQuestWithNoFinisherBindingAtAll()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5003, 9001, 12007, 12008, 1)],
            QuestRewards = new Dictionary<uint, QuestRewardDefinition> { [5003] = new(5003, 10, [], []) },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 1, new { networkId = "Creature:1", questId = 5003 })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 2, new { networkId = "Creature:1", questId = 5003 })));

        Assert.Contains(response, m => m.Contains("\"type\":\"quest.updated\""));
    }

    [Fact]
    public async Task PlaySession_StaticObjectInteract_GrantsTheBoundItemWhileTheQuestStillNeedsIt()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5003, 9001, 12007, 12008, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Held, GoalCount: 2, ObjectiveItem: 300)])],
            QuestItemBindingsByStaticObject = [new StaticObjectItemBindingDefinition(9101, 5003, 300)],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            staticObjectSpawns: new[] { new StaticObjectSpawnDefinition(2001, 13, 9101, 2, 2, 4, 0) },
            content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 1, new { networkId = "Creature:1", questId = 5003 })));

        var first = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.StaticObjectInteract, 2, new { networkId = "StaticObject:2" })));
        Assert.Contains(first, m => m.Contains("\"type\":\"inventory.itemAdded\"") && m.Contains("\"entry\":300"));

        var second = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.StaticObjectInteract, 3, new { networkId = "StaticObject:2" })));
        Assert.Contains(second, m => m.Contains("\"type\":\"inventory.itemAdded\"") && m.Contains("\"entry\":300"));
        Assert.Contains(second, m => m.Contains("\"type\":\"quest.updated\""));

        var third = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.StaticObjectInteract, 4, new { networkId = "StaticObject:2" })));
        Assert.Contains("invalid_staticobject", Assert.Single(third));
    }

    [Fact]
    public async Task PlaySession_StaticObjectInteract_RefusesWithoutHoldingTheBoundQuest()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5003, 9001, 12007, 12008, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Held, GoalCount: 2, ObjectiveItem: 300)])],
            QuestItemBindingsByStaticObject = [new StaticObjectItemBindingDefinition(9101, 5003, 300)],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            staticObjectSpawns: new[] { new StaticObjectSpawnDefinition(2001, 13, 9101, 2, 2, 4, 0) },
            content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.StaticObjectInteract, 1, new { networkId = "StaticObject:1" })));

        Assert.Contains("invalid_staticobject", Assert.Single(response));
    }

    [Fact]
    public async Task PlaySession_StaticObjectInteract_SatisfiesATriggeredObjectiveDirectly()
    {
        var content = GameContent.Development with
        {
            Quests = [new QuestDefinition(5004, 9001, 12009, 12010, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Triggered, GoalCount: 1, ObjectiveObject: 9101)])],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            new[] { new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0) },
            staticObjectSpawns: new[] { new StaticObjectSpawnDefinition(2001, 13, 9101, 2, 2, 4, 0) },
            content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 1, new { networkId = "Creature:1", questId = 5004 })));

        var first = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.StaticObjectInteract, 2, new { networkId = "StaticObject:2" })));
        Assert.Contains(first, m => m.Contains("\"type\":\"quest.updated\""));
        Assert.DoesNotContain(first, m => m.Contains("inventory.itemAdded"));

        var second = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.StaticObjectInteract, 3, new { networkId = "StaticObject:2" })));
        Assert.Contains("invalid_staticobject", Assert.Single(second));
    }

    [Fact]
    public async Task PlaySession_StaticObjectInteract_RefusesAStaticObjectWithNoBinding()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            staticObjectSpawns: new[] { new StaticObjectSpawnDefinition(2001, 13, 9101, 2, 2, 4, 0) }, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.StaticObjectInteract, 1, new { networkId = "StaticObject:1" })));

        Assert.Contains("invalid_staticobject", Assert.Single(response));
    }

    [Fact]
    public async Task FileStores_LoadSyntheticAccountAndCharacterPackages()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-stores-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "accounts.json"), "[{\"id\":1,\"userId\":\"developer\",\"passHash\":\"dev-secret\"}]");
            File.WriteAllText(Path.Combine(directory, "characters.json"), "[{\"guid\":\"char-a\",\"accountId\":1,\"name\":\"Ayla\",\"gender\":1,\"race\":2,\"class\":3,\"displayInfoId\":4,\"skin\":5,\"hairStyle\":6,\"hairColour\":7,\"face\":8,\"mapId\":13,\"x\":1,\"y\":2,\"z\":3,\"level\":4,\"equipment\":[100]}]");

            var accounts = new AccountAuthenticator(
                FileAccountStore.Load(Path.Combine(directory, "accounts.json")), iterations: TestSessions.TestPasswordIterations);
            var characters = FileCharacterStore.Load(Path.Combine(directory, "characters.json"));

            Assert.True((await accounts.AuthenticateAsync("developer", "dev-secret")).Ok);
            Assert.Equal("Ayla", (await characters.ListForAccountAsync(1)).Single().Name);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task PlaySession_VendorHelloForVisibleVendor_EmitsVendorItems()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorOpen, 2, new { networkId = "Creature:1" })));

        Assert.Contains("npc.vendorItems", response.Single());
        Assert.Contains("buyPrice", response.Single());
    }

    [Fact]
    public async Task PlaySession_AuctioneerHelloForVisibleAuctioneer_OpensTheMarket()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9003, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AuctioneerOpen, 2, new { networkId = "Creature:1" })));

        Assert.Contains("auction.opened", response.Single());
    }

    [Fact]
    public async Task PlaySession_AuctioneerHelloForSomeoneWhoIsNot_IsRefused()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AuctioneerOpen, 2, new { networkId = "Creature:1" })));

        Assert.Contains("invalid_auctioneer", response.Single());
    }

    [Fact]
    public async Task PlaySession_VendorHelloBeforeWorldEntry_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorOpen, 0, new { networkId = "Creature:1" })));

        Assert.Contains("invalid_vendor", response.Single());
    }

    [Fact]
    public async Task PlaySession_VendorBuy_EmitsBuyResultItemAddedAndNewBalance()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorBuy, 2, new { networkId = "Creature:1", slot = 0, entry = 100, count = 2 })));

        Assert.Equal(3, response.Count);
        Assert.Contains("npc.buyResult", response[0]);
        Assert.Contains("\"code\":\"ok\"", response[0]);
        Assert.Contains("inventory.itemAdded", response[1]);
        Assert.Contains("player.updated", response[2]);
        Assert.Contains("4900", response[2]);
    }

    [Fact]
    public async Task PlaySession_VendorBuyUnknownRow_IsRefusedWithItemCode()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorBuy, 2, new { networkId = "Creature:1", slot = 0, entry = 999, count = 1 })));

        Assert.Contains("npc.buyResult", response.Single());
        Assert.Contains("\"code\":\"vendorDoesNotSell\"", response.Single());
    }

    [Fact]
    public async Task PlaySession_VendorSell_EmitsSellResultItemRemovedAndNewBalance()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorSell, 2,
            new { networkId = "Creature:1", from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

        Assert.Equal(3, response.Count);
        Assert.Contains("npc.sellResult", response[0]);
        Assert.Contains("\"code\":\"ok\"", response[0]);
        Assert.Contains("inventory.itemRemoved", response[1]);
        Assert.Contains("starter-item", response[1]);
        Assert.Contains("player.updated", response[2]);
        Assert.Contains("5025", response[2]);
    }

    [Fact]
    public async Task PlaySession_VendorSell_UsesTheItemsOwnSellPriceWhenItHasOne()
    {
        var content = TestSessions.WithTestPurse(GameContent.Development) with
        {
            Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
            {
                [100] = GameContent.Development.Items[100] with { SellPrice = 17 },
            },
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            content: content, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorSell, 2,
            new { networkId = "Creature:1", from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

        Assert.Contains(response, m => m.Contains("\"type\":\"player.updated\"") && m.Contains("5017"));
    }

    [Fact]
    public async Task PlaySession_VendorSellAnItemTheBagDoesNotHold_IsRefused()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorSell, 3,
            new { networkId = "Creature:1", from = new { area = "bag", index = 5 }, itemId = "no-such-item" })));

        Assert.Contains("npc.sellResult", response.Single());
        Assert.Contains("\"code\":\"itemNotInSlot\"", response.Single());
    }

    [Fact]
    public async Task PlaySession_VendorSellBeforeWorldEntry_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorSell, 0,
            new { networkId = "Creature:1", from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

        Assert.Contains("npc.sellResult", response.Single());
        Assert.Contains("\"code\":\"notAVendor\"", response.Single());
    }

    [Fact]
    public async Task PlaySession_VendorBuyBeforeWorldEntry_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorBuy, 0, new { networkId = "Creature:1", slot = 0, entry = 100, count = 1 })));

        Assert.Contains("invalid_vendor", response.Single());
    }

    [Fact]
    public async Task PlaySession_AttackStart_EmitsMeleeStartAndEnterCombat()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        Assert.Equal(4, response.Count);
        Assert.Contains("attack.meleeStart", response[0]);
        Assert.Contains("\"attackerId\":\"player:char-a\"", response[0]);
        Assert.Contains("attack.meleeStart", response[1]);
        Assert.Contains("\"attackerId\":\"Creature:1\"", response[1]);
        Assert.Contains("attack.enterCombat", response[2]);
        Assert.Contains("Creature:1", response[3]);
    }

    [Fact]
    public async Task PlaySession_AttackStartRepeatedTarget_EmitsNothing()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:1" })));

        Assert.Empty(response);
    }

    [Fact]
    public async Task PlaySession_AttackStartInvisibleTarget_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:9" })));

        Assert.Contains("invalid_target", response.Single());
    }

    [Fact]
    public async Task PlaySession_AttackStartBeforeWorldEntry_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]), developmentEntry: true);
        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 0, new { targetId = "Creature:1" })));

        Assert.Contains("invalid_target", response.Single());
    }

    [Fact]
    public async Task PlaySession_TickCombat_SwingsHitsAndKills()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            combatRollUnit: () => 1.0, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var swings = new List<string>(session.TickCombat(DateTimeOffset.UtcNow));
        Assert.Contains(swings, message => message.Contains("attack.swingResult") && message.Contains("87"));

        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        for (var swing = 0; swing < 20 && !swings.Any(message => message.Contains("unit.died")); swing++)
        {
            swings.AddRange(session.TickCombat(now));
            now += TimeSpan.FromSeconds(2);
        }

        Assert.Contains(swings, message => message.Contains("attack.swingResult") && message.Contains("87"));
        Assert.Contains(swings, message => message.Contains("entity.updated"));
        var death = swings.First(message => message.Contains("unit.died"));
        var deathIndex = swings.IndexOf(death);
        Assert.Contains("entity.lootable", swings[deathIndex + 1]);
        Assert.Contains("attack.halted", swings[deathIndex + 2]);
        Assert.Contains("attack.exitCombat", swings[deathIndex + 3]);
    }

    [Fact]
    public async Task PlaySession_KillWithNoLoot_AnnouncesEmptiedInsteadOfLootable()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9500] = new(9500, "creature-9500", 3, 100, MoneyDropMin: 0, MoneyDropMax: 0),
            },
        };
        var session = await LootSessionAsync([new CreatureSpawnDefinition(1001, 13, 9500, 2, 2, 4, 0)], content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var swings = new List<string>();
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        for (var swing = 0; swing < 30 && !swings.Any(message => message.Contains("unit.died")); swing++)
        {
            swings.AddRange(session.TickCombat(now));
            now += TimeSpan.FromSeconds(2);
        }

        var death = swings.First(message => message.Contains("unit.died"));
        var lootable = swings[swings.IndexOf(death) + 1];
        Assert.Contains("entity.lootable", lootable);
        Assert.Contains("emptied", lootable);
    }

    [Fact]
    public async Task PlaySession_AttackStartDeadTarget_IsRejected()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        for (var swing = 0; swing < 20; swing++)
        {
            session.TickCombat(now);
            now += TimeSpan.FromSeconds(2);
        }

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:1" })));

        Assert.Contains("invalid_target", response.Single());
    }

    [Fact]
    public async Task PlaySession_LootOpenLivingBody_IsSilent()
    {
        var session = await LootSessionAsync([new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)]);

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.LootOpen, 2, new { networkId = "Creature:1" })));

        Assert.Empty(response);
    }

    [Fact]
    public async Task PlaySession_LootFlow_OpensTakesRowTakesCoinAndEmpties()
    {
        var session = await LootSessionAsync([new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)]);
        await KillAsync(session, "Creature:1", 2);

        var opened = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.LootOpen, 11, new { networkId = "Creature:1" })));
        Assert.Equal(2, opened.Count);
        Assert.Contains("loot.contents", opened[0]);
        Assert.Contains("loot.busy", opened[1]);

        var taken = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.LootTake, 12, new { networkId = "Creature:1", slot = 0 })));
        Assert.Equal(2, taken.Count);
        Assert.Contains("loot.slotEmptied", taken[0]);
        Assert.Contains("inventory.itemAdded", taken[1]);

        var coin = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.LootTakeMoney, 13, new { networkId = "Creature:1" })));
        Assert.Equal(3, coin.Count);
        Assert.Contains("loot.slotEmptied", coin[0]);
        Assert.Contains("player.updated", coin[1]);
        Assert.Contains("5050", coin[1]);
        Assert.Contains("entity.lootable", coin[2]);

        var reopened = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.LootOpen, 14, new { networkId = "Creature:1" })));
        Assert.Contains("\"money\":0", reopened[0]);
    }

    [Fact]
    public async Task PlaySession_LootOpenOutOfRange_IsSilent()
    {
        var session = await LootSessionAsync([new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)]);
        await KillAsync(session, "Creature:1", 2);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 10, new PlayerMovePayload(16, 2, 3, 0, true, MovementState.Walking))));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.LootOpen, 11, new { networkId = "Creature:1" })));

        Assert.Empty(response);
    }

    [Fact]
    public async Task PlaySession_TickCombat_CreatureSwingsBackAtPlayer()
    {
        var session = await LootSessionAsync([new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)]);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var at = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        var swings = session.TickCombat(at);

        Assert.Contains("attack.swingResult", swings[0]);
        Assert.DoesNotContain(swings, m => m.Contains("entity.updated") || m.Contains("player.updated"));

        var flushed = session.TickCombat(at + TimeSpan.FromMilliseconds(700));
        var character = Character("char-a", 1, "Ayla");
        var stamina = PlayerStatCurves.AttributesAt(3, 4, StartingLoadouts.For(character.Race, character.Class)!.Attributes).Stamina;
        var expectedHealth = PlayerStatCurves.BaseMaxHealth(3, 4, CombatService.PlayerMaxHealth)
            + stamina * CombatService.HealthPerStamina
            - CombatService.RetaliationDamage;
        Assert.Contains(flushed, m => m.Contains("player.updated") && m.Contains($"\"health\":{expectedHealth}"));
        Assert.Contains(flushed, m => m.Contains("entity.updated")
            && m.Contains("player:char-a") && m.Contains($"\"health\":{expectedHealth}"));
    }

    [Fact]
    public async Task PlaySession_SustainedFights_KillThePlayerAndBlockAttacks()
    {
        var session = await LootSessionAsync(
        [
            new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9001, 2.5f, 2, 4, 0),
            new CreatureSpawnDefinition(1003, 13, 9001, 1.5f, 2, 4, 0),
        ], TightLeashContent, wanderRollUnit: () => 0.0, health: FightLengthHealth);
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        var log = new List<string>();
        foreach (var target in new[] { "Creature:1", "Creature:2", "Creature:3" })
        {
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AttackEngage, 2, new { targetId = target })));
            for (var swing = 0; swing < 10 && !log.Any(message => message.Contains("unit.died")
                && message.Contains("player:char-a")); swing++)
            {
                log.AddRange(session.TickCombat(now));
                now += TimeSpan.FromSeconds(2);
            }
        }

        Assert.Contains(log, message => message.Contains("unit.died") && message.Contains("player:char-a"));
        var response = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 3, new { targetId = "Creature:3" })));

        Assert.Contains("invalid_target", response.Single());
        Assert.Contains("dead", response.Single());
    }

    [Fact]
    public async Task PlaySession_PlayerSos_RevivesADeadPlayerWithNoCooldownAtAll()
    {
        var session = await LootSessionAsync(
        [
            new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0),
            new CreatureSpawnDefinition(1002, 13, 9001, 2.5f, 2, 4, 0),
            new CreatureSpawnDefinition(1003, 13, 9001, 1.5f, 2, 4, 0),
        ], TightLeashContent, wanderRollUnit: () => 0.0, health: FightLengthHealth);
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        var log = new List<string>();
        foreach (var target in new[] { "Creature:1", "Creature:2", "Creature:3" })
        {
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AttackEngage, 2, new { targetId = target })));
            for (var swing = 0; swing < 10 && !log.Any(message => message.Contains("unit.died")
                && message.Contains("player:char-a")); swing++)
            {
                log.AddRange(session.TickCombat(now));
                now += TimeSpan.FromSeconds(2);
            }
        }

        Assert.Contains(log, message => message.Contains("unit.died") && message.Contains("player:char-a"));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.PlayerUnstuck, 3, new { })));

        Assert.Contains(response, m => m.Contains("\"type\":\"world.loading\""));
        Assert.Contains(response, m => m.Contains("\"type\":\"world.entered\"")
            && m.Contains("\"pos\":{\"x\":-130,\"y\":5,\"z\":70}"));
        var revive = Assert.Single(response, m => m.Contains("\"type\":\"player.revived\""));
        Assert.Contains("player:char-a", revive);
        var character = Character("char-a", 1, "Ayla");
        var expectedMaxHealth = PlayerStatCurves.BaseMaxHealth(3, 4, CombatService.PlayerMaxHealth)
            + PlayerStatCurves.AttributesAt(3, 4, StartingLoadouts.For(character.Race, character.Class)!.Attributes).Stamina
                * CombatService.HealthPerStamina;
        Assert.Contains($"\"health\":{expectedMaxHealth}", revive);
        Assert.Contains($"\"maxHealth\":{expectedMaxHealth}", revive);
        Assert.Contains($"\"power\":{SpellService.PlayerMaxPower}", revive);

        var attack = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 4, new { targetId = "Creature:1" })));
        Assert.DoesNotContain(attack, m => m.Contains("You are dead"));
    }

    [Fact]
    public async Task PlaySession_PlayerSos_WhileAliveIsRateLimitedByAFifteenMinuteCooldown()
    {
        var session = await LootSessionAsync([new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)]);

        var first = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.PlayerUnstuck, 2, new { })));
        Assert.Contains(first, m => m.Contains("\"type\":\"world.entered\""));

        var second = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.PlayerUnstuck, 3, new { })));
        Assert.Contains("unstuck_refused", second.Single());
    }

    [Fact]
    public async Task PlaySession_PlayerSos_WhileAliveAndInCombatIsRefused()
    {
        var session = await LootSessionAsync([new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)]);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

        var response = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.PlayerUnstuck, 3, new { })));

        Assert.Contains("unstuck_refused", response.Single());
        Assert.Contains("combat", response.Single());
    }

    [Fact]
    public async Task PlaySession_TickCombat_RegeneratesOutOfCombatOnItsOwnClock()
    {
        var session = await LootSessionAsync([new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)]);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        for (var swing = 0; swing < 9; swing++)
        {
            session.TickCombat(now);
            now += TimeSpan.FromSeconds(2);
        }

        Assert.DoesNotContain(session.TickCombat(now + CombatService.SwingInterval),
            message => message.Contains("player.updated"));

        var heals = 0;
        for (var step = 0; step < 4; step++)
        {
            now += CombatService.RegenInterval;
            heals += session.TickCombat(now).Count(message => message.Contains("player.updated"));
        }

        Assert.True(heals >= 1, $"expected out-of-combat healing on the regeneration clock, saw {heals}");
    }

    [Fact]
    public async Task PlaySession_TickCombat_DespawnsAndRespawnsTheVictim()
    {
        var content = GameContent.Development with
        {
            Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
            {
                [9001] = new(9001, "creature-9001", 3, 100, NoticeDistance: 5),
            },
        };
        var session = await LootSessionAsync([new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        List<string> last = [];
        for (var swing = 0; swing < 9 && !last.Any(message => message.Contains("unit.died")); swing++)
        {
            last = session.TickCombat(now).ToList();
            now += TimeSpan.FromSeconds(2);
        }

        Assert.Contains(last, message => message.Contains("unit.died"));

        Assert.DoesNotContain(session.TickCombat(now + TimeSpan.FromSeconds(30)),
            message => message.Contains("entity.removed"));
        var removed = session.TickCombat(now + TimeSpan.FromSeconds(150));
        Assert.Contains(removed, message => message.Contains("entity.removed"));

        var respawned = session.TickCombat(now + TimeSpan.FromSeconds(181));
        Assert.Contains(respawned, message => message.Contains("entity.spawned"));
        Assert.Contains(respawned, message => message.Contains("Creature:2"));
        Assert.Contains(respawned, message => message.Contains("attack.meleeStart"));

        var reopened = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.LootOpen, 3, new { networkId = "Creature:1" })));
        Assert.Empty(reopened);

        var reattack = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 4, new { targetId = "Creature:2" })));
        Assert.Contains(reattack, message => message.Contains("attack.meleeStart")
            && message.Contains("\"targetId\":\"Creature:2\""));
    }

    private static Task<PlaySession> LootSessionAsync(
        IReadOnlyList<CreatureSpawnDefinition> spawns) =>
        LootSessionAsync(spawns, TestSessions.WithTestPurse(GameContent.Development));

    private static Task<PlaySession> LootSessionAsync(
        IReadOnlyList<CreatureSpawnDefinition> spawns, GameContent content) =>
        LootSessionAsync(spawns, content, wanderRollUnit: null);

    private const int FightLengthHealth = 52;

    private static async Task<PlaySession> LootSessionAsync(
        IReadOnlyList<CreatureSpawnDefinition> spawns, GameContent content, Func<double>? wanderRollUnit,
        int? health = null)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([health is int carried ? Character("char-a", 1, "Ayla") with { Health = carried } : Character("char-a", 1, "Ayla")]), spawns,
            content: content, combatRollUnit: () => 1.0, wanderRollUnit: wanderRollUnit, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static async Task KillAsync(PlaySession session, string targetId, int sequence)
    {
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, sequence, new { targetId })));
        var now = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        for (var swing = 0; swing < 20; swing++)
        {
            session.TickCombat(now);
            now += TimeSpan.FromSeconds(2);
        }
    }

    private static CharacterRecord Character(string guid, int accountId, string name) => new(
        guid, accountId, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, [100, 200]);

    private static readonly GameContent TightLeashContent = GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9001] = new(9001, "creature-9001", 3, 100, LeashRange: 2f),
        },
    };

    [Fact]
    public async Task PlaySession_WalkingIntoTheForestSideCityZone_ReturnsToTheMainCity()
    {
        var account = new DevelopmentAccountStore("developer", "dev-secret");
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla") with
        {
            MapId = 1, X = -900, Y = 60, Z = -290,
        }]);
        var session = TestSessions.Create(account, characters, developmentEntry: true);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var through = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 2, new PlayerMovePayload(-900, 60, -305, 0, true, MovementState.Walking))));

        Assert.DoesNotContain(through, m => m.Contains("entity.moved"));
        Assert.Contains(through, m => m.Contains("world.loading") && m.Contains("\"map\":13"));
        Assert.Contains(through, m => m.Contains("world.entered") && m.Contains("\"map\":13"));
        Assert.Contains(through, m => m.Contains("\"x\":-285") && m.Contains("\"z\":345"));
    }

    [Fact]
    public void MapGates_ArrivalPointsSitOutsideTheOppositeGatesOwnTrigger()
    {
        var gates = GameContent.Development.MapGates;
        var toForest = gates.Single(g => g.SourceMapId == 13);
        var toCity = gates.Single(g => g.SourceMapId == 1 && !g.IsDungeonEntrance);

        Assert.False(toCity.Contains(toForest.DestinationX, toForest.DestinationZ));
        Assert.False(toForest.Contains(toCity.DestinationX, toCity.DestinationZ));

        foreach (var mouth in gates.Where(g => g.IsDungeonEntrance))
        {
            var wayOut = gates.Single(g => g.IsDungeonExit && g.SourceMapId == mouth.DestinationMapId);
            Assert.False(wayOut.Contains(mouth.DestinationX, mouth.DestinationZ));
            Assert.False(mouth.Contains(wayOut.DestinationX, wayOut.DestinationZ));
        }
    }
}
