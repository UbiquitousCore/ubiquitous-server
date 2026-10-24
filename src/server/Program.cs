using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;

namespace Ubiquitous.Server;

public static class Program
{
    private const int ExitInvalidData = 1;
    private const int ExitInvalidConfiguration = 2;
    private const int ExitDatabaseUnavailable = 3;
    private const int ExitStoreUnreadable = 4;
    private const int ExitBindFailed = 5;

    public static async Task<int> Main(string[] args)
    {
        var accountCommand = args is ["account", ..];
        ServerOptions options;
        LogLevel logLevel;
        try
        {
            options = ServerOptions.Load(args);
            logLevel = ParseLogLevel(options.LogLevel);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentException
            or JsonException or IOException)
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            return ExitInvalidConfiguration;
        }

        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(accountCommand && logLevel < LogLevel.Warning ? LogLevel.Warning : logLevel)
            .AddSimpleConsole(console =>
            {
                console.SingleLine = true;
                console.TimestampFormat = "HH:mm:ss ";
            }));
        var logger = loggerFactory.CreateLogger("Ubiquitous.Server");

        if (accountCommand) return await RunAccountCommandAsync(args[1..], options, logger);

        var readiness = new ServerReadinessState();
        if (!TryLoadCatalog(options.DataDirectory, readiness, logger, out var catalog)) return ExitInvalidData;

        NpgsqlDataSource? postgres = null;
        if (!string.IsNullOrWhiteSpace(options.PostgresConnectionString))
        {
            postgres = await ConnectPostgresAsync(options.PostgresConnectionString, logger);
            if (postgres is null) return ExitDatabaseUnavailable;
        }

        await using var _ = postgres;
        if (postgres is not null && options.DevelopmentAccountsEnabled)
            logger.LogWarning("Development accounts are ignored with Postgres; create real ones with: account create <name>");
        options = options.ForStore(postgres is not null);

        SessionStores stores;
        try
        {
            stores = SessionStoreFactory.Create(options, postgres, logger);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogCritical(
                "Account/character store error: {Reason}. Fix or remove {Accounts} or {Characters} in {Directory}, then restart",
                ex.Message, SessionStoreFactory.AccountsFile, SessionStoreFactory.CharactersFile,
                Path.GetFullPath(options.ResolvedStateDirectory));
            return ExitStoreUnreadable;
        }

        var moduleAccounts = new ModuleAccounts(stores.Accounts);
        stores = stores with { Accounts = moduleAccounts };

        ServerContent content;
        try
        {
            content = new ServerContentLoader(catalog, options, logger, ContentDirectory.Find()).Load();
        }
        catch (InvalidDataException ex)
        {
            logger.LogCritical("Data package error: {Reason}. Fix or re-import the package in {Directory}",
                ex.Message, Path.GetFullPath(options.DataDirectory));
            return ExitInvalidData;
        }

        var world = new SharedWorld(stores, loggerFactory.CreateLogger<SharedWorld>());
        var sessions = new PlaySessionFactory(
            stores, content, world, options.DevelopmentAccountsEnabled, loggerFactory.CreateLogger<PlaySession>(),
            options.DevelopmentLogins().Select(login => login.UserId));

        var gatewayLogger = loggerFactory.CreateLogger<WebSocketGateway>();
        var hub = new PlaySessionHub(gatewayLogger);
        var modules = new ServerModules(loggerFactory.CreateLogger<ServerModules>());
        try
        {
            modules.Register(ServerModules.BuiltIn(), module => new ModuleHost(
                module.Name, new Ubiquitous.Modules.ModuleSettings(options.ModuleSettings(module.Name)), modules, sessions, hub,
                moduleAccounts, options.MaxMessagesPerSecond, loggerFactory.CreateLogger($"Ubiquitous.Modules.{module.Name}")));
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"Module configuration error: {ex.Message}");
            return ExitInvalidConfiguration;
        }

        await using var gateway = new WebSocketGateway(
            options, sessions, readiness, logger: gatewayLogger, hub: hub, afterTick: modules.TickAsync);
        try
        {
            gateway.Start();
        }
        catch (System.Net.HttpListenerException ex)
        {
            logger.LogCritical("Bind failed on {Address}:{Port}: {Reason}. Check --bind/--port",
                options.BindAddress, options.WebSocketPort, ex.Message);
            return ExitBindFailed;
        }

        logger.LogInformation("Server started on port {Port} ({Address}). Press Ctrl+C to stop", options.WebSocketPort, options.BindAddress);
        await WaitForShutdownAsync();
        await modules.ShutdownAsync(CancellationToken.None);
        return 0;
    }

    private static async Task<int> RunAccountCommandAsync(string[] args, ServerOptions options, ILogger logger)
    {
        NpgsqlDataSource? postgres = null;
        if (!string.IsNullOrWhiteSpace(options.PostgresConnectionString))
        {
            postgres = await ConnectPostgresAsync(options.PostgresConnectionString, logger);
            if (postgres is null) return ExitDatabaseUnavailable;
        }

        await using var _ = postgres;
        string? piped = null;
        string? ReadPassword(string prompt)
        {
            if (Console.IsInputRedirected) return piped ??= Console.In.ReadLine();
            Console.Error.Write(prompt);
            var typed = new System.Text.StringBuilder();
            while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
            {
                if (key.Key == ConsoleKey.Backspace && typed.Length > 0) typed.Length--;
                else if (!char.IsControl(key.KeyChar)) typed.Append(key.KeyChar);
            }

            Console.Error.WriteLine();
            return typed.ToString();
        }

        try
        {
            return await AccountCommands.RunAsync(
                AccountCommands.Words(args), SessionStoreFactory.OpenAccountAdmin(options, postgres), ReadPassword,
                Console.Out, Console.Error);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException
            or NpgsqlException)
        {
            Console.Error.WriteLine($"Account store error: {ex.Message}");
            return ExitStoreUnreadable;
        }
    }

    private static LogLevel ParseLogLevel(string value) =>
        Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level)
            ? level
            : throw new InvalidDataException($"Log level '{value}' is not one of {string.Join(", ", Enum.GetNames<LogLevel>())}.");

    private static bool TryLoadCatalog(
        string dataDirectory, ServerReadinessState readiness, ILogger logger, out DataCatalog? catalog)
    {
        catalog = null;
        try
        {
            catalog = DataCatalog.LoadFromDirectory(dataDirectory);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or JsonException)
        {
            logger.LogWarning(
                "No data package at {Manifest}; running on development content. See docs/development/setup.md",
                Path.Combine(Path.GetFullPath(dataDirectory), DataManifest.FileName));
            readiness.SetDataUnavailable("No generated data package is loaded; using development fallback data.");
            return true;
        }

        var manifest = catalog.Manifest;
        logger.LogInformation("Data package: game {GameVersion}, extractor {ExtractorVersion}, {Count} dataset(s)",
            manifest.GameVersion, manifest.ExtractorVersion, manifest.Datasets?.Count ?? 0);
        var validation = catalog.Validate();
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors) logger.LogCritical("Data validation: {Error}", error);
            return false;
        }

        readiness.SetDataReady();
        if (!DataManifest.IsTargetGameVersion(manifest.GameVersion))
        {
            logger.LogWarning("Game version {GameVersion} differs from target {Target}; schema drift may apply",
                manifest.GameVersion, DataManifest.TargetGameVersion);
        }

        return true;
    }

    private static async Task<NpgsqlDataSource?> ConnectPostgresAsync(string connectionString, ILogger logger)
    {
        NpgsqlDataSource? dataSource = null;
        try
        {
            dataSource = NpgsqlDataSource.Create(connectionString);
            await PostgresSchema.EnsureCreatedAsync(dataSource);
            logger.LogInformation("Postgres connected, schema ensured");
            return dataSource;
        }
        catch (Exception ex) when (ex is NpgsqlException or ArgumentException or TimeoutException)
        {
            if (dataSource is not null) await dataSource.DisposeAsync();
            logger.LogCritical(
                "Postgres connection error: {Reason}. Check UBIQUITOUS_POSTGRES_CONNECTION_STRING, or unset it to run file-backed",
                ex.Message);
            return null;
        }
    }

    private static Task WaitForShutdownAsync()
    {
        var shutdown = new TaskCompletionSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.TrySetResult();
        };
        var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            shutdown.TrySetResult();
        });
        return shutdown.Task.ContinueWith(_ => terminate.Dispose(), TaskScheduler.Default);
    }
}
