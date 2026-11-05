namespace Ubiquitous.Admin;

public sealed record AdminAccount(int Id, string UserId, bool IsGm);

public sealed record AdminCharacter(string Guid, string Name, string Race, string Class, int Level, int MapId);

public sealed record AdminBagItem(int Entry, string Name, int Count, int Slot);

public sealed record CharacterDetail(AdminCharacter Character, int Gold, int BagSlots, IReadOnlyList<AdminBagItem> Bag);

// What the admin page can change on a character; a null leaves that part as it is.
public sealed record CharacterEdit(string? Name = null, int? Level = null, int? Gold = null);

public sealed record AdminItem(int Id, string Name, int MaxStack);

public enum CharacterChange
{
    Done,
    NotFound,
    Online,
    BadName,
    NameTaken,
    BadLevel,
    BadGold,
    NoSuchItem,
    BadCount,
    BagFull,
}

public enum AccountChange
{
    Done,
    NotFound,
    Taken,
    BadUserId,
    WeakPassword,
}

public interface IAccountDirectory
{
    Task<IReadOnlyList<AdminAccount>> ListAsync(CancellationToken cancellationToken = default);

    Task<AdminAccount?> FindAsync(string userId, CancellationToken cancellationToken = default);

    // False for a wrong password, an unknown user, or one locked out after too many tries.
    Task<bool> PasswordMatchesAsync(string userId, string password, CancellationToken cancellationToken = default);

    Task<AccountChange> CreateAsync(string userId, string password, CancellationToken cancellationToken = default);

    Task<AccountChange> SetPasswordAsync(int accountId, string password, CancellationToken cancellationToken = default);

    Task<AccountChange> SetGmAsync(int accountId, bool isGm, CancellationToken cancellationToken = default);
}

public interface ICharacterDirectory
{
    Task<IReadOnlyList<AdminCharacter>> ListForAccountAsync(int accountId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string guid, CancellationToken cancellationToken = default);

    Task<CharacterDetail?> GetAsync(string guid, CancellationToken cancellationToken = default);

    Task<CharacterChange> EditAsync(string guid, CharacterEdit edit, CancellationToken cancellationToken = default);

    Task<CharacterChange> GiveItemAsync(string guid, int entry, int count, CancellationToken cancellationToken = default);
}

public interface IItemCatalog
{
    IReadOnlyList<AdminItem> Search(string text, int limit);
}

public interface IOnlinePlayers
{
    // Names of characters in the world now; null when the game server cannot be asked.
    Task<IReadOnlySet<string>?> NamesAsync(CancellationToken cancellationToken = default);
}

// The server's settings file: NAME=value pairs. A null value removes the setting, so its default applies.
public interface ISettingsStore
{
    IReadOnlyDictionary<string, string> Read();

    void Write(IReadOnlyDictionary<string, string?> changes);

    string Location { get; }
}

public enum ServerState
{
    Running,
    WaitingForData,
    Unreachable,
}

public sealed record ServerStatus(ServerState State, string? Reason = null);

public interface IServerStatus
{
    Task<ServerStatus> CheckAsync(CancellationToken cancellationToken = default);
}

public interface IServerControl
{
    // False when the server could not be reached or refused the request.
    Task<bool> RestartAsync(CancellationToken cancellationToken = default);
}
