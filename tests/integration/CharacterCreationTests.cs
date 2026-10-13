using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class CharacterCreationTests
{
    private const string User = "developer";
    private const string Secret = "dev-secret";

    [Fact]
    public async Task Creating_A_Character_Answers_And_Lists_It()
    {
        var session = await SignedIn(new InMemoryCharacterStore([]));

        var replies = await Create(session, "Ayla", PlayerRaceIds.Human, PlayerClassIds.Ranger);

        Assert.Equal(2, replies.Count);
        Assert.Contains("characters.created", replies[0]);
        Assert.Contains("\"ok\":true", replies[0]);
        Assert.Contains("characters.listed", replies[1]);
        Assert.Contains("\"name\":\"Ayla\"", replies[1]);
    }

    [Fact]
    public async Task A_Created_Character_Starts_Where_Its_Pairing_Says_Wearing_What_It_Says()
    {
        var store = new InMemoryCharacterStore([]);
        var session = await SignedIn(store);

        await Create(session, "Ayla", PlayerRaceIds.Human, PlayerClassIds.Ranger);

        var character = Assert.Single(await store.ListForAccountAsync(1));
        var loadout = StartingLoadouts.For(PlayerRaceIds.Human, PlayerClassIds.Ranger)!;
        Assert.Equal(loadout.MapId, character.MapId);
        Assert.Equal(loadout.X, character.X);
        Assert.Equal(loadout.Y, character.Y);
        Assert.Equal(loadout.Z, character.Z);
        Assert.Equal(CharacterCreation.StartingLevel, character.Level);
        Assert.Equal(loadout.WornEquipment(), character.Equipment);
        Assert.Null(character.Currency);
        Assert.Null(character.BagSlots);
    }

    public static TheoryData<int, int> EveryPairing()
    {
        var pairings = new TheoryData<int, int>();
        foreach (var race in PlayerRaceIds.All)
        {
            foreach (var playerClass in PlayerClassIds.All) pairings.Add(race, playerClass);
        }

        return pairings;
    }

    [Theory]
    [MemberData(nameof(EveryPairing))]
    public async Task A_New_Character_Of_Every_Pairing_Wakes_Up_In_Its_Races_Starting_Area(int race, int playerClass)
    {
        var store = new InMemoryCharacterStore([]);
        var session = await SignedIn(store);

        await Create(session, "Ayla", race, playerClass);

        var character = Assert.Single(await store.ListForAccountAsync(1));
        var (mapId, x, z) = race switch
        {
            PlayerRaceIds.Human or PlayerRaceIds.Elf => (1, 2f, 55f),
            PlayerRaceIds.Orc or PlayerRaceIds.Undead => (7, -2640f, 100f),
            _ => (6, -860f, 20f),
        };
        Assert.Equal(mapId, character.MapId);
        Assert.InRange(MathF.Sqrt(MathF.Pow(character.X - x, 2) + MathF.Pow(character.Z - z, 2)), 0f, 5f);
    }

    [Fact]
    public async Task A_Created_Character_Wears_Its_Own_Races_Body()
    {
        var store = new InMemoryCharacterStore([]);
        var session = await SignedIn(store);

        await Create(session, "Yrsa", PlayerRaceIds.Undead, PlayerClassIds.Mage, gender: PlayerBodies.Female);

        var character = Assert.Single(await store.ListForAccountAsync(1));
        Assert.Equal(PlayerBodies.DisplayInfoFor(PlayerRaceIds.Undead, PlayerBodies.Female), character.DisplayInfoId);
        Assert.Equal(PlayerRaceIds.Undead, character.Race);
        Assert.Equal(PlayerClassIds.Mage, character.Class);
        Assert.Equal(PlayerBodies.Female, character.Gender);
    }

    [Fact]
    public async Task A_Created_Characters_Carried_Items_Are_Waiting_For_Its_First_Session()
    {
        var store = new InMemoryCharacterStore([]);
        var inventory = new RecordingInventoryStore();
        var session = await SignedIn(store, inventory);

        await Create(session, "Ayla", PlayerRaceIds.Human, PlayerClassIds.Ranger);

        var character = Assert.Single(await store.ListForAccountAsync(1));
        var saved = await inventory.ListAsync(character.Guid);
        var loadout = StartingLoadouts.For(PlayerRaceIds.Human, PlayerClassIds.Ranger)!;
        Assert.Equal(loadout.BagItems.Count, saved.Count);
        Assert.All(saved, row => Assert.Equal("bag", row.Area));
        Assert.Equal(
            loadout.BagItems.Select(item => ((int)item.ItemEntry, item.Count, item.BagIndex)),
            saved.Select(row => (row.Entry, row.Count, row.Slot)));
    }

    [Theory]
    [InlineData("", CharacterCreateCode.NameNotAllowed)]
    [InlineData("A", CharacterCreateCode.NameNotAllowed)]
    [InlineData("Ayla the Second", CharacterCreateCode.NameNotAllowed)]
    [InlineData("Ayla99", CharacterCreateCode.NameNotAllowed)]
    [InlineData("Averyverylongcharactername", CharacterCreateCode.NameNotAllowed)]
    public async Task A_Name_The_Rules_Refuse_Is_Answered_Not_Errored(string name, CharacterCreateCode expected)
    {
        var store = new InMemoryCharacterStore([]);
        var session = await SignedIn(store);

        var replies = await Create(session, name, PlayerRaceIds.Human, PlayerClassIds.Warrior);

        var refusal = Assert.Single(replies);
        Assert.Contains("characters.created", refusal);
        Assert.Contains("\"ok\":false", refusal);
        Assert.Contains($"\"code\":{(int)expected}", refusal);
        Assert.Empty(await store.ListForAccountAsync(1));
    }

    [Fact]
    public async Task A_Name_Somebody_Else_Holds_Is_Refused_However_It_Is_Spelled()
    {
        var store = new InMemoryCharacterStore([Character("char-a", 2, "Borin")]);
        var session = await SignedIn(store);

        var replies = await Create(session, "borin", PlayerRaceIds.Orc, PlayerClassIds.Warrior);

        var refusal = Assert.Single(replies);
        Assert.Contains($"\"code\":{(int)CharacterCreateCode.NameTaken}", refusal);
        Assert.Empty(await store.ListForAccountAsync(1));
    }

    [Fact]
    public async Task A_Race_And_Class_That_Cannot_Be_Played_Together_Is_Refused()
    {
        var session = await SignedIn(new InMemoryCharacterStore([]));

        var replies = await Create(session, "Nobody", race: 42, playerClass: PlayerClassIds.Warrior);

        Assert.Contains($"\"code\":{(int)CharacterCreateCode.PairingNotPlayable}", Assert.Single(replies));
    }

    [Fact]
    public async Task The_Races_A_Server_Opened_With_Are_Listed_With_The_Account_S_Characters()
    {
        var session = await SignedIn(
            new InMemoryCharacterStore([]),
            content: GameContent.Development with
            {
                PlayableRaces = new HashSet<int>
                {
                    PlayerRaceIds.Human, PlayerRaceIds.Elf, PlayerRaceIds.Orc, PlayerRaceIds.Undead,
                },
            });

        var listed = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.CharactersList, 9, new object()))));

        Assert.Contains("\"races\":[0,1,2,3]", listed);
    }

    [Fact]
    public async Task A_Race_The_Server_Did_Not_Open_With_Is_Refused()
    {
        var store = new InMemoryCharacterStore([]);
        var session = await SignedIn(
            store,
            content: GameContent.Development with
            {
                PlayableRaces = new HashSet<int>
                {
                    PlayerRaceIds.Human, PlayerRaceIds.Elf, PlayerRaceIds.Orc, PlayerRaceIds.Undead,
                },
            });

        var replies = await Create(session, "Rootling", PlayerRaceIds.Mendel, PlayerClassIds.Warrior);

        Assert.Contains($"\"code\":{(int)CharacterCreateCode.PairingNotPlayable}", Assert.Single(replies));
        Assert.Empty(await store.ListForAccountAsync(1));
    }

    [Fact]
    public async Task A_Race_The_Server_Did_Open_With_Is_Made()
    {
        var session = await SignedIn(new InMemoryCharacterStore([]));

        var replies = await Create(session, "Rootling", PlayerRaceIds.Mendel, PlayerClassIds.Warrior);

        Assert.Contains("\"ok\":true", replies[0]);
    }

    [Fact]
    public async Task A_Sex_No_Body_Exists_For_Is_Refused()
    {
        var session = await SignedIn(new InMemoryCharacterStore([]));

        var replies = await Create(session, "Nobody", PlayerRaceIds.Elf, PlayerClassIds.Mage, gender: 7);

        Assert.Contains($"\"code\":{(int)CharacterCreateCode.LookNotAllowed}", Assert.Single(replies));
    }

    [Fact]
    public async Task An_Account_Is_Refused_A_Fifth_Character()
    {
        var store = new InMemoryCharacterStore(
        [
            Character("char-1", 1, "Ayla"),
            Character("char-2", 1, "Borin"),
            Character("char-3", 1, "Cass"),
            Character("char-4", 1, "Dain"),
        ]);
        var session = await SignedIn(store);

        var replies = await Create(session, "Eris", PlayerRaceIds.Human, PlayerClassIds.Monk);

        Assert.Contains($"\"code\":{(int)CharacterCreateCode.NoRoom}", Assert.Single(replies));
        Assert.Equal(4, (await store.ListForAccountAsync(1)).Count);
    }

    [Fact]
    public async Task A_Character_Deleted_Is_Gone_From_The_Account_And_Its_Bag_With_It()
    {
        var store = new InMemoryCharacterStore([]);
        var inventory = new RecordingInventoryStore();
        var session = await SignedIn(store, inventory);
        await Create(session, "Ayla", PlayerRaceIds.Human, PlayerClassIds.Ranger);
        var made = Assert.Single(await store.ListForAccountAsync(1));

        var replies = await Delete(session, "Ayla");

        Assert.Equal(2, replies.Count);
        Assert.Contains("characters.deleted", replies[0]);
        Assert.Contains("\"ok\":true", replies[0]);
        Assert.Contains("characters.listed", replies[1]);
        Assert.Empty(await store.ListForAccountAsync(1));
        Assert.Empty(await inventory.ListAsync(made.Guid));
    }

    [Fact]
    public async Task A_Character_On_Another_Account_Cannot_Be_Deleted()
    {
        var store = new InMemoryCharacterStore([Character("char-a", 2, "Borin")]);
        var session = await SignedIn(store);

        var replies = await Delete(session, "Borin");

        Assert.Contains("\"ok\":false", Assert.Single(replies));
        Assert.Single(await store.ListForAccountAsync(2));
    }

    [Fact]
    public async Task The_Name_A_Deleted_Character_Held_Can_Be_Used_Again()
    {
        var store = new InMemoryCharacterStore([]);
        var session = await SignedIn(store);
        await Create(session, "Ayla", PlayerRaceIds.Human, PlayerClassIds.Ranger);
        await Delete(session, "Ayla");

        var replies = await Create(session, "Ayla", PlayerRaceIds.Elf, PlayerClassIds.Mage);

        Assert.Contains("\"ok\":true", replies[0]);
        var again = Assert.Single(await store.ListForAccountAsync(1));
        Assert.Equal(PlayerRaceIds.Elf, again.Race);
    }

    [Fact]
    public async Task A_Pairing_Says_What_It_Would_Start_Out_Wearing()
    {
        var session = await SignedIn(new InMemoryCharacterStore([]));

        var replies = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersOutfit, 3, new OutfitRequest(PlayerRaceIds.Human, PlayerClassIds.Mage))));

        var answer = Assert.Single(replies);
        Assert.Contains("characters.outfitted", answer);
        var worn = StartingLoadouts.For(PlayerRaceIds.Human, PlayerClassIds.Mage)!.WornEquipment();
        Assert.Contains($"\"equipment\":[{string.Join(",", worn)}]", answer);
    }

    [Fact]
    public async Task A_Pairing_That_Cannot_Be_Played_Is_Wearing_Nothing_Rather_Than_Erroring()
    {
        var session = await SignedIn(new InMemoryCharacterStore([]));

        var replies = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersOutfit, 3, new OutfitRequest(42, PlayerClassIds.Mage))));

        Assert.Contains("\"equipment\":[]", Assert.Single(replies));
    }

    [Fact]
    public async Task A_Sign_Of_Life_Is_Answered_Without_Signing_In()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(User, Secret), new InMemoryCharacterStore([]));

        var replies = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.SessionPing, 0)));

        Assert.Contains("session.pong", Assert.Single(replies));
    }

    [Fact]
    public async Task Making_A_Character_Before_Signing_In_Is_Refused()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(User, Secret), new InMemoryCharacterStore([]));

        var replies = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersCreate,
            0,
            new CreateCharacterRequest("Ayla", PlayerRaceIds.Human, PlayerClassIds.Ranger, PlayerBodies.Female,
                new Appearance(0, 0, 0, 0)))));

        Assert.Contains("not_authenticated", Assert.Single(replies));
    }

    [Fact]
    public async Task A_Character_Made_Now_Can_Be_Picked_And_Walked_Into_The_World()
    {
        var store = new InMemoryCharacterStore([]);
        var session = await SignedIn(store);

        await Create(session, "Ayla", PlayerRaceIds.Human, PlayerClassIds.Ranger);
        var entered = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 9, new ChooseCharacterRequest("Ayla"))));

        Assert.Contains("world.loading", entered[0]);
        Assert.Contains("world.entered", entered[1]);
    }

    [Fact]
    public async Task A_Name_Taken_By_A_Simultaneous_Creation_Is_Refused_As_Taken()
    {
        var session = await SignedIn(new NameRaceLosingStore());

        var replies = await Create(session, "Ayla", PlayerRaceIds.Human, PlayerClassIds.Ranger);

        var reply = Assert.Single(replies);
        Assert.Contains("characters.created", reply);
        Assert.Contains("\"ok\":false", reply);
        Assert.Contains($"\"code\":{(int)CharacterCreateCode.NameTaken}", reply);
    }

    private sealed class NameRaceLosingStore : ICharacterStore
    {
        private readonly InMemoryCharacterStore _inner = new([]);

        public Task<CharacterRecord?> GetByGuidAsync(string guid, CancellationToken cancellationToken = default) =>
            _inner.GetByGuidAsync(guid, cancellationToken);

        public Task<IReadOnlyList<CharacterRecord>> ListForAccountAsync(int accountId, CancellationToken cancellationToken = default) =>
            _inner.ListForAccountAsync(accountId, cancellationToken);

        public Task<CharacterRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult<CharacterRecord?>(null);

        public Task UpdateAsync(CharacterRecord character, CancellationToken cancellationToken = default) =>
            _inner.UpdateAsync(character, cancellationToken);

        public Task CreateAsync(CharacterRecord character, CancellationToken cancellationToken = default) =>
            Task.FromException(new CharacterNameTakenException(character.Name));

        public Task<bool> DeleteAsync(string guid, CancellationToken cancellationToken = default) =>
            _inner.DeleteAsync(guid, cancellationToken);
    }

    private static async Task<PlaySession> SignedIn(
        ICharacterStore characters, IInventoryStore? inventory = null, GameContent? content = null)
    {
        var accounts = new DevelopmentAccountStore(User, Secret);
        var session = TestSessions.Create(accounts, characters, content: content, inventoryStore: inventory);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest(User, Secret))));
        return session;
    }

    private static Task<IReadOnlyList<string>> Create(
        PlaySession session,
        string name,
        int race,
        int playerClass,
        int gender = PlayerBodies.Male) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersCreate,
            1,
            new CreateCharacterRequest(name, race, playerClass, gender, new Appearance(1, 2, 3, 4)))));

    private static Task<IReadOnlyList<string>> Delete(PlaySession session, string name) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersDelete, 5, new DeleteCharacterRequest(name))));

    private static CharacterRecord Character(string guid, int accountId, string name) => new(
        guid, accountId, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, []);

    private sealed class RecordingInventoryStore : IInventoryStore
    {
        private readonly Dictionary<string, IReadOnlyList<PersistedInventoryItem>> _byCharacter = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<PersistedInventoryItem>> ListAsync(
            string characterGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byCharacter.TryGetValue(characterGuid, out var items) ? items : []);

        public Task SaveAsync(
            string characterGuid,
            IReadOnlyList<PersistedInventoryItem> items,
            CancellationToken cancellationToken = default)
        {
            _byCharacter[characterGuid] = items;
            return Task.CompletedTask;
        }
    }
}
