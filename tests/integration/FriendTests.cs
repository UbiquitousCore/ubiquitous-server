using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class FriendTests
{
    [Fact]
    public async Task AddingAFriend_PersistsAndSurvivesARestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-friends-{Guid.NewGuid():N}");
        try
        {
            var friendStore = new FileFriendStore(directory);
            var characters = new InMemoryCharacterStore(
            [
                Character("char-a", 1, "Ayla"),
                Character("char-b", 2, "Borin"),
            ]);

            var ayla = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, friendStore: friendStore);
            await LoginAsync(ayla, "developer", "Ayla");

            var added = await ayla.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.SocialFriendAdd, 2, new SocialTargetPayload("player:char-b"))));
            var reply = Assert.Single(added);
            Assert.Contains("\"type\":\"social.listUpdated\"", reply);
            Assert.Contains("\"name\":\"Borin\"", reply);
            Assert.Contains("\"networkId\":\"player:char-b\"", reply);
            Assert.Contains("\"ignored\":[]", reply);

            var aylaAgain = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, friendStore: friendStore);
            await LoginAsync(aylaAgain, "developer", "Ayla");

            var list = await aylaAgain.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.SocialFriendList, 2, new { })));
            var listReply = Assert.Single(list);
            Assert.Contains("\"name\":\"Borin\"", listReply);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task RemovingAFriend_Persists()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-friends-remove-{Guid.NewGuid():N}");
        try
        {
            var friendStore = new FileFriendStore(directory);
            var characters = new InMemoryCharacterStore(
            [
                Character("char-a", 1, "Ayla"),
                Character("char-b", 2, "Borin"),
            ]);

            var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, friendStore: friendStore);
            await LoginAsync(session, "developer", "Ayla");
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.SocialFriendAdd, 2, new SocialTargetPayload("player:char-b"))));

            var removed = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.SocialFriendRemove, 3, new SocialTargetPayload("player:char-b"))));
            Assert.Contains(removed, r => r.Contains("\"friends\":[]"));

            var restarted = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, friendStore: friendStore);
            await LoginAsync(restarted, "developer", "Ayla");
            var list = await restarted.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.SocialFriendList, 2, new { })));
            Assert.Contains(list, r => r.Contains("\"friends\":[]"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Ignoring_DoesNotAffectTheFriendsListAndViceVersa()
    {
        var characters = new InMemoryCharacterStore(
        [
            Character("char-a", 1, "Ayla"),
            Character("char-b", 2, "Borin"),
            Character("char-c", 3, "Cara"),
        ]);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters);
        await LoginAsync(session, "developer", "Ayla");

        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialFriendAdd, 2, new SocialTargetPayload("player:char-b"))));
        var ignored = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialIgnoreAdd, 3, new SocialTargetPayload("player:char-c"))));

        var reply = Assert.Single(ignored);
        Assert.Contains("\"name\":\"Borin\"", reply);
        Assert.Contains("\"name\":\"Cara\"", reply);
    }

    [Fact]
    public async Task AddingYourselfAsAFriend_IsRefused()
    {
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters);
        await LoginAsync(session, "developer", "Ayla");

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialFriendAdd, 2, new SocialTargetPayload("player:char-a"))));

        Assert.Contains(responses, r => r.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task IgnoringYourself_IsRefused()
    {
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters);
        await LoginAsync(session, "developer", "Ayla");

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialIgnoreAdd, 2, new SocialTargetPayload("player:char-a"))));

        Assert.Contains(responses, r => r.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task AFriend_CanBeAddedByTypedName_OnlineOrNot()
    {
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla"), Character("char-b", 2, "Borin")]);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters);
        await LoginAsync(session, "developer", "Ayla");

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialFriendAdd, 2, new SocialTargetPayload(Name: "borin"))));

        Assert.Contains(responses, r => r.Contains("\"type\":\"social.listUpdated\"") && r.Contains("\"name\":\"Borin\""));
        var unknown = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialFriendAdd, 3, new SocialTargetPayload(Name: "Nobody"))));
        Assert.Contains(unknown, r => r.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task Who_ListsEveryoneInTheWorld_WithTheirLevels()
    {
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var hub = new PlaySessionHub();
        var ayla = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters);
        var borin = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-b", 1, "Borin") with { Level = 17 }]));
        hub.Register(ayla, _ => Task.CompletedTask);
        hub.Register(borin, _ => Task.CompletedTask);
        await LoginAsync(ayla, "developer", "Ayla");
        await LoginAsync(borin, "developer", "Borin");

        var reply = Assert.Single(await ayla.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialWho, 2, new { }))));

        using var json = System.Text.Json.JsonDocument.Parse(reply);
        var players = json.RootElement.GetProperty("payload").GetProperty("players").EnumerateArray()
            .Select(p => (p.GetProperty("name").GetString(), p.GetProperty("level").GetInt32())).ToList();
        Assert.Equal([("Ayla", 4), ("Borin", 17)], players);
    }

    [Fact]
    public async Task AddingAnUnknownTarget_IsRefused()
    {
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters);
        await LoginAsync(session, "developer", "Ayla");

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialFriendAdd, 2, new SocialTargetPayload("player:char-nobody"))));

        Assert.Contains(responses, r => r.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task AddingTheSameFriendTwice_IsAHarmlessNoOpNotAnError()
    {
        var characters = new InMemoryCharacterStore(
        [
            Character("char-a", 1, "Ayla"),
            Character("char-b", 2, "Borin"),
        ]);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters);
        await LoginAsync(session, "developer", "Ayla");
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialFriendAdd, 2, new SocialTargetPayload("player:char-b"))));

        var again = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialFriendAdd, 3, new SocialTargetPayload("player:char-b"))));

        var reply = Assert.Single(again);
        Assert.Contains("\"type\":\"social.listUpdated\"", reply);
        Assert.Contains("\"name\":\"Borin\"", reply);
    }

    [Fact]
    public async Task AddingAFriend_NeverNotifiesOrChangesTheTargetOwnList()
    {
        var characters = new InMemoryCharacterStore(
        [
            Character("char-a", 1, "Ayla"),
            Character("char-b", 2, "Borin"),
        ]);
        var ayla = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters);
        await LoginAsync(ayla, "developer", "Ayla");
        await ayla.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialFriendAdd, 2, new SocialTargetPayload("player:char-b"))));

        var borin = TestSessions.Create(new DevelopmentAccountStore("developer2", "dev-secret", 2), characters);
        await LoginAsync(borin, "developer2", "Borin");
        var borinList = await borin.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialFriendList, 2, new { })));

        Assert.Contains(borinList, r => r.Contains("\"friends\":[]"));
    }

    [Fact]
    public async Task AFriendInTheWorld_IsListedWithWhoTheyAreAndWhere_AndAnOfflineOneByNameAlone()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-friends-online-{Guid.NewGuid():N}");
        try
        {
            var friendStore = new FileFriendStore(directory);
            var characters = new InMemoryCharacterStore(
            [
                Character("char-a", 1, "Ayla"),
                Character("char-b", 2, "Borin"),
                Character("char-c", 3, "Myrtle"),
            ]);
            var hub = new PlaySessionHub();
            var ayla = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, friendStore: friendStore);
            var borin = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                new InMemoryCharacterStore([Character("char-b", 1, "Borin")]), friendStore: friendStore);
            hub.Register(ayla, _ => Task.CompletedTask);
            hub.Register(borin, _ => Task.CompletedTask);
            await LoginAsync(ayla, "developer", "Ayla");
            await LoginAsync(borin, "developer", "Borin");

            await ayla.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.SocialFriendAdd, 2, new SocialTargetPayload("player:char-b"))));
            var list = await ayla.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.SocialFriendAdd, 3, new SocialTargetPayload("player:char-c"))));
            var reply = Assert.Single(list);

            using var json = System.Text.Json.JsonDocument.Parse(reply);
            var friends = json.RootElement.GetProperty("payload").GetProperty("friends").EnumerateArray().ToList();
            var online = friends.Single(f => f.GetProperty("name").GetString() == "Borin");
            Assert.True(online.GetProperty("online").GetBoolean());
            Assert.True(online.GetProperty("level").GetInt32() > 0);
            Assert.True(online.TryGetProperty("mapId", out _));
            Assert.True(online.TryGetProperty("position", out _));
            var offline = friends.Single(f => f.GetProperty("name").GetString() == "Myrtle");
            Assert.False(offline.GetProperty("online").GetBoolean());
            Assert.False(offline.TryGetProperty("level", out var level) && level.ValueKind != System.Text.Json.JsonValueKind.Null);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task LoginAsync(PlaySession session, string userId, string characterName)
    {
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest(userId, "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(characterName))));
    }

    private static CharacterRecord Character(string guid, int accountId, string name) => new(
        guid, accountId, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, [100, 200]);
}
