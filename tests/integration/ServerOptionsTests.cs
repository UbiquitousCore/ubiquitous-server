using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class ServerOptionsTests
{
    [Fact]
    public void ConfigurationPrecedence_IsDefaultsThenFileThenEnvironmentThenArguments()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ubiquitous-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "bindAddress": "0.0.0.0",
              "webSocketPort": 9000,
              "dataDirectory": "from-file",
              "logLevel": "Debug"
            }
            """);

        try
        {
            var environment = new Dictionary<string, string?>
            {
                ["UBIQUITOUS_WEBSOCKET_PORT"] = "9001",
                ["UBIQUITOUS_DATA_DIRECTORY"] = "from-environment",
            };

            var options = ServerOptions.Load(
                ["--config", path, "--data-dir", "from-arguments"], environment);

            Assert.Equal("0.0.0.0", options.BindAddress);
            Assert.Equal(9001, options.WebSocketPort);
            Assert.Equal("from-arguments", options.DataDirectory);
            Assert.Equal("Debug", options.LogLevel);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PostgresPasswordFile_FillsThePasswordIntoTheConnectionString()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ubiquitous-pg-{Guid.NewGuid():N}");
        File.WriteAllText(path, "s3cret\n");
        try
        {
            var options = ServerOptions.Load([], new Dictionary<string, string?>
            {
                ["UBIQUITOUS_POSTGRES_CONNECTION_STRING"] = "Host=db;Username=ubiquitous;Database=ubiquitous",
                ["UBIQUITOUS_POSTGRES_PASSWORD_FILE"] = path,
            });

            var builder = new Npgsql.NpgsqlConnectionStringBuilder(options.PostgresConnectionString);
            Assert.Equal("s3cret", builder.Password);
            Assert.Equal("db", builder.Host);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PostgresPasswordFile_WithoutAConnectionString_StaysFileBacked()
    {
        var options = ServerOptions.Load([], new Dictionary<string, string?>
        {
            ["UBIQUITOUS_POSTGRES_PASSWORD_FILE"] = "/does/not/matter",
        });

        Assert.Null(options.PostgresConnectionString);
    }

    [Fact]
    public void PostgresPasswordFile_ThatIsMissing_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() => ServerOptions.Load([], new Dictionary<string, string?>
        {
            ["UBIQUITOUS_POSTGRES_CONNECTION_STRING"] = "Host=db",
            ["UBIQUITOUS_POSTGRES_PASSWORD_FILE"] = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"),
        }));
    }

    [Fact]
    public void DotEnvBesideTheServer_IsRead_ButTheEnvironmentWins()
    {
        var directory = Directory.CreateTempSubdirectory("ubiquitous-dotenv-").FullName;
        File.WriteAllText(Path.Combine(directory, ".env"), """
            # comment
            UBIQUITOUS_DEVELOPMENT_ACCOUNTS=true
            UBIQUITOUS_DEVELOPMENT_PASS_HASH="from-dotenv"
            export UBIQUITOUS_LOG_LEVEL=Debug
            UBIQUITOUS_WEBSOCKET_PORT=9002
            """);
        try
        {
            var options = ServerOptions.Load([], new Dictionary<string, string?>
            {
                ["UBIQUITOUS_WEBSOCKET_PORT"] = "9003",
            }, directory);

            Assert.True(options.DevelopmentAccountsEnabled);
            Assert.Equal("from-dotenv", options.DevelopmentPassHash);
            Assert.Equal("Debug", options.LogLevel);
            Assert.Equal(9003, options.WebSocketPort);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ServerJsonBesideTheServer_IsRead_UnlessAnotherIsNamed()
    {
        var directory = Directory.CreateTempSubdirectory("ubiquitous-server-json-").FullName;
        File.WriteAllText(Path.Combine(directory, "server.json"), "{ \"webSocketPort\": 9004 }");
        var other = Path.Combine(directory, "other.json");
        File.WriteAllText(other, "{ \"webSocketPort\": 9005 }");
        try
        {
            var empty = new Dictionary<string, string?>();
            Assert.Equal(9004, ServerOptions.Load([], empty, directory).WebSocketPort);
            Assert.Equal(9005, ServerOptions.Load(["--config", other], empty, directory).WebSocketPort);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RepeatedArgument_TakesTheLastOne()
    {
        var options = ServerOptions.Load(
            ["--port", "8781", "--port", "9100"], new Dictionary<string, string?>());

        Assert.Equal(9100, options.WebSocketPort);
    }

    [Fact]
    public void InvalidEnvironmentPort_IsRejected()
    {
        var environment = new Dictionary<string, string?>
        {
            ["UBIQUITOUS_WEBSOCKET_PORT"] = "70000",
        };

        Assert.Throws<InvalidDataException>(() => ServerOptions.Load([], environment));
    }

    [Fact]
    public void FestivalOptions_LoadFromFileEnvironmentAndArguments()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ubiquitous-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "festivalContentEnabled": false,
              "festivalStaticEntries": [288, 145]
            }
            """);

        try
        {
            var environment = new Dictionary<string, string?>
            {
                ["UBIQUITOUS_FESTIVAL_STATIC_ENTRIES"] = "288,146",
            };

            var options = ServerOptions.Load(
                ["--config", path, "--festival-content", "true", "--festival-static-entries", "288"], environment);

            Assert.True(options.FestivalContentEnabled);
            Assert.Equal([288u], options.FestivalStaticEntries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FestivalOptions_DefaultToEnabledWithNoEntries()
    {
        var options = ServerOptions.Load([], new Dictionary<string, string?>());

        Assert.True(options.FestivalContentEnabled);
        Assert.Null(options.FestivalStaticEntries);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("2")]
    public void InvalidFestivalFlag_IsRejected(string value)
    {
        var environment = new Dictionary<string, string?>
        {
            ["UBIQUITOUS_FESTIVAL_CONTENT_ENABLED"] = value,
        };

        Assert.Throws<InvalidDataException>(() => ServerOptions.Load([], environment));
    }

    [Theory]
    [InlineData("288,x")]
    [InlineData("99999999999")]
    public void InvalidFestivalEntries_AreRejected(string value)
    {
        var environment = new Dictionary<string, string?>
        {
            ["UBIQUITOUS_FESTIVAL_STATIC_ENTRIES"] = value,
        };

        Assert.Throws<InvalidDataException>(() => ServerOptions.Load([], environment));
    }

    [Fact]
    public void FilterFestiveSpawns_HidesOnlyListedEntriesWhenDisabled()
    {
        var spawns = new[]
        {
            new StaticObjectSpawnDefinition(1, 13, 288, 0, 0, 0, 0),
            new StaticObjectSpawnDefinition(2, 13, 52, 0, 0, 0, 0),
        };

        var hidden = ServerContentLoader.FilterFestiveSpawns(spawns, false, [288u]);
        Assert.Single(hidden);
        Assert.Equal(52u, hidden[0].EntryId);

        Assert.Equal(2, ServerContentLoader.FilterFestiveSpawns(spawns, true, [288u]).Count);
        Assert.Equal(2, ServerContentLoader.FilterFestiveSpawns(spawns, false, null).Count);
        Assert.Equal(2, ServerContentLoader.FilterFestiveSpawns(spawns, false, []).Count);
    }

    [Fact]
    public void FilterFestiveCreatures_HidesEverySpawnOfAListedEntry()
    {
        var spawns = new[]
        {
            new CreatureSpawnDefinition(1, 13, 3012, 0, 0, 0, 0),
            new CreatureSpawnDefinition(2, 1, 3012, 0, 0, 0, 0),
            new CreatureSpawnDefinition(3, 13, 726, 0, 0, 0, 0),
        };

        var kept = ServerContentLoader.FilterFestiveCreatures(spawns, false, [3012u]);
        Assert.Single(kept);
        Assert.Equal(726u, kept[0].CreatureEntryId);

        Assert.Equal(3, ServerContentLoader.FilterFestiveCreatures(spawns, true, [3012u]).Count);
        Assert.Equal(3, ServerContentLoader.FilterFestiveCreatures(spawns, false, null).Count);
        Assert.Equal(3, ServerContentLoader.FilterFestiveCreatures(spawns, false, []).Count);
    }

    [Fact]
    public void TurningTheFestivalOff_NeedsNothingElseSaid()
    {
        var off = ServerOptions.Load(["--festival-content", "false"], new Dictionary<string, string?>());

        Assert.False(off.FestivalContentEnabled);
        Assert.Null(off.FestivalStaticEntries);
        Assert.Null(off.FestivalCreatureEntries);

        Assert.NotEmpty(FestivalContent.StaticObjectEntries);
        Assert.NotEmpty(FestivalContent.CreatureEntries);

        Assert.True(FestivalContent.StaticObjectEntries.Count > 40,
            "the scenery list should cover the event, not one prop");

        Assert.Contains(3014u, FestivalContent.CreatureEntries);
        Assert.Contains(2802u, FestivalContent.CreatureEntries);
        Assert.True(FestivalContent.CreatureEntries.Count > 15,
            "the creature list should cover both events, not one of them");

        Assert.False(FestivalIds.IsSeasonal(FestivalIds.Expansion1));
        Assert.True(FestivalIds.IsSeasonal(FestivalIds.WinterSolstice));
        Assert.True(FestivalIds.IsSeasonal(FestivalIds.Anniversary));

        var scenery = new[] { new StaticObjectSpawnDefinition(1, 13, FestivalContent.StaticObjectEntries[0], 0, 0, 0, 0) };
        var creatures = new[] { new CreatureSpawnDefinition(1, 13, FestivalContent.CreatureEntries[0], 0, 0, 0, 0) };

        Assert.Empty(ServerContentLoader.FilterFestiveSpawns(scenery, off.FestivalContentEnabled, FestivalContent.StaticObjectEntries));
        Assert.Empty(ServerContentLoader.FilterFestiveCreatures(creatures, off.FestivalContentEnabled, FestivalContent.CreatureEntries));
    }

    [Fact]
    public void FestivalCreatureEntries_AreReadFromTheCommandLineAndTheEnvironment()
    {
        var fromArgs = ServerOptions.Load(["--festival-creature-entries", "3012,2781"], new Dictionary<string, string?>());
        Assert.Equal([3012u, 2781u], fromArgs.FestivalCreatureEntries);

        var fromEnvironment = ServerOptions.Load([], new Dictionary<string, string?>
        {
            ["UBIQUITOUS_FESTIVAL_CREATURE_ENTRIES"] = "3012",
        });
        Assert.Equal([3012u], fromEnvironment.FestivalCreatureEntries);
    }

    [Fact]
    public void DevelopmentLogins_AreNoneUnlessExplicitlyEnabled()
    {
        var options = ServerOptions.Load([], new Dictionary<string, string?>
        {
            ["UBIQUITOUS_DEVELOPMENT_PASS_HASH"] = "local-only",
            ["UBIQUITOUS_DEVELOPMENT_SECOND_PASS_HASH"] = "local-only-2",
        });

        Assert.Empty(options.DevelopmentLogins());
    }

    [Fact]
    public void DevelopmentLogins_WhenEnabled_CreateOnlyTheLoginsThatHaveAPassword()
    {
        var primaryOnly = ServerOptions.Load([], new Dictionary<string, string?>
        {
            ["UBIQUITOUS_DEVELOPMENT_ACCOUNTS"] = "true",
            ["UBIQUITOUS_DEVELOPMENT_PASS_HASH"] = "local-only",
        });
        Assert.Equal([(1, "developer", "local-only")], primaryOnly.DevelopmentLogins());

        var both = primaryOnly with { SecondDevelopmentPassHash = "local-only-2" };
        Assert.Equal(
            [(1, "developer", "local-only"), (2, "developer2", "local-only-2")],
            both.DevelopmentLogins());

        var noPassword = primaryOnly with { DevelopmentPassHash = "" };
        Assert.Empty(noPassword.DevelopmentLogins());
    }

    [Fact]
    public void StateDirectory_DefaultsToTheDataDirectory()
    {
        var options = ServerOptions.Load(["--data-dir", "package"], new Dictionary<string, string?>());

        Assert.Equal("package", options.ResolvedStateDirectory);
    }

    [Fact]
    public void StateDirectory_IsReadFromTheEnvironmentAndOverriddenByTheArgument()
    {
        var environment = new Dictionary<string, string?> { ["UBIQUITOUS_STATE_DIRECTORY"] = "from-environment" };

        Assert.Equal("from-environment", ServerOptions.Load([], environment).ResolvedStateDirectory);
        Assert.Equal("from-arguments", ServerOptions.Load(["--state-dir", "from-arguments"], environment).ResolvedStateDirectory);
    }

    [Fact]
    public void DevelopmentLogins_TakeTheSecondUserIdFromTheEnvironment()
    {
        var options = ServerOptions.Load([], new Dictionary<string, string?>
        {
            ["UBIQUITOUS_DEVELOPMENT_ACCOUNTS"] = "true",
            ["UBIQUITOUS_DEVELOPMENT_PASS_HASH"] = "local-only",
            ["UBIQUITOUS_DEVELOPMENT_SECOND_USER_ID"] = "two",
            ["UBIQUITOUS_DEVELOPMENT_SECOND_PASS_HASH"] = "local-only-2",
        });

        Assert.Equal([(1, "developer", "local-only"), (2, "two", "local-only-2")], options.DevelopmentLogins());
    }

    [Fact]
    public void GatewayLimits_HaveDefaultsAndReadFromTheEnvironmentAndArguments()
    {
        var defaults = ServerOptions.Load([], new Dictionary<string, string?>());
        Assert.Equal(500, defaults.MaxConnections);
        Assert.Equal(30, defaults.MaxMessagesPerSecond);
        Assert.Null(defaults.AllowedOrigins);

        var environment = new Dictionary<string, string?>
        {
            ["UBIQUITOUS_MAX_CONNECTIONS"] = "40",
            ["UBIQUITOUS_ALLOWED_ORIGINS"] = "https://a.example, https://b.example",
        };
        var loaded = ServerOptions.Load(["--max-messages-per-second", "12"], environment);
        Assert.Equal(40, loaded.MaxConnections);
        Assert.Equal(12, loaded.MaxMessagesPerSecond);
        Assert.Equal(["https://a.example", "https://b.example"], loaded.AllowedOrigins);
    }

    [Fact]
    public void GatewayLimits_RefuseANonPositiveNumber()
    {
        Assert.Throws<InvalidDataException>(() =>
            ServerOptions.Load([], new Dictionary<string, string?> { ["UBIQUITOUS_MAX_CONNECTIONS"] = "0" }));
    }

    [Fact]
    public void ConfigPath_IsReadFromTheEnvironmentWhenNoFlagIsGiven()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ubiquitous-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "webSocketPort": 9002
            }
            """);

        try
        {
            var environment = new Dictionary<string, string?> { ["UBIQUITOUS_CONFIG"] = path };
            Assert.Equal(9002, ServerOptions.Load([], environment).WebSocketPort);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConfigFlag_WinsOverTheEnvironmentConfigPath()
    {
        var fromEnvironment = Path.Combine(Path.GetTempPath(), $"ubiquitous-config-{Guid.NewGuid():N}.json");
        var fromArguments = Path.Combine(Path.GetTempPath(), $"ubiquitous-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(fromEnvironment, """{ "webSocketPort": 9003 }""");
        File.WriteAllText(fromArguments, """{ "webSocketPort": 9004 }""");

        try
        {
            var environment = new Dictionary<string, string?> { ["UBIQUITOUS_CONFIG"] = fromEnvironment };
            var options = ServerOptions.Load(["--config", fromArguments], environment);
            Assert.Equal(9004, options.WebSocketPort);
        }
        finally
        {
            File.Delete(fromEnvironment);
            File.Delete(fromArguments);
        }
    }

    [Fact]
    public void ConfigFile_AllowsCommentsAndModuleSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ubiquitous-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              // Bots and seasonal content live beside the other settings.
              "festivalContentEnabled": false,
              "modules": {
                "playerbots": {
                  "enabled": true,
                  "randomBotCount": 3
                }
              }
            }
            """);

        try
        {
            var options = ServerOptions.Load(["--config", path], new Dictionary<string, string?>());
            Assert.False(options.FestivalContentEnabled);
            Assert.Equal("true", options.ModuleSettings("playerbots")["enabled"]);
            Assert.Equal("3", options.ModuleSettings("playerbots")["randombotcount"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
