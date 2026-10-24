using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class VendorLessonTests
{
    private const uint Lesson = 100;
    private const uint LessonSpell = 900_001;
    private const int Ranger = PlayerClassIds.Ranger;
    private const int Human = PlayerRaceIds.Human;

    // The vendor in the development content sells item 100; here it is a lesson teaching `taught`.
    private static GameContent Content(uint taught, uint requiredLevel, uint? classMask)
    {
        var template = GameContent.Development.Items.Values.First();
        var spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [LessonSpell] = new(LessonSpell, "lesson", 0, 0, 0f, EffectLearnSpellId: taught),
            [taught] = new(taught, "Taught", 0, 0, 0f),
        };
        return GameContent.Development with
        {
            Spells = spells,
            Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
            {
                [Lesson] = template with { Id = Lesson, OnUseSpellId = LessonSpell, RequiredLevel = requiredLevel, AllowableClassMask = classMask },
            },
        };
    }

    private static async Task<string> OpenVendorAsync(GameContent content, int level)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, Human, Ranger, 4, 5, 6, 7, 8, 13, 1, 2, 3, level, [100, 200])]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)],
            content: content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        var opened = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.VendorOpen, 2, new { networkId = "Creature:1" })));
        return opened.Single(message => message.Contains($"\"type\":\"{WebMessageTypes.VendorItems}\""));
    }

    private static uint SpellTheCharacterKnows() => StartingLoadouts.For(Human, Ranger)!.Spells.First();

    [Fact]
    public async Task ALessonForTheCharacter_CarriesNoMark()
    {
        var items = await OpenVendorAsync(Content(taught: 910_001, requiredLevel: 1, classMask: 0), level: 5);

        Assert.DoesNotContain("\"lesson\":\"", items);
    }

    [Fact]
    public async Task ALessonOfAHigherLevel_IsMarkedCannot()
    {
        var items = await OpenVendorAsync(Content(taught: 910_001, requiredLevel: 10, classMask: 0), level: 5);

        Assert.Contains($"\"lesson\":\"{WireCodes.Lesson.Cannot}\"", items);
    }

    [Fact]
    public async Task ALessonForAnotherClass_IsMarkedCannot()
    {
        var mageOnly = ItemClassMask.BitFor(PlayerClassIds.Mage);

        var items = await OpenVendorAsync(Content(taught: 910_001, requiredLevel: 1, classMask: mageOnly), level: 5);

        Assert.Contains($"\"lesson\":\"{WireCodes.Lesson.Cannot}\"", items);
    }

    [Fact]
    public async Task ALessonForASpellAlreadyKnown_IsMarkedLearned_EvenAboveTheLevel()
    {
        var items = await OpenVendorAsync(Content(taught: SpellTheCharacterKnows(), requiredLevel: 30, classMask: 0), level: 5);

        Assert.Contains($"\"lesson\":\"{WireCodes.Lesson.Learned}\"", items);
    }

    [Fact]
    public async Task AnItemThatTeachesNothing_CarriesNoMark()
    {
        var items = await OpenVendorAsync(GameContent.Development, level: 5);

        Assert.DoesNotContain("\"lesson\":\"", items);
    }
}
