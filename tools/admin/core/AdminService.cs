namespace Ubiquitous.Admin;

public sealed record SettingView(Setting Setting, string? Value);

public sealed record SettingsResult(bool Saved, IReadOnlyDictionary<string, string> Problems);

// What the admin page can do. Only a GM account may sign in to it, since
// every action here is one a GM would otherwise do from the command line.
public sealed class AdminService(
    IAccountDirectory accounts,
    ICharacterDirectory characters,
    ISettingsStore settings,
    IServerStatus status,
    IServerControl control,
    IItemCatalog items,
    IOnlinePlayers online)
{
    public async Task<AdminAccount?> SignInAsync(string userId, string password, CancellationToken cancellationToken = default)
    {
        if (!await accounts.PasswordMatchesAsync(userId, password, cancellationToken)) return null;
        var account = await accounts.FindAsync(userId, cancellationToken);
        return account is { IsGm: true } ? account : null;
    }

    // A GM taken away while signed in loses the page on their next request.
    public async Task<bool> IsStillGmAsync(string userId, CancellationToken cancellationToken = default) =>
        await accounts.FindAsync(userId, cancellationToken) is { IsGm: true };

    public Task<ServerStatus> StatusAsync(CancellationToken cancellationToken = default) => status.CheckAsync(cancellationToken);

    // Players are saved and disconnected; the server reads its settings again as it starts.
    public Task<bool> RestartServerAsync(CancellationToken cancellationToken = default) => control.RestartAsync(cancellationToken);

    public Task<IReadOnlyList<AdminAccount>> AccountsAsync(CancellationToken cancellationToken = default) =>
        accounts.ListAsync(cancellationToken);

    public Task<AccountChange> CreateAccountAsync(string userId, string password, CancellationToken cancellationToken = default) =>
        accounts.CreateAsync(userId.Trim(), password, cancellationToken);

    public Task<AccountChange> SetPasswordAsync(int accountId, string password, CancellationToken cancellationToken = default) =>
        accounts.SetPasswordAsync(accountId, password, cancellationToken);

    public Task<AccountChange> SetGmAsync(int accountId, bool isGm, CancellationToken cancellationToken = default) =>
        accounts.SetGmAsync(accountId, isGm, cancellationToken);

    public Task<IReadOnlyList<AdminCharacter>> CharactersAsync(int accountId, CancellationToken cancellationToken = default) =>
        characters.ListForAccountAsync(accountId, cancellationToken);

    public async Task<CharacterChange> DeleteCharacterAsync(string guid, CancellationToken cancellationToken = default)
    {
        if (await characters.GetAsync(guid, cancellationToken) is not { } detail) return CharacterChange.NotFound;
        if (await IsOnlineAsync(detail.Character.Name, cancellationToken)) return CharacterChange.Online;
        return await characters.DeleteAsync(guid, cancellationToken) ? CharacterChange.Done : CharacterChange.NotFound;
    }

    public async Task<(CharacterDetail Detail, bool Online)?> CharacterAsync(string guid, CancellationToken cancellationToken = default) =>
        await characters.GetAsync(guid, cancellationToken) is { } detail
            ? (detail, await IsOnlineAsync(detail.Character.Name, cancellationToken))
            : null;

    // A character in the world is saved from the server's memory when it leaves, which would undo any change made
    // here, so it is left alone until then.
    public async Task<CharacterChange> EditCharacterAsync(string guid, CharacterEdit edit, CancellationToken cancellationToken = default)
    {
        if (await characters.GetAsync(guid, cancellationToken) is not { } detail) return CharacterChange.NotFound;
        if (await IsOnlineAsync(detail.Character.Name, cancellationToken)) return CharacterChange.Online;
        return await characters.EditAsync(guid, edit, cancellationToken);
    }

    public async Task<CharacterChange> GiveItemAsync(string guid, int entry, int count, CancellationToken cancellationToken = default)
    {
        if (await characters.GetAsync(guid, cancellationToken) is not { } detail) return CharacterChange.NotFound;
        if (await IsOnlineAsync(detail.Character.Name, cancellationToken)) return CharacterChange.Online;
        return await characters.GiveItemAsync(guid, entry, count, cancellationToken);
    }

    public IReadOnlyList<AdminItem> SearchItems(string text) => items.Search(text.Trim(), 30);

    // A server that cannot be asked is not running, so nobody is in its world to overwrite the change.
    private async Task<bool> IsOnlineAsync(string name, CancellationToken cancellationToken) =>
        await online.NamesAsync(cancellationToken) is { } names && names.Contains(name);

    public string SettingsLocation => settings.Location;

    public IReadOnlyList<SettingView> Settings()
    {
        var current = settings.Read();
        return [.. SettingCatalog.All.Select(setting =>
            new SettingView(setting, current.TryGetValue(setting.Name, out var value) ? value : null))];
    }

    // Nothing is written unless every value is acceptable.
    public SettingsResult SaveSettings(IReadOnlyDictionary<string, string?> values)
    {
        var problems = new Dictionary<string, string>(StringComparer.Ordinal);
        var changes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            if (SettingCatalog.Find(name) is not { } setting)
            {
                problems[name] = "Not a setting this page changes.";
                continue;
            }

            var text = value?.Trim() ?? "";
            if (SettingCatalog.Problem(setting, text) is { } problem) problems[name] = problem;
            else changes[name] = text.Length == 0 ? null : text;
        }

        if (problems.Count > 0) return new SettingsResult(false, problems);
        settings.Write(changes);
        return new SettingsResult(true, problems);
    }
}
