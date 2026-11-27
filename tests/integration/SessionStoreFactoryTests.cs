using Microsoft.Extensions.Logging.Abstractions;
using Ubiquitous.Common;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class SessionStoreFactoryTests
{
    [Fact]
    public async Task AFirstStart_HasNoAccounts_AndRefusesEveryLogin()
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
    public async Task AFirstStart_WritesBothSaveFiles_SoACharacterSurvivesTheNextStart()
    {
        var directory = Directory.CreateTempSubdirectory("ubiquitous-stores-");
        try
        {
            var options = ServerOptions.Defaults with { DataDirectory = directory.FullName };
            var first = SessionStoreFactory.Create(options, postgres: null, NullLogger.Instance);

            Assert.True(File.Exists(Path.Combine(directory.FullName, SessionStoreFactory.AccountsFile)));
            Assert.True(File.Exists(Path.Combine(directory.FullName, SessionStoreFactory.CharactersFile)));
            await first.Characters.CreateAsync(new Ubiquitous.Persistence.CharacterRecord(
                "char-a", 100, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [])
            { Currency = 1234 });

            var restarted = SessionStoreFactory.Create(options, postgres: null, NullLogger.Instance);

            Assert.Equal(1234, (await restarted.Characters.GetByGuidAsync("char-a"))!.Currency);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
