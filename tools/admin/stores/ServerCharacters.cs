using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;

namespace Ubiquitous.Admin.Stores;

// Changes a signed-out character's saves by the game's own rules: the names
// character creation accepts, its level range, the purse's limit, and the
// bag's stacking and room.
public sealed class ServerCharacters(ICharacterStore store, IInventoryStore inventory, PackageItems items) : ICharacterDirectory
{
    // Own choice: the same cap a GM's in-game gift has.
    public const int MaxGivenItems = 1000;

    public async Task<IReadOnlyList<AdminCharacter>> ListForAccountAsync(int accountId, CancellationToken cancellationToken = default) =>
        [.. (await store.ListForAccountAsync(accountId, cancellationToken))
            .OrderBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .Select(Summary)];

    public Task<bool> DeleteAsync(string guid, CancellationToken cancellationToken = default) =>
        store.DeleteAsync(guid, cancellationToken);

    public async Task<CharacterDetail?> GetAsync(string guid, CancellationToken cancellationToken = default)
    {
        if (await store.GetByGuidAsync(guid, cancellationToken) is not { } character) return null;
        var bag = (await inventory.ListAsync(guid, cancellationToken))
            .Where(item => item.Area == "bag")
            .OrderBy(item => item.Slot)
            .Select(item => new AdminBagItem(item.Entry, items.NameOf(item.Entry), item.Count, item.Slot))
            .ToArray();
        return new CharacterDetail(Summary(character), (character.Currency ?? 0) / CoinWords.Gold, BagSlotsOf(character), bag);
    }

    public async Task<CharacterChange> EditAsync(string guid, CharacterEdit edit, CancellationToken cancellationToken = default)
    {
        if (await store.GetByGuidAsync(guid, cancellationToken) is not { } character) return CharacterChange.NotFound;
        var changed = character;

        if (edit.Name is { } wanted)
        {
            var name = CharacterCreation.Tidy(wanted);
            if (!CharacterCreation.IsNameAllowed(name)) return CharacterChange.BadName;
            if (!string.Equals(name, character.Name, StringComparison.OrdinalIgnoreCase)
                && await store.FindByNameAsync(name, cancellationToken) is not null)
                return CharacterChange.NameTaken;
            changed = changed with { Name = name };
        }

        if (edit.Level is { } level)
        {
            if (level < 1 || level > LevelingService.MaxLevel) return CharacterChange.BadLevel;
            // Full health and an empty experience bar, as a GM's in-game level change leaves them.
            if (level != character.Level) changed = changed with { Level = level, Xp = 0, Health = null };
        }

        if (edit.Gold is { } gold)
        {
            var copper = (long)gold * CoinWords.Gold + (character.Currency ?? 0) % CoinWords.Gold;
            if (gold < 0 || copper > int.MaxValue) return CharacterChange.BadGold;
            changed = changed with { Currency = (int)copper };
        }

        try
        {
            if (changed != character) await store.UpdateAsync(changed, cancellationToken);
        }
        catch (CharacterNameTakenException)
        {
            return CharacterChange.NameTaken;
        }

        return CharacterChange.Done;
    }

    public async Task<CharacterChange> GiveItemAsync(string guid, int entry, int count, CancellationToken cancellationToken = default)
    {
        if (await store.GetByGuidAsync(guid, cancellationToken) is not { } character) return CharacterChange.NotFound;
        if (items.Find(entry) is not { } item) return CharacterChange.NoSuchItem;
        if (count < 1 || count > MaxGivenItems) return CharacterChange.BadCount;

        var belongings = new InventoryService();
        belongings.Load(await inventory.ListAsync(guid, cancellationToken));
        if (belongings.Add(entry, count, BagSlotsOf(character), item.MaxStack) is null) return CharacterChange.BagFull;
        await inventory.SaveAsync(guid, belongings.ToPersisted(), cancellationToken);
        return CharacterChange.Done;
    }

    private static int BagSlotsOf(CharacterRecord character) => character.BagSlots ?? InventoryService.DefaultBagCapacity;

    private static AdminCharacter Summary(CharacterRecord character) =>
        new(character.Guid, character.Name, RaceName(character.Race), ClassName(character.Class), character.Level, character.MapId);

    public static string RaceName(int race) => race switch
    {
        PlayerRaceIds.Human => "Human",
        PlayerRaceIds.Elf => "Elf",
        PlayerRaceIds.Orc => "Orc",
        PlayerRaceIds.Undead => "Undead",
        PlayerRaceIds.Mendel => "Mendel",
        _ => $"race {race}",
    };

    public static string ClassName(int playerClass) => playerClass switch
    {
        PlayerClassIds.Warrior => "Warrior",
        PlayerClassIds.Mage => "Mage",
        PlayerClassIds.Ranger => "Ranger",
        PlayerClassIds.Monk => "Monk",
        _ => $"class {playerClass}",
    };
}
