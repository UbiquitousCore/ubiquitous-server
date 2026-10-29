using System.Text.Json;
using Npgsql;

namespace Ubiquitous.Server;

public sealed record ServerOptions(
    string BindAddress,
    int WebSocketPort,
    string DataDirectory,
    string DevelopmentUserId,
    string DevelopmentPassHash,
    string LogLevel,
    string? PostgresConnectionString = null,
    bool FestivalContentEnabled = true,
    IReadOnlyList<uint>? FestivalStaticEntries = null,
    IReadOnlyList<uint>? FestivalCreatureEntries = null,
    IReadOnlyList<int>? DisabledRaces = null,
    bool DevelopmentAccountsEnabled = false,
    string? SecondDevelopmentPassHash = null,
    string SecondDevelopmentUserId = "developer2",
    string? StateDirectory = null,
    int MaxConnections = 500,
    int MaxMessagesPerSecond = 30,
    IReadOnlyList<string>? AllowedOrigins = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Modules = null,
    string? PostgresPasswordFile = null,
    bool RequireDataPackage = false)
{
    private const string ModuleEnvironmentPrefix = "UBIQUITOUS_MODULE_";

    public static ServerOptions Defaults => new(
        BindAddress: "127.0.0.1",
        WebSocketPort: 8781,
        DataDirectory: Path.Combine("data", "generated"),
        DevelopmentUserId: "developer",
        DevelopmentPassHash: "",
        LogLevel: "Information");

    public string ResolvedStateDirectory => StateDirectory ?? DataDirectory;


    private sealed record Setting(
        string? ConfigKey,
        string Environment,
        string[] Arguments,
        Func<ServerOptions, string, string, ServerOptions> Apply);

    private static readonly Setting[] Settings =
    [
        new("bindAddress", "UBIQUITOUS_BIND_ADDRESS", ["--bind", "--bind-address"],
            (options, value, _) => options with { BindAddress = value }),
        new("webSocketPort", "UBIQUITOUS_WEBSOCKET_PORT", ["--websocket-port", "--port"],
            (options, value, name) => options with { WebSocketPort = ParsePort(value, name) }),
        new("dataDirectory", "UBIQUITOUS_DATA_DIRECTORY", ["--data-dir", "-d"],
            (options, value, _) => options with { DataDirectory = value }),
        new("stateDirectory", "UBIQUITOUS_STATE_DIRECTORY", ["--state-dir"],
            (options, value, _) => options with { StateDirectory = value }),
        new("maxConnections", "UBIQUITOUS_MAX_CONNECTIONS", ["--max-connections"],
            (options, value, name) => options with { MaxConnections = ParsePositive(value, name) }),
        new("maxMessagesPerSecond", "UBIQUITOUS_MAX_MESSAGES_PER_SECOND", ["--max-messages-per-second"],
            (options, value, name) => options with { MaxMessagesPerSecond = ParsePositive(value, name) }),
        new("allowedOrigins", "UBIQUITOUS_ALLOWED_ORIGINS", ["--allowed-origins"],
            (options, value, _) => options with { AllowedOrigins = SplitList(value) }),
        new("developmentUserId", "UBIQUITOUS_DEVELOPMENT_USER_ID", ["--development-user"],
            (options, value, _) => options with { DevelopmentUserId = value }),
        new("developmentPassHash", "UBIQUITOUS_DEVELOPMENT_PASS_HASH", ["--development-pass-hash"],
            (options, value, _) => options with { DevelopmentPassHash = value }),
        new("developmentSecondUserId", "UBIQUITOUS_DEVELOPMENT_SECOND_USER_ID", ["--development-second-user"],
            (options, value, _) => options with { SecondDevelopmentUserId = value }),
        new("developmentSecondPassHash", "UBIQUITOUS_DEVELOPMENT_SECOND_PASS_HASH", ["--development-second-pass-hash"],
            (options, value, _) => options with { SecondDevelopmentPassHash = value }),
        new("developmentAccounts", "UBIQUITOUS_DEVELOPMENT_ACCOUNTS", ["--development-accounts"],
            (options, value, name) => options with { DevelopmentAccountsEnabled = ParseBool(value, name) }),
        new("logLevel", "UBIQUITOUS_LOG_LEVEL", ["--log-level"],
            (options, value, _) => options with { LogLevel = value }),
        new("postgresConnectionString", "UBIQUITOUS_POSTGRES_CONNECTION_STRING", ["--postgres-connection-string"],
            (options, value, _) => options with { PostgresConnectionString = value }),
        new("postgresPasswordFile", "UBIQUITOUS_POSTGRES_PASSWORD_FILE", ["--postgres-password-file"],
            (options, value, _) => options with { PostgresPasswordFile = value }),
        new("festivalContentEnabled", "UBIQUITOUS_FESTIVAL_CONTENT_ENABLED", ["--festival-content"],
            (options, value, name) => options with { FestivalContentEnabled = ParseBool(value, name) }),
        new("festivalStaticEntries", "UBIQUITOUS_FESTIVAL_STATIC_ENTRIES", ["--festival-static-entries"],
            (options, value, name) => options with { FestivalStaticEntries = ParseEntryList(value, name) }),
        new("festivalCreatureEntries", "UBIQUITOUS_FESTIVAL_CREATURE_ENTRIES", ["--festival-creature-entries"],
            (options, value, name) => options with { FestivalCreatureEntries = ParseEntryList(value, name) }),
        new("requireDataPackage", "UBIQUITOUS_REQUIRE_DATA_PACKAGE", ["--require-data-package"],
            (options, value, name) => options with { RequireDataPackage = ParseBool(value, name) }),
        new("disabledRaces", "UBIQUITOUS_DISABLED_RACES", ["--disabled-races"],
            (options, value, name) => options with { DisabledRaces = ParseRaceList(value, name) }),
    ];

    // A native run picks up .env and server.json from where it is started, as
    // Docker Compose does, so `dotnet run` needs no wrapper to pass them on.
    public static ServerOptions Load(
        string[] args, IReadOnlyDictionary<string, string?>? environment = null, string? workingDirectory = null)
    {
        var options = Defaults;
        var directory = workingDirectory ?? Directory.GetCurrentDirectory();
        var variables = WithDotEnv(environment ?? ReadEnvironment(), Path.Combine(directory, ".env"));
        var configPath = FindArgument(args, "--config", "-c");
        if (configPath is null
            && variables.TryGetValue("UBIQUITOUS_CONFIG", out var configFromEnvironment)
            && !string.IsNullOrWhiteSpace(configFromEnvironment))
        {
            configPath = configFromEnvironment;
        }

        if (configPath is null && File.Exists(Path.Combine(directory, "server.json")))
            configPath = Path.Combine(directory, "server.json");

        if (configPath is not null)
            options = ApplyConfigFile(options, configPath);

        foreach (var setting in Settings)
        {
            // A variable set but empty (compose passes every listed variable,
            // empty when the operator left it out of .env) means "not said":
            // the config file or the default stands, instead of parsing "".
            if (variables.TryGetValue(setting.Environment, out var value) && !string.IsNullOrWhiteSpace(value))
                options = setting.Apply(options, value, setting.Environment);
        }

        foreach (var (name, value) in variables)
        {
            if (string.IsNullOrWhiteSpace(value) || !name.StartsWith(ModuleEnvironmentPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = name[ModuleEnvironmentPrefix.Length..];
            var split = rest.IndexOf('_', StringComparison.Ordinal);
            if (split <= 0 || split == rest.Length - 1) continue;
            options = options.WithModuleSetting(rest[..split], rest[(split + 1)..], value);
        }

        foreach (var setting in Settings)
        {
            if (FindArgument(args, setting.Arguments) is { } value)
                options = setting.Apply(options, value, setting.Arguments[0]);
        }

        return options.WithPostgresPasswordFromFile();
    }

    public IReadOnlyDictionary<string, string> ModuleSettings(string module) =>
        Modules is not null && Modules.TryGetValue(module.ToLowerInvariant(), out var settings)
            ? settings
            : new Dictionary<string, string>();

    private ServerOptions WithModuleSetting(string module, string key, string value)
    {
        var modules = new Dictionary<string, IReadOnlyDictionary<string, string>>(
            Modules ?? new Dictionary<string, IReadOnlyDictionary<string, string>>(), StringComparer.Ordinal);
        var name = module.ToLowerInvariant();
        var settings = new Dictionary<string, string>(ModuleSettings(name), StringComparer.Ordinal)
        {
            [Ubiquitous.Modules.ModuleSettings.Normalize(key)] = value,
        };
        modules[name] = settings;
        return this with { Modules = modules };
    }

    // Docker generates the database password inside a volume, so no script on the host has to write it into .env.
    private ServerOptions WithPostgresPasswordFromFile()
    {
        if (string.IsNullOrWhiteSpace(PostgresPasswordFile) || string.IsNullOrWhiteSpace(PostgresConnectionString)) return this;
        if (!File.Exists(PostgresPasswordFile))
            throw new InvalidDataException($"UBIQUITOUS_POSTGRES_PASSWORD_FILE names '{PostgresPasswordFile}', which does not exist.");
        var builder = new NpgsqlConnectionStringBuilder(PostgresConnectionString)
        {
            Password = File.ReadAllText(PostgresPasswordFile).Trim(),
        };
        return this with { PostgresConnectionString = builder.ConnectionString };
    }

    // Development logins and `dev.enterWorld` are for file-backed local runs only; a Postgres server uses real accounts.
    public ServerOptions ForStore(bool usesPostgres) =>
        usesPostgres && DevelopmentAccountsEnabled ? this with { DevelopmentAccountsEnabled = false } : this;

    public IReadOnlyList<(int AccountId, string UserId, string PassHash)> DevelopmentLogins()
    {
        if (!DevelopmentAccountsEnabled) return [];
        var logins = new List<(int, string, string)>();
        if (!string.IsNullOrEmpty(DevelopmentPassHash)) logins.Add((1, DevelopmentUserId, DevelopmentPassHash));
        if (!string.IsNullOrEmpty(SecondDevelopmentPassHash)) logins.Add((2, SecondDevelopmentUserId, SecondDevelopmentPassHash));
        return logins;
    }

    private static ServerOptions ApplyConfigFile(ServerOptions options, string path)
    {
        using var stream = File.OpenRead(path);
        var readerOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };
        using var document = JsonDocument.Parse(stream, readerOptions);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Configuration at '{path}' must be a JSON object.");

        foreach (var setting in Settings)
        {
            if (setting.ConfigKey is null
                || !document.RootElement.TryGetProperty(setting.ConfigKey, out var element)
                || element.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            options = setting.Apply(options, ConfigValueText(element), setting.ConfigKey);
        }

        if (document.RootElement.TryGetProperty("modules", out var modules) && modules.ValueKind == JsonValueKind.Object)
        {
            foreach (var module in modules.EnumerateObject())
            {
                if (module.Value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"modules.{module.Name} must be a JSON object.");
                foreach (var setting in module.Value.EnumerateObject())
                {
                    if (setting.Value.ValueKind == JsonValueKind.Null) continue;
                    options = options.WithModuleSetting(module.Name, setting.Name, ConfigValueText(setting.Value));
                }
            }
        }

        return options;
    }

    private static string ConfigValueText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Array => string.Join(',', element.EnumerateArray().Select(ConfigValueText)),
        _ => element.GetRawText(),
    };

    private static int ParsePort(string value, string name)
    {
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
            throw new InvalidDataException($"{name} must be an integer between 1 and 65535.");
        return port;
    }

    private static int ParsePositive(string value, string name)
    {
        if (!int.TryParse(value, out var parsed) || parsed < 1)
            throw new InvalidDataException($"{name} must be a positive integer.");
        return parsed;
    }

    private static bool ParseBool(string value, string name)
    {
        if (bool.TryParse(value, out var parsed)) return parsed;
        return value switch
        {
            "1" => true,
            "0" => false,
            _ => throw new InvalidDataException($"{name} must be true or false."),
        };
    }

    private static IReadOnlyList<uint> ParseEntryList(string value, string name)
    {
        try
        {
            return [.. SplitList(value).Select(uint.Parse)];
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new InvalidDataException($"{name} must be a comma-separated list of entry ids.");
        }
    }

    private static IReadOnlyList<int> ParseRaceList(string value, string name)
    {
        var races = new List<int>();
        foreach (var part in SplitList(value))
        {
            if (!int.TryParse(part, out var race) || race < 0)
                throw new InvalidDataException($"{name} must be a comma-separated list of race ids.");
            races.Add(race);
        }

        return races;
    }

    private static string[] SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? FindArgument(string[] args, params string[] names)
    {
        // The last occurrence wins, so a port added after a script's own
        // arguments overrides the one it passes first.
        for (var index = args.Length - 2; index >= 0; index--)
        {
            if (names.Contains(args[index], StringComparer.OrdinalIgnoreCase)) return args[index + 1];
        }

        return null;
    }

    // A variable already in the environment wins over the same name in .env.
    private static IReadOnlyDictionary<string, string?> WithDotEnv(IReadOnlyDictionary<string, string?> environment, string path)
    {
        if (!File.Exists(path)) return environment;
        var merged = new Dictionary<string, string?>(environment, StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#')) continue;
            if (text.StartsWith("export ", StringComparison.Ordinal)) text = text["export ".Length..];
            var equals = text.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0) continue;
            var value = text[(equals + 1)..].Trim();
            if (value.Length >= 2 && value[0] is ('"' or '\'') && value[^1] == value[0]) value = value[1..^1];
            merged.TryAdd(text[..equals].Trim(), value);
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string?> ReadEnvironment() =>
        Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key is string)
            .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
}
