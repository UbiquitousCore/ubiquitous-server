namespace Ubiquitous.Persistence;

public sealed record AccountRecord(int Id, string UserId, string PassHash, bool IsAdmin = false);

public enum LoginCode
{
    InvalidCredentials = 1,
    Success = 2,
}

public sealed record LoginResult(bool Ok, LoginCode Code, string? Reason = null);

public sealed record CharacterRecord(
    string Guid,
    int AccountId,
    string Name,
    int Gender,
    int Race,
    int Class,
    int DisplayInfoId,
    int Skin,
    int HairStyle,
    int HairColour,
    int Face,
    ushort MapId,
    float X,
    float Y,
    float Z,
    int Level,
    IReadOnlyList<int> Equipment,
    int Xp = 0,
    int? Currency = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("bagCapacity")] int? BagSlots = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("bankCapacity")] int? BankSlots = null,
    int? Health = null,
    IReadOnlyDictionary<uint, int>? TalentRanks = null,
    IReadOnlyList<uint>? LearnedSpells = null,
    IReadOnlyList<PersistedActionButton>? ActionBar = null,
    int? Exhaustion = null,
    int? ExhaustionDay = null,
    bool? LegendDungeons = null,
    // The game version this character was made on; only that build plays it.
    string? GameVersion = null);

public sealed record PersistedActionButton(int Slot, uint Id, int Type);

public interface IProgressStore
{
    Task SaveAsync(
        CharacterRecord character,
        IReadOnlyList<Ubiquitous.Common.PersistedInventoryItem> inventory,
        Ubiquitous.Common.PersistedQuestLog questLog,
        CancellationToken cancellationToken = default);
}

public interface ICharacterStore
{
    Task<CharacterRecord?> GetByGuidAsync(string guid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CharacterRecord>> ListForAccountAsync(int accountId, CancellationToken cancellationToken = default);

    Task<CharacterRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    Task UpdateAsync(CharacterRecord character, CancellationToken cancellationToken = default);

    Task CreateAsync(CharacterRecord character, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string guid, CancellationToken cancellationToken = default);
}

public interface IAccountStore
{
    Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    Task SetPassHashAsync(int accountId, string passHash, CancellationToken cancellationToken = default);
}

public interface IAccountAuthenticator
{
    Task<LoginResult> AuthenticateAsync(string userId, string passHash, CancellationToken cancellationToken = default);
}

public sealed class InMemoryCharacterStore : ICharacterStore
{
    private readonly Dictionary<string, CharacterRecord> _byGuid = new(StringComparer.Ordinal);

    public InMemoryCharacterStore(IEnumerable<CharacterRecord> characters)
    {
        foreach (var character in characters) _byGuid[character.Guid] = character;
    }

    public Task<CharacterRecord?> GetByGuidAsync(string guid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_byGuid.TryGetValue(guid, out var character) ? character : null);
    }

    public Task<IReadOnlyList<CharacterRecord>> ListForAccountAsync(int accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<CharacterRecord> result = _byGuid.Values
            .Where(character => character.AccountId == accountId)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<CharacterRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_byGuid.Values.FirstOrDefault(character =>
            string.Equals(character.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    public Task UpdateAsync(CharacterRecord character, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _byGuid[character.Guid] = character;
        return Task.CompletedTask;
    }

    public Task CreateAsync(CharacterRecord character, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_byGuid.TryAdd(character.Guid, character))
            throw new InvalidOperationException($"A character with GUID '{character.Guid}' already exists.");
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string guid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_byGuid.Remove(guid));
    }
}

public interface IAccountAdmin : IAccountStore
{
    // Null when the user id is already taken.
    Task<AccountRecord?> CreateAsync(string userId, string passHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountRecord>> ListAsync(CancellationToken cancellationToken = default);

    Task SetAdminAsync(int accountId, bool isAdmin, CancellationToken cancellationToken = default);
}
