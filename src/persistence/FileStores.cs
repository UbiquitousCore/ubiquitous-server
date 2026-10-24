using System.Text.Json;

namespace Ubiquitous.Persistence;

public sealed class FileAccountStore : IAccountAdmin
{
    private readonly string _path;
    private List<AccountRecord> _accounts;
    private DateTime _loadedAt;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // Own choice: ids below this are the development logins', so a real account never shares one with them.
    public const int FirstAccountId = 100;

    private readonly Action? _beforeFirstWrite;

    private FileAccountStore(string path, List<AccountRecord> accounts, Action? beforeFirstWrite = null)
    {
        _path = path;
        _accounts = accounts;
        _beforeFirstWrite = beforeFirstWrite;
        _loadedAt = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
    }

    public static FileAccountStore Load(string path) => new(path, Read(path));

    // Reading never creates the file: its existence is what tells the server real accounts are in use.
    public static FileAccountStore Open(string path, Action beforeFirstWrite) =>
        new(path, File.Exists(path) ? Read(path) : [], beforeFirstWrite);

    private static List<AccountRecord> Read(string path)
    {
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return [];
        var accounts = JsonSerializer.Deserialize<List<AccountRecord>>(text, JsonOptions)
            ?? throw new InvalidDataException("Account file must contain a JSON array.");
        if (accounts.GroupBy(account => account.UserId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidDataException("Account file contains duplicate user IDs.");
        return accounts;
    }

    // Accounts are added from the command line while the server runs.
    private void ReloadIfChanged()
    {
        if (!File.Exists(_path)) return;
        var modified = File.GetLastWriteTimeUtc(_path);
        if (modified == _loadedAt) return;
        var fresh = Read(_path);
        lock (_accounts)
        {
            _accounts = fresh;
            _loadedAt = modified;
        }
    }

    public Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReloadIfChanged();
        lock (_accounts) return Task.FromResult(_accounts.FirstOrDefault(account => account.UserId == userId));
    }

    public Task<IReadOnlyList<AccountRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        ReloadIfChanged();
        lock (_accounts) return Task.FromResult<IReadOnlyList<AccountRecord>>([.. _accounts.OrderBy(account => account.Id)]);
    }

    public Task<AccountRecord?> CreateAsync(string userId, string passHash, CancellationToken cancellationToken = default) =>
        MutateAsync(accounts =>
        {
            if (accounts.Any(account => account.UserId == userId)) return null;
            var created = new AccountRecord(Math.Max(FirstAccountId, accounts.Count == 0 ? 0 : accounts.Max(account => account.Id) + 1), userId, passHash);
            accounts.Add(created);
            return created;
        }, cancellationToken);

    public Task SetPassHashAsync(int accountId, string passHash, CancellationToken cancellationToken = default) =>
        Update(accountId, account => account with { PassHash = passHash }, cancellationToken);

    public Task SetAdminAsync(int accountId, bool isAdmin, CancellationToken cancellationToken = default) =>
        Update(accountId, account => account with { IsAdmin = isAdmin }, cancellationToken);

    private Task Update(int accountId, Func<AccountRecord, AccountRecord> change, CancellationToken cancellationToken) =>
        MutateAsync<object>(accounts =>
        {
            for (var index = 0; index < accounts.Count; index++)
            {
                if (accounts[index].Id == accountId) accounts[index] = change(accounts[index]);
            }

            return null;
        }, cancellationToken);

    private async Task<T?> MutateAsync<T>(Func<List<AccountRecord>, T?> change, CancellationToken cancellationToken)
        where T : class
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            ReloadIfChanged();
            List<AccountRecord> snapshot;
            T? result;
            lock (_accounts)
            {
                result = change(_accounts);
                snapshot = [.. _accounts];
            }

            if (!File.Exists(_path)) _beforeFirstWrite?.Invoke();
            await JsonFile.WriteAsync(_path, snapshot, cancellationToken);
            lock (_accounts) _loadedAt = File.GetLastWriteTimeUtc(_path);
            return result;
        }
        finally
        {
            _writeLock.Release();
        }
    }
}

public sealed class FileCharacterStore : ICharacterStore
{
    private readonly string _path;
    private readonly List<CharacterRecord> _characters;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private FileCharacterStore(string path, List<CharacterRecord> characters)
    {
        _path = path;
        _characters = characters;
    }

    public static FileCharacterStore Load(string path)
    {
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new FileCharacterStore(path, []);
        var characters = JsonSerializer.Deserialize<List<CharacterRecord>>(text, JsonFile.Options)
            ?? throw new InvalidDataException("Character file must contain a JSON array.");
        if (characters.Any(character => string.IsNullOrWhiteSpace(character.Guid)
            || string.IsNullOrWhiteSpace(character.Name)))
            throw new InvalidDataException("Character file contains an empty GUID or name.");
        if (characters.GroupBy(character => character.Guid, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidDataException("Character file contains duplicate GUIDs.");
        return new FileCharacterStore(path, characters);
    }

    public Task<CharacterRecord?> GetByGuidAsync(string guid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_characters.FirstOrDefault(character => character.Guid == guid));
    }

    public Task<IReadOnlyList<CharacterRecord>> ListForAccountAsync(int accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<CharacterRecord>>(
                [.. _characters.Where(character => character.AccountId == accountId)]);
        }
    }

    public Task<CharacterRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_characters.FirstOrDefault(character =>
                string.Equals(character.Name, name, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public Task UpdateAsync(CharacterRecord character, CancellationToken cancellationToken = default) =>
        ChangeAsync(characters =>
        {
            var index = characters.FindIndex(existing => existing.Guid == character.Guid);
            if (index < 0)
                throw new InvalidOperationException($"No character with GUID '{character.Guid}' exists to update.");
            characters[index] = character;
            return true;
        }, cancellationToken);

    public Task CreateAsync(CharacterRecord character, CancellationToken cancellationToken = default) =>
        ChangeAsync(characters =>
        {
            if (characters.Any(existing => existing.Guid == character.Guid))
                throw new InvalidOperationException($"A character with GUID '{character.Guid}' already exists.");
            characters.Add(character);
            return true;
        }, cancellationToken);

    public Task<bool> DeleteAsync(string guid, CancellationToken cancellationToken = default) =>
        ChangeAsync(characters => characters.RemoveAll(existing => existing.Guid == guid) > 0, cancellationToken);

    private async Task<bool> ChangeAsync(Func<List<CharacterRecord>, bool> change, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            CharacterRecord[] snapshot;
            lock (_gate)
            {
                if (!change(_characters)) return false;
                snapshot = [.. _characters];
            }

            await JsonFile.WriteAsync(_path, snapshot, cancellationToken);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
