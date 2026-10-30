using System.Text.Json;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class FallDamageTests
{
    private const float Top = 60f;

    private static readonly CharacterRecord Character = new(
        "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 2, Top, 2, 4, [100, 200]);

    private static async Task<(PlaySession Session, int MaxHealth)> EnterAsync()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character]), developmentEntry: true);
        var entry = await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.DevEnterWorld, 0, new { characterRef = Character.Guid })));
        var snapshot = Assert.Single(entry, m => m.Contains("\"type\":\"player.updated\""));
        var maxHealth = System.Text.RegularExpressions.Regex.Match(snapshot, "\"maxHealth\":(\\d+)");
        Assert.True(maxHealth.Success);
        return (session, int.Parse(maxHealth.Groups[1].Value));
    }

    private static int _sequence = 1;

    private static Task<IReadOnlyList<string>> Move(PlaySession session, float y, MovementState state) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, Interlocked.Increment(ref _sequence),
            new PlayerMovePayload(2, y, 2, 0, state != MovementState.Standing, state))));

    // Down in steps the server accepts, ending in `last` at `bottom`.
    private static async Task<IReadOnlyList<string>> FallTo(
        PlaySession session, float bottom, MovementState first, MovementState last)
    {
        await Move(session, Top, first);
        for (var y = Top - 10f; y > bottom; y -= 10f) await Move(session, y, MovementState.Falling);
        return await Move(session, bottom, last);
    }

    private static JsonElement FallOutcome(IReadOnlyList<string> replies)
    {
        var outcome = Assert.Single(replies, m => m.Contains("\"type\":\"spell.outcome\""));
        using var document = JsonDocument.Parse(outcome);
        var payload = document.RootElement.GetProperty("payload").Clone();
        Assert.Equal(WireCodes.Outcome.Fall, payload.GetProperty("result").GetString());
        return payload;
    }

    [Fact]
    public async Task AJumpThatLandsFarBelowCostsHealthByTheHeightFallen()
    {
        var (session, maxHealth) = await EnterAsync();

        var replies = await FallTo(session, Top - 15f, MovementState.Jumping, MovementState.Landed);

        var expected = FallDamage.DamageFor(FallDamage.LandingSpeed(15f), maxHealth);
        Assert.True(expected > 0);
        var outcome = FallOutcome(replies);
        Assert.Equal(expected, outcome.GetProperty("damage").GetInt32());
        Assert.Equal(maxHealth - expected, outcome.GetProperty("finalHealth").GetInt32());
        Assert.DoesNotContain(replies, m => m.Contains("\"type\":\"unit.died\""));
    }

    [Fact]
    public async Task AJumpFromHighEnoughKillsAndIsADeathToNoOne()
    {
        var (session, _) = await EnterAsync();

        var replies = await FallTo(session, Top - 30f, MovementState.Jumping, MovementState.LandedMoving);

        Assert.Equal(0, FallOutcome(replies).GetProperty("finalHealth").GetInt32());
        Assert.Contains(replies, m => m.Contains("\"type\":\"unit.died\""));
        Assert.Contains(replies, m => m.Contains("\"type\":\"player.death\""));
    }

    [Fact]
    public async Task ALowJumpIsHarmless()
    {
        var (session, _) = await EnterAsync();

        var replies = await FallTo(session, Top - 8f, MovementState.Jumping, MovementState.Landed);

        Assert.DoesNotContain(replies, m => m.Contains("\"type\":\"spell.outcome\""));
    }

    [Fact]
    public async Task WalkingOffALedgeNeverHurts()
    {
        var (session, _) = await EnterAsync();

        var replies = await FallTo(session, Top - 30f, MovementState.Falling, MovementState.Landed);

        Assert.DoesNotContain(replies, m => m.Contains("\"type\":\"spell.outcome\""));
    }

    [Fact]
    public async Task AJumpThatEndsInWaterIsHarmless()
    {
        var (session, _) = await EnterAsync();

        var replies = await FallTo(session, Top - 30f, MovementState.Jumping, MovementState.Swimming);
        replies = [.. replies, .. await Move(session, Top - 30f, MovementState.Landed)];

        Assert.DoesNotContain(replies, m => m.Contains("\"type\":\"spell.outcome\""));
    }
}
