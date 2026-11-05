namespace Ubiquitous.Admin.Web;

// Read from the same variables the game server uses where they mean the same thing.
public sealed record AdminOptions(
    string Urls,
    string? PostgresConnectionString,
    string? PostgresPasswordFile,
    string StateDirectory,
    string DataDirectory,
    string SettingsFile,
    Uri ServerUrl,
    string? AdminTokenFile)
{
    public static AdminOptions FromEnvironment(Func<string, string?> read)
    {
        string? Value(string name) => read(name) is { } value && !string.IsNullOrWhiteSpace(value) ? value : null;
        return new AdminOptions(
            Urls: Value("UBIQUITOUS_ADMIN_URLS") ?? "http://127.0.0.1:8790",
            PostgresConnectionString: Value("UBIQUITOUS_POSTGRES_CONNECTION_STRING"),
            PostgresPasswordFile: Value("UBIQUITOUS_POSTGRES_PASSWORD_FILE"),
            StateDirectory: Value("UBIQUITOUS_STATE_DIRECTORY") ?? Value("UBIQUITOUS_DATA_DIRECTORY") ?? Path.Combine("data", "generated"),
            DataDirectory: Value("UBIQUITOUS_DATA_DIRECTORY") ?? Path.Combine("data", "generated"),
            SettingsFile: Value("UBIQUITOUS_ADMIN_SETTINGS_FILE") ?? ".env",
            ServerUrl: new Uri(Value("UBIQUITOUS_ADMIN_SERVER_URL") ?? "http://127.0.0.1:8781/"),
            AdminTokenFile: Value("UBIQUITOUS_ADMIN_TOKEN_FILE"));
    }
}
