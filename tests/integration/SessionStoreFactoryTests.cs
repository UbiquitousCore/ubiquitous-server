using Microsoft.Extensions.Logging.Abstractions;
using Ubiquitous.Common;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class SessionStoreFactoryTests
{
    [Fact]
    public async Task WithNoSavedAccountsAndNoDevelopmentLogins_StartsAndRefusesEveryLogin()
    {
        var directory = Directory.CreateTempSubdirectory("ubiquitous-stores-");
        try
        {
            var options = ServerOptions.Defaults with { DataDirectory = directory.FullName };

            var stores = SessionStoreFactory.Create(options, postgres: null, NullLogger.Instance);

            Assert.False((await stores.Accounts.AuthenticateAsync("developer", "")).Ok);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RuntimeStores_WriteUnderTheStateDirectoryAndNotTheDataDirectory()
    {
        var data = Directory.CreateTempSubdirectory("ubiquitous-data-");
        var state = Directory.CreateTempSubdirectory("ubiquitous-state-");
        try
        {
            var options = ServerOptions.Defaults with { DataDirectory = data.FullName, StateDirectory = state.FullName };
            var stores = SessionStoreFactory.Create(options, postgres: null, NullLogger.Instance);

            await stores.Mail!.SaveAsync("guid", [new PersistedMailMessage("m1", "guid", "Sender", "Hi", "", 0, 0, false, false)]);

            Assert.NotEmpty(Directory.GetFileSystemEntries(state.FullName, "*", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFileSystemEntries(data.FullName, "*", SearchOption.AllDirectories));
        }
        finally
        {
            data.Delete(recursive: true);
            state.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WithDevelopmentLoginsEnabled_SignsInTheConfiguredDeveloper()
    {
        var directory = Directory.CreateTempSubdirectory("ubiquitous-stores-");
        try
        {
            var options = ServerOptions.Defaults with
            {
                DataDirectory = directory.FullName,
                DevelopmentAccountsEnabled = true,
                DevelopmentPassHash = "dev-secret",
            };

            var stores = SessionStoreFactory.Create(options, postgres: null, NullLogger.Instance);

            Assert.True((await stores.Accounts.AuthenticateAsync("developer", "dev-secret")).Ok);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
