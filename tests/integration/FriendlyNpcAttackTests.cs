using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class FriendlyNpcAttackTests
{
    [Fact]
    public async Task AttackStart_RefusesATownsfolkNpc()
    {
        var session = await SessionAsync(UnitFactionTypes.Townsfolk);

        var response = await AttackAsync(session);

        Assert.Contains("invalid_target", Assert.Single(response));
    }

    [Fact]
    public async Task AttackStart_RefusesATownsfolkNpcEvenWithNoServiceFlags()
    {
        var session = await SessionAsync(UnitFactionTypes.Townsfolk, npcFlags: 0);

        var response = await AttackAsync(session);

        Assert.Contains("invalid_target", Assert.Single(response));
    }

    [Fact]
    public async Task AttackStart_AcceptsAHostileMob()
    {
        var session = await SessionAsync(UnitFactionTypes.Hostile);

        var response = await AttackAsync(session);

        Assert.Contains(response, message => message.Contains("attack.meleeStart"));
    }

    [Fact]
    public async Task AttackStart_AcceptsANeutralTrainingDummy()
    {
        var session = await SessionAsync(UnitFactionTypes.Neutral);

        var response = await AttackAsync(session);

        Assert.Contains(response, message => message.Contains("attack.meleeStart"));
    }

    [Fact]
    public async Task AttackStart_AcceptsACreatureCarryingNoFactionAtAll()
    {
        var session = await SessionAsync(factionType: null);

        var response = await AttackAsync(session);

        Assert.Contains(response, message => message.Contains("attack.meleeStart"));
    }

    [Fact]
    public async Task SpellRequest_RefusesAHarmfulSpellAtATownsfolkNpc()
    {
        var session = await SessionAsync(UnitFactionTypes.Townsfolk);

        var response = await CastAsync(session);

        var reply = Assert.Single(response);
        Assert.Contains("\"type\":\"spell.failed\"", reply);
        Assert.Contains($"\"{WireCodes.SpellRefusal.InvalidTarget}\"", reply);
    }

    [Fact]
    public async Task SpellRequest_CastsAHarmfulSpellAtAHostileMob()
    {
        var session = await SessionAsync(UnitFactionTypes.Hostile);

        var response = await CastAsync(session);

        Assert.Contains("\"type\":\"spell.casting\"", Assert.Single(response));
    }

    [Fact]
    public async Task SpellRequest_RefusesALeapStrikeAtATownsfolkNpc()
    {
        const uint charge = 1500;
        var session = await SessionAsync(UnitFactionTypes.Townsfolk, spells: new Dictionary<uint, SpellDefinition>
        {
            [charge] = new(charge, "spell-1500", 0, 1000, 15f, PowerCost: 0,
                EffectTarget: SpellEffectTargets.SingleEnemy, EffectLeapToTarget: true,
                ExtraEffects: [new(charge, "spell-1500", 0, 0, 15f, EffectDamage: 5,
                    EffectTarget: SpellEffectTargets.SingleEnemy)]),
        }, startingSpells: [charge]);

        var response = await CastAsync(session, charge);

        var reply = Assert.Single(response);
        Assert.Contains("\"type\":\"spell.failed\"", reply);
        Assert.Contains($"\"{WireCodes.SpellRefusal.InvalidTarget}\"", reply);
        Assert.DoesNotContain(response, message => message.Contains("move.accepted"));
        Assert.DoesNotContain(response, message => message.Contains("attack.enterCombat"));
    }

    [Fact]
    public async Task SpellRequest_RefusesAMeleeWeaponHitAtATownsfolkNpc()
    {
        const uint swiftStrike = 1501;
        var session = await SessionAsync(UnitFactionTypes.Townsfolk, spells: new Dictionary<uint, SpellDefinition>
        {
            [swiftStrike] = new(swiftStrike, "spell-1501", 0, 0, 5f, PowerCost: 0,
                EffectMeleeWeaponHit: true),
        }, startingSpells: [swiftStrike]);

        var response = await CastAsync(session, swiftStrike);

        var reply = Assert.Single(response);
        Assert.Contains("\"type\":\"spell.failed\"", reply);
        Assert.Contains($"\"{WireCodes.SpellRefusal.InvalidTarget}\"", reply);
    }

    [Fact]
    public async Task SpellRequest_RefusesAStunAtATownsfolkNpc()
    {
        const uint stun = 1502;
        var session = await SessionAsync(UnitFactionTypes.Townsfolk, spells: new Dictionary<uint, SpellDefinition>
        {
            [stun] = new(stun, "spell-1502", 0, 0, 30f, PowerCost: 0, AuraStun: true, DurationMs: 4000),
        }, startingSpells: [stun]);

        var response = await CastAsync(session, stun);

        var reply = Assert.Single(response);
        Assert.Contains("\"type\":\"spell.failed\"", reply);
        Assert.Contains($"\"{WireCodes.SpellRefusal.InvalidTarget}\"", reply);
    }

    [Fact]
    public async Task SpellRequest_RefusesAHitCarriedAsAFurtherEffectAtATownsfolkNpc()
    {
        const uint strike = 1503;
        var session = await SessionAsync(UnitFactionTypes.Townsfolk, spells: new Dictionary<uint, SpellDefinition>
        {
            [strike] = new(strike, "spell-1503", 0, 0, 5f, PowerCost: 0,
                EffectTarget: SpellEffectTargets.SingleEnemy,
                ExtraEffects: [new(strike, "spell-1503", 0, 0, 5f, EffectDamage: 5,
                    EffectTarget: SpellEffectTargets.SingleEnemy)]),
        }, startingSpells: [strike]);

        var response = await CastAsync(session, strike);

        var reply = Assert.Single(response);
        Assert.Contains("\"type\":\"spell.failed\"", reply);
        Assert.Contains($"\"{WireCodes.SpellRefusal.InvalidTarget}\"", reply);
    }

    private static async Task<IReadOnlyList<string>> CastAsync(PlaySession session, uint spellId = 100) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 2, new { spellId, targetId = "Creature:1" })));

    private static async Task<IReadOnlyList<string>> AttackAsync(PlaySession session) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" })));

    private static async Task<PlaySession> SessionAsync(int? factionType, uint npcFlags = 0,
        Dictionary<uint, SpellDefinition>? spells = null, uint[]? startingSpells = null)
    {
        var content = CreatureAggroTests.ContentWithCreature(new CreatureDefinition(
            9001, "creature-9001", 3, 100, RawNpcFlags: npcFlags, FactionType: factionType));
        if (spells is not null)
        {
            var merged = new Dictionary<uint, SpellDefinition>(content.Spells);
            foreach (var (id, spell) in spells) merged[id] = spell;
            content = content with
            {
                Spells = merged,
                StartingSpells = startingSpells ?? content.StartingSpells,
            };
        }
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new CreatureSpawnDefinition(1001, 13, 9001, 2, 2, 4, 0)], content: content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }
}
