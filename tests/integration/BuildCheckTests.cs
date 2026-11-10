using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class BuildCheckTests
{
    private static readonly DataBuild Build = new("1.1.4", new Dictionary<string, TableShape>
    {
        ["items"] = new("items", 5034, "uuuk"),
        ["zones"] = new("zones", 240, "iiiu"),
    });

    private static async Task<JsonElement> SignInAsync(DataBuild build, IReadOnlyList<ClientTableShape>? tables)
    {
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret"), new InMemoryCharacterStore([]), build: build);
        var replies = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret", tables))));
        return JsonDocument.Parse(Assert.Single(replies)).RootElement.GetProperty("payload");
    }

    [Fact]
    public async Task SigningInNamesTheBuildTheServerRuns()
    {
        var payload = await SignInAsync(Build, [new("items", 5034, "uuuk")]);

        Assert.True(payload.GetProperty("ok").GetBoolean());
        Assert.Equal("1.1.4", payload.GetProperty("gameVersion").GetString());
        Assert.False(payload.TryGetProperty("differingTables", out var differing)
            && differing.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task APlayerOnAnotherBuildIsToldWhichTablesDiffer()
    {
        var payload = await SignInAsync(Build, [new("tables/items.tbl", 6515, "uuuk"), new("zones", 240, "iiiu")]);

        Assert.True(payload.GetProperty("ok").GetBoolean());
        Assert.Equal(["items"], payload.GetProperty("differingTables").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public async Task AClientThatSendsNoTablesStillSignsIn()
    {
        var payload = await SignInAsync(Build, tables: null);

        Assert.True(payload.GetProperty("ok").GetBoolean());
    }
}
