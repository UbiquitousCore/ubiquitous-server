using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PatrolEditingTests : IDisposable
{
    private const string TwoStops = """
        {"mapId": 13, "routes": [{"id": 4541, "stops": [
          {"x": 1.5, "y": 2, "z": 3, "pauseSeconds": 6},
          {"x": 10, "y": 2, "z": 3, "pauseSeconds": 0}]}]}
        """;

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ubiquitous-patrols-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public async Task AnAdmin_SavesARouteFile_AndTheServerWalksItAtOnce()
    {
        var library = PatrolRouteLibrary.Load(_directory.FullName);
        var session = await SessionAsync(admin: true, library);

        var saved = await SendAsync(session, WebMessageTypes.PatrolSave, new { fileName = "greenmont.json", text = TwoStops });

        Assert.Contains(saved, m => m.Contains("\"type\":\"patrol.saved\"") && m.Contains("\"routes\":1"));
        Assert.True(library.TryGet(4541, out var route));
        Assert.Equal(6000, route.Stops[0].PauseMilliseconds);
        var written = PatrolRouteFiles.LoadDirectory(_directory.FullName);
        Assert.Equal(1.5f, Assert.Single(written).Value.Stops[0].X);
    }

    [Fact]
    public async Task TheSavedFile_IsWhatTheServerSendsBackForThatMap()
    {
        var library = PatrolRouteLibrary.Load(_directory.FullName);
        var session = await SessionAsync(admin: true, library);
        await SendAsync(session, WebMessageTypes.PatrolSave, new { fileName = "greenmont.json", text = TwoStops });

        var file = await SendAsync(session, WebMessageTypes.PatrolRequest, new { });

        var reply = Assert.Single(file, m => m.Contains("\"type\":\"patrol.file\""));
        Assert.Contains("\"fileName\":\"greenmont.json\"", reply);
        Assert.Contains("4541", reply);
    }

    [Fact]
    public async Task AMapWithNoFile_IsAnsweredWithNone()
    {
        var session = await SessionAsync(admin: true, PatrolRouteLibrary.Load(_directory.FullName));

        var file = await SendAsync(session, WebMessageTypes.PatrolRequest, new { });

        var reply = Assert.Single(file, m => m.Contains("\"type\":\"patrol.file\""));
        Assert.DoesNotContain("\"fileName\":\"", reply);
        Assert.Contains("\"mapId\":13", reply);
    }

    [Fact]
    public async Task APlayerWhoIsNotAnAdmin_IsRefused()
    {
        var library = PatrolRouteLibrary.Load(_directory.FullName);
        var session = await SessionAsync(admin: false, library);

        var refused = await SendAsync(session, WebMessageTypes.PatrolSave, new { fileName = "greenmont.json", text = TwoStops });

        Assert.Contains(refused, m => m.Contains("\"forbidden\""));
        Assert.False(library.TryGet(4541, out _));
        Assert.Empty(_directory.GetFiles());
    }

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("Greenmont.json")]
    [InlineData("greenmont.txt")]
    public async Task AFileNameThatIsNotAPlainLowerCaseName_IsRefused(string fileName)
    {
        var session = await SessionAsync(admin: true, PatrolRouteLibrary.Load(_directory.FullName));

        var refused = await SendAsync(session, WebMessageTypes.PatrolSave, new { fileName, text = TwoStops });

        Assert.Contains(refused, m => m.Contains("\"patrol_refused\""));
        Assert.Empty(_directory.GetFiles());
    }

    [Fact]
    public async Task ARouteTheServerWouldNotRead_IsRefused()
    {
        var session = await SessionAsync(admin: true, PatrolRouteLibrary.Load(_directory.FullName));
        const string oneStop = """{"mapId": 13, "routes": [{"id": 9, "stops": [{"x": 0, "y": 0, "z": 0, "pauseSeconds": 0}]}]}""";

        var refused = await SendAsync(session, WebMessageTypes.PatrolSave, new { fileName = "greenmont.json", text = oneStop });

        Assert.Contains(refused, m => m.Contains("\"patrol_refused\"") && m.Contains("at least 2 stops"));
    }

    [Fact]
    public void ARouteAlreadyPlacedInAnotherFile_IsRefused()
    {
        var library = PatrolRouteLibrary.Load(_directory.FullName);
        library.Save("greenmont.json", TwoStops);

        var again = library.Save("elsewhere.json", TwoStops);

        Assert.Equal("Route 4541 is already placed in greenmont.json.", again.Refusal);
    }

    [Fact]
    public void ASavedFile_IsReadBackTheSameOnTheNextStart()
    {
        PatrolRouteLibrary.Load(_directory.FullName).Save("greenmont.json", TwoStops);

        var restarted = PatrolRouteLibrary.Load(_directory.FullName);

        Assert.True(restarted.TryGet(4541, out var route));
        Assert.Equal(2, route.Stops.Count);
    }

    private static async Task<PlaySession> SessionAsync(bool admin, PatrolRouteLibrary patrols)
    {
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret", isAdmin: admin),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [], content: GameContent.Development, patrols: patrols);
        await SendAsync(session, WebMessageTypes.AccountLogin, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, new ChooseCharacterRequest("Ayla"));
        return session;
    }

    private static Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, object payload) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 1, payload)));
}
