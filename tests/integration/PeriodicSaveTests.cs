using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PeriodicSaveTests
{
    [Fact]
    public async Task InWorldSession_IsSavedPeriodically()
    {
        var characters = new CountingCharacterStore();
        var (hub, _) = await EnteredAsync(characters);
        var start = DateTimeOffset.UtcNow;

        await hub.AutosaveAsync(start + PlaySession.AutosaveInterval - TimeSpan.FromSeconds(1));
        Assert.Equal(0, characters.Updates);

        await hub.AutosaveAsync(start + PlaySession.AutosaveInterval + TimeSpan.FromSeconds(1));
        Assert.Equal(1, characters.Updates);

        await hub.AutosaveAsync(start + PlaySession.AutosaveInterval + TimeSpan.FromSeconds(2));
        Assert.Equal(1, characters.Updates);
    }

    [Fact]
    public async Task APeriodicSaveOfAnUnchangedCharacterWritesNothing()
    {
        var characters = new CountingCharacterStore();
        var (hub, session) = await EnteredAsync(characters);
        var start = DateTimeOffset.UtcNow;
        await hub.AutosaveAsync(start + PlaySession.AutosaveInterval + TimeSpan.FromSeconds(1));

        await hub.AutosaveAsync(start + PlaySession.AutosaveInterval * 2 + TimeSpan.FromSeconds(2));
        Assert.Equal(1, characters.Updates);

        await hub.SendInOrderAsync(session, () => session.HandleTextAsync(Serialize(
            WebMessageTypes.PlayerMove, 2, new PlayerMovePayload(3, 3, 5, 0, true, MovementState.Walking))));
        await hub.AutosaveAsync(start + PlaySession.AutosaveInterval * 3 + TimeSpan.FromSeconds(3));
        Assert.Equal(2, characters.Updates);
    }

    [Fact]
    public async Task ASaveOfOnePartMakesTheNextWholeSaveWriteAgain()
    {
        var characters = new CountingCharacterStore();
        var (_, session) = await EnteredAsync(characters);
        await session.SaveProgressAsync();

        await session.SaveCharacterAsync();
        await session.SaveProgressAsync();

        Assert.Equal(3, characters.Updates);
    }

    [Fact]
    public async Task TheSessionKeepsTimeWithTheWorldsClock()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var characters = new CountingCharacterStore();
        var hub = new PlaySessionHub();
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters, time: clock);
        hub.Register(session, _ => Task.CompletedTask);
        await hub.SendInOrderAsync(session, () => session.HandleTextAsync(Serialize(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await hub.SendInOrderAsync(session, () => session.HandleTextAsync(Serialize(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        clock.Advance(PlaySession.AutosaveInterval - TimeSpan.FromSeconds(1));
        await hub.AutosaveAsync(clock.GetUtcNow());
        Assert.Equal(0, characters.Updates);

        clock.Advance(TimeSpan.FromSeconds(2));
        await hub.AutosaveAsync(clock.GetUtcNow());
        Assert.Equal(1, characters.Updates);
    }

    [Fact]
    public async Task FailedPeriodicSave_DoesNotDisconnect_AndRetriesNextInterval()
    {
        var characters = new CountingCharacterStore { Failing = true };
        var (hub, session) = await EnteredAsync(characters);
        var due = DateTimeOffset.UtcNow + PlaySession.AutosaveInterval + TimeSpan.FromSeconds(1);

        await hub.AutosaveAsync(due);
        Assert.Equal(1, characters.Updates);
        Assert.NotNull(session.CurrentMapId);

        characters.Failing = false;
        await hub.AutosaveAsync(due + TimeSpan.FromSeconds(1));
        Assert.Equal(1, characters.Updates);
        await hub.AutosaveAsync(due + PlaySession.AutosaveInterval);
        Assert.Equal(2, characters.Updates);
        Assert.NotNull(session.CurrentMapId);
    }

    [Fact]
    public async Task ASessionNotInTheWorld_IsNeverSaved()
    {
        var characters = new CountingCharacterStore();
        var hub = new PlaySessionHub();
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters);
        hub.Register(session, _ => Task.CompletedTask);

        await hub.AutosaveAsync(DateTimeOffset.UtcNow + PlaySession.AutosaveInterval * 3);

        Assert.Equal(0, characters.Updates);
    }

    private static async Task<(PlaySessionHub Hub, PlaySession Session)> EnteredAsync(CountingCharacterStore characters)
    {
        var hub = new PlaySessionHub();
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters);
        hub.Register(session, _ => Task.CompletedTask);
        await hub.SendInOrderAsync(session, () => session.HandleTextAsync(Serialize(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await hub.SendInOrderAsync(session, () => session.HandleTextAsync(Serialize(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return (hub, session);
    }

    private static string Serialize(string type, int sequence, object payload) =>
        WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload));

    private sealed class CountingCharacterStore : ICharacterStore
    {
        private readonly InMemoryCharacterStore _inner = new(
            [new CharacterRecord("char-save", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]);

        public int Updates { get; private set; }

        public bool Failing { get; set; }

        public Task<CharacterRecord?> GetByGuidAsync(string guid, CancellationToken cancellationToken = default) =>
            _inner.GetByGuidAsync(guid, cancellationToken);

        public Task<IReadOnlyList<CharacterRecord>> ListForAccountAsync(int accountId, CancellationToken cancellationToken = default) =>
            _inner.ListForAccountAsync(accountId, cancellationToken);

        public Task<CharacterRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
            _inner.FindByNameAsync(name, cancellationToken);

        public Task UpdateAsync(CharacterRecord character, CancellationToken cancellationToken = default)
        {
            Updates++;
            return Failing ? Task.FromException(new IOException("disk full")) : _inner.UpdateAsync(character, cancellationToken);
        }

        public Task CreateAsync(CharacterRecord character, CancellationToken cancellationToken = default) =>
            _inner.CreateAsync(character, cancellationToken);

        public Task<bool> DeleteAsync(string guid, CancellationToken cancellationToken = default) =>
            _inner.DeleteAsync(guid, cancellationToken);
    }
}
