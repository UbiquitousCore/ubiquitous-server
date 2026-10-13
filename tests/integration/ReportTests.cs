using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class ReportTests
{
    [Fact]
    public async Task ReportingAPlayer_IsAcknowledgedAndPersistsToTheStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-reports-{Guid.NewGuid():N}");
        try
        {
            var reportStore = new FileReportStore(directory);
            var characters = new InMemoryCharacterStore(
            [
                Character("char-a", 1, "Ayla"),
                Character("char-b", 2, "Borin"),
            ]);

            var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, reportStore: reportStore);
            await LoginAsync(session, "developer", "Ayla");

            var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.SocialReport, 2, new ReportPlayerPayload("player:char-b", "spamming trade chat"))));

            var reply = Assert.Single(responses);
            Assert.Contains("\"type\":\"social.reportAcknowledged\"", reply);

            var persisted = await reportStore.ListAsync();
            var report = Assert.Single(persisted);
            Assert.Equal("char-a", report.ReporterGuid);
            Assert.Equal("Ayla", report.ReporterName);
            Assert.Equal("char-b", report.TargetGuid);
            Assert.Equal("Borin", report.TargetName);
            Assert.Equal("spamming trade chat", report.Reason);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ReportingAPlayer_SurvivesARestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-reports-restart-{Guid.NewGuid():N}");
        try
        {
            var reportStore = new FileReportStore(directory);
            var characters = new InMemoryCharacterStore(
            [
                Character("char-a", 1, "Ayla"),
                Character("char-b", 2, "Borin"),
            ]);

            var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, reportStore: reportStore);
            await LoginAsync(session, "developer", "Ayla");
            await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.SocialReport, 2, new ReportPlayerPayload("player:char-b", "griefing at the fountain"))));

            var restartedStore = new FileReportStore(directory);
            var persisted = await restartedStore.ListAsync();
            var report = Assert.Single(persisted);
            Assert.Equal("griefing at the fountain", report.Reason);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ReportingYourself_IsRefusedAndNothingIsPersisted()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-reports-self-{Guid.NewGuid():N}");
        try
        {
            var reportStore = new FileReportStore(directory);
            var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
            var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, reportStore: reportStore);
            await LoginAsync(session, "developer", "Ayla");

            var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.SocialReport, 2, new ReportPlayerPayload("player:char-a", "self report"))));

            Assert.Contains(responses, r => r.Contains("\"type\":\"protocol.error\""));
            Assert.Empty(await reportStore.ListAsync());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ReportingWithAnEmptyReason_IsRefused()
    {
        var characters = new InMemoryCharacterStore(
        [
            Character("char-a", 1, "Ayla"),
            Character("char-b", 2, "Borin"),
        ]);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters);
        await LoginAsync(session, "developer", "Ayla");

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialReport, 2, new ReportPlayerPayload("player:char-b", "   "))));

        Assert.Contains(responses, r => r.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task ReportingAnUnknownTarget_IsRefused()
    {
        var characters = new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]);
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters);
        await LoginAsync(session, "developer", "Ayla");

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialReport, 2, new ReportPlayerPayload("player:char-nobody", "griefing"))));

        Assert.Contains(responses, r => r.Contains("\"type\":\"protocol.error\""));
    }

    [Fact]
    public async Task ReportingAPlayer_NeverNotifiesTheTarget()
    {
        var characters = new InMemoryCharacterStore(
        [
            Character("char-a", 1, "Ayla"),
            Character("char-b", 2, "Borin"),
        ]);
        var ayla = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"), characters);
        await LoginAsync(ayla, "developer", "Ayla");

        var responses = await ayla.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SocialReport, 2, new ReportPlayerPayload("player:char-b", "griefing"))));

        var reply = Assert.Single(responses);
        Assert.Contains("\"type\":\"social.reportAcknowledged\"", reply);
    }

    private static async Task LoginAsync(PlaySession session, string userId, string characterName)
    {
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest(userId, "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest(characterName))));
    }

    private static CharacterRecord Character(string guid, int accountId, string name) => new(
        guid, accountId, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, [100, 200]);
}
