using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

// The saved inventory decides what is worn; the character record's own list can lag behind it.
public sealed class WornGearOnLoginTests
{
    private const int Breastplate = 9701;
    private const int StrengthBonus = 7;

    private static readonly GameContent Content = GameContent.Development with
    {
        Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
        {
            [Breastplate] = new(Breastplate, "Breastplate", 4, 4, 0, InventoryType: 5,
                Stats: [new ItemAttributeBonus(AttributeIds.Strength, StrengthBonus)]),
        },
    };

    private static readonly CharacterRecord Character = new(
        "char-a", 1, "Ayla", 1, PlayerRaceIds.Human, PlayerClassIds.Warrior, 4, 5, 6, 7, 8, 13, 1, 2, 3,
        Level: 6, Equipment: []);

    private sealed class FixedInventoryStore(IReadOnlyList<PersistedInventoryItem> items) : IInventoryStore
    {
        public Task<IReadOnlyList<PersistedInventoryItem>> ListAsync(
            string characterGuid, CancellationToken cancellationToken = default) => Task.FromResult(items);

        public Task SaveAsync(string characterGuid, IReadOnlyList<PersistedInventoryItem> items,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task<IReadOnlyList<string>> EnterWearingAsync(IReadOnlyList<PersistedInventoryItem> inventory)
    {
        var session = TestSessions.Create(
            new InMemoryAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), content: Content,
            inventoryStore: new FixedInventoryStore(inventory));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        return await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
    }

    private static readonly PersistedInventoryItem WornBreastplate =
        new("worn-chest", Breastplate, 1, "equipment", EquipmentSlots.Chest);

    [Fact]
    public async Task GearTheRecordDoesNotList_IsShownOnTheCharacterAtLogin()
    {
        var entered = await EnterWearingAsync([WornBreastplate]);

        var look = Assert.Single(entered, message => message.Contains($"\"type\":\"{WebMessageTypes.EntityAppearanceChanged}\""));
        Assert.Contains("player:char-a", look);
        Assert.Contains($",{Breastplate},", look);
    }

    [Fact]
    public async Task GearTheRecordDoesNotList_CountsTowardTheCharactersStatsAtLogin()
    {
        var bare = await EnterWearingAsync([]);
        var dressed = await EnterWearingAsync([WornBreastplate]);

        Assert.Equal(StrengthOf(bare) + StrengthBonus, StrengthOf(dressed));
    }

    private static int StrengthOf(IReadOnlyList<string> entered)
    {
        var updated = Assert.Single(entered, message => message.Contains($"\"type\":\"{WebMessageTypes.PlayerValuesChanged}\""));
        using var document = System.Text.Json.JsonDocument.Parse(updated);
        return document.RootElement.GetProperty("payload").GetProperty("changed").GetProperty("strength").GetInt32();
    }
}
