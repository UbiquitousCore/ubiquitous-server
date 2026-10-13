using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class DevelopmentFixturesTests
{
    [Theory]
    [InlineData(1, PlayerClassIds.Warrior, PlayerClassIds.Mage)]
    [InlineData(2, PlayerClassIds.Ranger, PlayerClassIds.Monk)]
    public async Task EachDevelopmentAccount_HoldsTwoLevelSixtyCharacters(int accountId, int first, int second)
    {
        var characters = await DevelopmentFixtures.Characters().ListForAccountAsync(accountId);

        Assert.Equal([first, second], characters.Select(character => character.Class));
        Assert.All(characters, character => Assert.Equal(60, character.Level));
    }

    [Theory]
    [InlineData("Bastion", 6247, 6537)]
    [InlineData("Arlen", 6330, 6281)]
    [InlineData("Kestrel", 6348, 6583)]
    [InlineData("Liora", 6342, 6281)]
    public async Task EachGearedCharacter_WearsItsSetAndWeapon(string name, int helmet, int weapon)
    {
        var character = (await DevelopmentFixtures.Characters().FindByNameAsync(name))!;

        Assert.Equal(helmet, character.Equipment[EquipmentSlots.Head]);
        Assert.Equal(weapon, character.Equipment[EquipmentSlots.MainHand]);
        foreach (var slot in new[] { EquipmentSlots.Head, EquipmentSlots.Shoulder, EquipmentSlots.Chest,
            EquipmentSlots.Legs, EquipmentSlots.Feet, EquipmentSlots.Hands, EquipmentSlots.MainHand })
        {
            Assert.NotEqual(0, character.Equipment[slot]);
        }

        Assert.Equal(0, character.Equipment[EquipmentSlots.OffHand]);
    }

    [Theory]
    [InlineData("Bastion", PlayerClassIds.Warrior, 1)]
    [InlineData("Arlen", PlayerClassIds.Mage, 1)]
    [InlineData("Kestrel", PlayerClassIds.Ranger, 2)]
    [InlineData("Liora", PlayerClassIds.Monk, 2)]
    public async Task ALevelSixtyCharacter_KnowsEveryRankOfItsClassSpells(string name, int playerClass, int accountId)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret", accountId), DevelopmentFixtures.Characters());
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name))));

        var book = responses.Single(message => message.Contains("\"spell.book\""));
        Assert.NotEmpty(PlaytestSpellbook.AtMaxLevel(playerClass));
        Assert.All(PlaytestSpellbook.AtMaxLevel(playerClass), spellId => Assert.Contains($"{spellId}", book));
    }

    [Theory]
    [InlineData("Bastion", 1)]
    [InlineData("Arlen", 1)]
    [InlineData("Kestrel", 2)]
    [InlineData("Liora", 2)]
    public async Task AGearedCharacter_EntersTheWorldWearingItsSet(string name, int accountId)
    {
        var characters = DevelopmentFixtures.Characters();
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret", accountId), characters);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(name))));

        Assert.Contains(responses, message => message.Contains("world.entered"));
        var weapon = (await characters.FindByNameAsync(name))!.Equipment[EquipmentSlots.MainHand];
        Assert.Contains(responses, message => message.Contains("inventory.updated") && message.Contains($"\"entry\":{weapon}"));
    }
}
