using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class RapidShotTests
{
    private const uint RapidShot = 5692;
    private const float PlayerStartX = 1;

    private static readonly IReadOnlyList<int> WithBow =
        [.. EquipmentSlots.Build((EquipmentSlots.MainHand, 1103), (EquipmentSlots.Ranged, 4149))];

    private static readonly IReadOnlyList<int> WithoutBow =
        [.. EquipmentSlots.Build((EquipmentSlots.MainHand, 1103))];

    [Fact]
    public async Task Cast_IsRefusedWithNothingInTheRangedSlot()
    {
        var session = await SessionAsync(At(12), WithoutBow);

        var response = Assert.Single(await CastAsync(session));

        Assert.Contains("\"type\":\"spell.failed\"", response);
        Assert.Contains($"\"spellId\":{RapidShot}", response);
        Assert.Contains("\"reason\":\"missingEquipment\"", response);
    }

    [Fact]
    public async Task Cast_IsRefusedBeyondTwentyFiveUnits()
    {
        var session = await SessionAsync(At((float)CombatService.MaxRangedAttackDistance + 5), WithBow);

        var responses = await CastAsync(session);

        Assert.Contains(responses, message => message.Contains("\"type\":\"spell.failed\"")
            && message.Contains("\"reason\":\"outOfRange\""));
        Assert.DoesNotContain(responses, message => message.Contains("attack.shotFired"));
    }

    [Fact]
    public async Task Cast_WithABowStartsTheCastBar()
    {
        var session = await SessionAsync(At(12), WithBow);

        var response = Assert.Single(await CastAsync(session));

        Assert.Contains("\"type\":\"spell.casting\"", response);
        Assert.Contains("\"castTime\":500", response);
    }

    [Fact]
    public async Task TickCombat_TheResolvedCastLoosesAnArrow()
    {
        var session = await SessionAsync(At(12), WithBow);
        var start = DateTimeOffset.UtcNow;
        await CastAsync(session);

        var responses = session.TickCombat(start + TimeSpan.FromSeconds(1));

        Assert.Contains(responses, message => message.Contains("\"type\":\"spell.released\"")
            && message.Contains($"\"spellId\":{RapidShot}"));
        var launch = Assert.Single(responses, message => message.Contains("attack.shotFired"));
        Assert.Contains("\"speed\":30", launch);
        Assert.Contains(responses, message => message.Contains("\"type\":\"spell.cooldownStarted\""));
    }

    [Fact]
    public async Task TickCombat_TheArrowLandsAsARangedHit()
    {
        var session = await SessionAsync(At(12), WithBow);
        var start = DateTimeOffset.UtcNow;
        await CastAsync(session);
        var resolvedAt = start + TimeSpan.FromSeconds(1);
        session.TickCombat(resolvedAt);

        var landed = session.TickCombat(resolvedAt + TimeSpan.FromSeconds(1));

        var hit = Assert.Single(landed, message => message.Contains("attack.shotResult"));
        Assert.Contains("\"attackerId\":\"player:char-a\"", hit);
        Assert.Contains("\"targetId\":\"Creature:1\"", hit);
    }

    [Fact]
    public async Task TickCombat_CastingRapidShotCompletesTheCastObjective()
    {
        var session = await SessionAsync(At(8), WithBow, CastObjectiveContent());
        var accepted = Assert.Single(await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(WebMessageTypes.QuestAccept, 2,
                new { networkId = "Creature:1", questId = 5001u }))));
        Assert.Contains("\"readyToHandIn\":false", accepted);

        var start = DateTimeOffset.UtcNow;
        await CastAsync(session);
        var resolved = session.TickCombat(start + TimeSpan.FromSeconds(1));

        var update = Assert.Single(resolved, message => message.Contains("\"type\":\"quest.updated\""));
        Assert.Contains("\"kind\":\"triggered\"", update);
        Assert.Contains("\"count\":1", update);
        Assert.Contains("\"readyToHandIn\":true", update);

        var complete = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestComplete, 9,
            new { networkId = "Creature:1", questId = 5001u, rewardIndex = 0 })));
        Assert.Contains(complete, message => message.Contains("\"status\":\"complete\""));
    }

    [Fact]
    public async Task TickCombat_ARefusedCastCreditsNothing()
    {
        var session = await SessionAsync(At(12), WithoutBow, CastObjectiveContent());
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.QuestAccept, 2, new { networkId = "Creature:1", questId = 5001u })));

        var start = DateTimeOffset.UtcNow;
        var refused = await CastAsync(session);
        var ticked = session.TickCombat(start + TimeSpan.FromSeconds(1));

        Assert.DoesNotContain(refused.Concat(ticked), message => message.Contains("\"type\":\"quest.updated\""));
    }

    [Fact]
    public void DevelopmentContent_CarriesAQuestWhoseObjectiveIsTheSpellCast()
    {
        var quest = Assert.Single(GameContent.Development.Quests, candidate => candidate.Id == 5014);

        var objective = Assert.Single(quest.Objectives);
        Assert.Equal(QuestObjectiveKind.Triggered, objective.Kind);
        Assert.Equal(1, objective.GoalCount);
        Assert.Equal(RapidShot, objective.ObjectiveSpell);
        Assert.Equal(0u, objective.ObjectiveCreature);
        Assert.Equal(0u, objective.ObjectiveItem);
        Assert.Equal(0u, objective.ObjectiveObject);
    }

    private static GameContent CastObjectiveContent() => GameContent.Development with
    {
        Quests =
        [
            new QuestDefinition(5001, 9001, 12001, 12002, 1,
                [new QuestObjectiveDefinition(
                    0, QuestObjectiveKind.Triggered, GoalCount: 1,
                    ObjectiveStringId: 12020, ObjectiveSpell: RapidShot)]),
        ],
    };

    private static CreatureSpawnDefinition At(float distance) =>
        new(1001, 13, 9001, PlayerStartX + distance, 2, 3, 0);

    private static async Task<IReadOnlyList<string>> CastAsync(PlaySession session) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 5, new { spellId = RapidShot, targetId = "Creature:1" })));

    private static Task<PlaySession> SessionAsync(
        CreatureSpawnDefinition spawn, IReadOnlyList<int> equipment) =>
        SessionAsync(spawn, equipment, GameContent.Development);

    private static async Task<PlaySession> SessionAsync(
        CreatureSpawnDefinition spawn, IReadOnlyList<int> equipment, GameContent content)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, PlayerClassIds.Ranger, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, equipment)]),
            [spawn], null, content, spellRollUnit: () => 1.0, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
