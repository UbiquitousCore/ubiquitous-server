using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class UnexpectedFailureTests
{
    [Fact]
    public async Task LoginWithoutUserId_RepliesInvalidPayload()
    {
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret"), new InMemoryCharacterStore([]));

        var replies = await session.HandleTextAsync(
            "{\"type\":\"account.login\",\"sequence\":0,\"payload\":{}}");

        var reply = Assert.Single(replies);
        Assert.Contains("protocol.error", reply);
        Assert.Contains("\"code\":\"invalid_payload\"", reply);
    }

    [Fact]
    public async Task StoreFailureWhileHandling_RepliesInternalErrorWithoutTheExceptionText()
    {
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret"), new FailingCharacterStore());
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));

        var replies = await session.HandleTextAsync(
            WebMessageJson.Serialize(WebMessageEnvelope.Create(WebMessageTypes.CharactersList, 1)));

        var reply = Assert.Single(replies);
        Assert.Contains("\"code\":\"internal\"", reply);
        Assert.DoesNotContain("secret-disk-path", reply);
    }

    private sealed class FailingCharacterStore : ICharacterStore
    {
        public Task<CharacterRecord?> GetByGuidAsync(string guid, CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<IReadOnlyList<CharacterRecord>> ListForAccountAsync(int accountId, CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<CharacterRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task UpdateAsync(CharacterRecord character, CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task CreateAsync(CharacterRecord character, CancellationToken cancellationToken = default) =>
            throw Failure();

        public Task<bool> DeleteAsync(string guid, CancellationToken cancellationToken = default) =>
            throw Failure();

        private static IOException Failure() => new("secret-disk-path");
    }
}
