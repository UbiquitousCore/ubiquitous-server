using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class AmbientTests
{
    private static readonly CharacterRecord Character = new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200]);

    [Fact]
    public async Task DevEnterWorld_SendsTheInventedDefaultAmbientStateWhenNothingSetOneYet()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), developmentEntry: true);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        var ambientMessage = Assert.Single(responses, m => m.Contains("\"type\":\"world.ambient\""));
        Assert.Contains("\"weather\":\"clear\"", ambientMessage);
        Assert.DoesNotContain("festival", ambientMessage);
    }

    [Fact]
    public async Task DevEnterWorld_SendsAStateAlreadySetOnTheEnteredMapBeforeTheSessionExisted()
    {
        var ambient = new AmbientService();
        ambient.SetState(13, new AmbientState("rain", "Harvest Festival"));
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), ambient: ambient, developmentEntry: true);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"world.ambient\"")
            && m.Contains("\"weather\":\"rain\"") && m.Contains("\"festival\":\"Harvest Festival\""));
    }

    [Fact]
    public async Task DevEnterWorld_ADifferentMapsAmbientStateNeverLeaksOntoThisOne()
    {
        var ambient = new AmbientService();
        ambient.SetState(20, new AmbientState("snow"));
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), ambient: ambient, developmentEntry: true);

        var responses = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = "char-a" })));

        Assert.Contains(responses, m => m.Contains("\"type\":\"world.ambient\"") && m.Contains("\"weather\":\"clear\""));
    }
}
