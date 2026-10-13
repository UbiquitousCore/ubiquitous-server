using System.Text.Json;

namespace Ubiquitous.Persistence;

public sealed class FileAccountStore : IAccountStore
{
    private readonly string _path;
    private readonly List<AccountRecord> _accounts;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private FileAccountStore(string path, List<AccountRecord> accounts)
    {
        _path = path;
        _accounts = accounts;
    }

    public static FileAccountStore Load(string path)
    {
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new FileAccountStore(path, []);
        var accounts = JsonSerializer.Deserialize<List<AccountRecord>>(text, JsonOptions)
            ?? throw new InvalidDataException("Account file must contain a JSON array.");
        if (accounts.GroupBy(account => account.UserId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidDataException("Account file contains duplicate user IDs.");
        return new FileAccountStore(path, accounts);
    }

    public Task<AccountRecord?> FindByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_accounts) return Task.FromResult(_accounts.FirstOrDefault(account => account.UserId == userId));
    }

    public async Task SetPassHashAsync(int accountId, string passHash, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            List<AccountRecord> snapshot;
            lock (_accounts)
            {
                for (var index = 0; index < _accounts.Count; index++)
                {
                    if (_accounts[index].Id == accountId) _accounts[index] = _accounts[index] with { PassHash = passHash };
                }

                snapshot = [.. _accounts];
            }

            await JsonFile.WriteAsync(_path, snapshot, cancellationToken);
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
