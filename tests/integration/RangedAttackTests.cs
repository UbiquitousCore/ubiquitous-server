using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class RangedAttackTests
{
    private static readonly IReadOnlyList<int> WithBow =
        [.. EquipmentSlots.Build((EquipmentSlots.MainHand, 1103), (EquipmentSlots.Ranged, 4149))];

    private static readonly IReadOnlyList<int> WithoutBow =
        [.. EquipmentSlots.Build((EquipmentSlots.MainHand, 1103))];

    [Fact]
    public async Task AttackStart_AcceptsADistantTargetWhenABowIsWorn()
    {
        var session = await SessionAsync(At(12), WithBow);

        var response = await AttackAsync(session);

        Assert.Contains(response, message => message.Contains("attack.meleeStart"));
    }

    [Fact]
    public async Task AttackStart_StillRefusesADistantTargetWithNoBow()
    {
        var session = await SessionAsync(At(12), WithoutBow);

        Assert.Contains("invalid_target", Assert.Single(await AttackAsync(session)));
    }

    [Fact]
    public async Task AttackStart_RefusesATargetBeyondTheRangedMaximumEvenWithABow()
    {
        var session = await SessionAsync(At((float)CombatService.MaxRangedAttackDistance + 5), WithBow);

        Assert.Contains("invalid_target", Assert.Single(await AttackAsync(session)));
    }

    [Fact]
    public async Task TickCombat_SwitchesToRangedOnTheFirstTickAfterADistantStart()
    {
        var session = await SessionAsync(At(12), WithBow);
        await AttackAsync(session);

        var responses = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.Contains(responses, message => message.Contains("attack.rangeStart"));
    }

    [Fact]
    public async Task TickCombat_StaysMeleeWithNoBowHowEverFarAway()
    {
        var session = await SessionAsync(At(2), WithoutBow);
        await AttackAsync(session);
        await StepToDistanceAsync(session, creatureAt: 2, distance: 12, moving: false);

        var responses = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.DoesNotContain(responses, message => message.Contains("attack.rangeStart"));
    }

    [Fact]
    public async Task TickCombat_WalkingAwayMidFightStartsShooting()
    {
        var session = await SessionAsync(At(2), WithBow);
        await AttackAsync(session);
        await StepToDistanceAsync(session, creatureAt: 2, distance: 12, moving: false);

        var responses = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.Contains(responses, message => message.Contains("attack.rangeStart"));
    }

    [Fact]
    public async Task TickCombat_WalkingBackInDrawsTheMeleeWeaponAgain()
    {
        var session = await SessionAsync(At(12), WithBow);
        await AttackAsync(session);
        session.TickCombat(DateTimeOffset.UtcNow);
        await StepToDistanceAsync(session, creatureAt: 12, distance: 2, moving: false);

        var responses = session.TickCombat(DateTimeOffset.UtcNow);

        Assert.Contains(responses, message => message.Contains("attack.meleeStart"));
    }

    [Fact]
    public async Task TickCombat_LaunchesAShotOnceTheRangedIntervalElapses()
    {
        var session = await SessionAsync(At(12), WithBow);
        await AttackAsync(session);
        var start = DateTimeOffset.UtcNow;
        session.TickCombat(start);

        var responses = session.TickCombat(start + CombatService.SwingInterval);

        var launch = Assert.Single(responses, message => message.Contains("attack.shotFired"));
        Assert.Contains($"\"spellVisualId\":{CombatService.RangedAttackSpellVisualId}", launch);
        Assert.Contains("\"speed\":30", launch);
    }

    [Fact]
    public async Task TickCombat_AShotLandsAsARangeHitOnceItsFlightIsOver()
    {
        var session = await SessionAsync(At(12), WithBow);
        await AttackAsync(session);
        var start = DateTimeOffset.UtcNow;
        session.TickCombat(start);
        var launchedAt = start + CombatService.SwingInterval;
        session.TickCombat(launchedAt);

        var landed = session.TickCombat(launchedAt + TimeSpan.FromSeconds(0.5));

        var hit = Assert.Single(landed, message => message.Contains("attack.shotResult"));
        Assert.Contains("\"attackerId\":\"player:char-a\"", hit);
        Assert.Contains("\"targetId\":\"Creature:1\"", hit);
    }

    [Fact]
    public async Task TickCombat_HoldsFireWhileThePlayerIsMoving()
    {
        var session = await SessionAsync(At(2), WithBow);
        await AttackAsync(session);
        await StepToDistanceAsync(session, creatureAt: 2, distance: 12, moving: true);
        var start = DateTimeOffset.UtcNow;

        Assert.Contains(session.TickCombat(start), message => message.Contains("attack.rangeStart"));
        Assert.DoesNotContain(
            session.TickCombat(start + CombatService.SwingInterval),
            message => message.Contains("attack.shotFired"));
    }

    private static CreatureSpawnDefinition At(float distance) =>
        new(1001, 13, 9001, PlayerStartX + distance, 2, 3, 0);

    private const float PlayerStartX = 1;

    private static async Task<IReadOnlyList<string>> AttackAsync(PlaySession session) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

    private static async Task StepToDistanceAsync(
        PlaySession session, float creatureAt, float distance, bool moving) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.PlayerMove, 3,
            new PlayerMovePayload(PlayerStartX + creatureAt + distance, 2, 3, 0, moving, MovementState.Walking))));

    private static async Task<PlaySession> SessionAsync(
        CreatureSpawnDefinition spawn, IReadOnlyList<int> equipment)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, PlayerClassIds.Ranger, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, equipment)]),
            [spawn]);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
