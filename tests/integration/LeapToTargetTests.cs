using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class LeapToTargetTests
{
    private const uint Charge = 1500;

    private static readonly CharacterRecord Warrior = new(
        "char-a", 1, "Ayla", 1, 2, PlayerClassIds.Warrior, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, []);

    private static readonly SpellDefinition LeapFirst = new(Charge, "spell-1500", 0, 1000, 15f, PowerCost: 0,
        EffectTarget: SpellEffectTargets.SingleEnemy, EffectLeapToTarget: true,
        ExtraEffects: [new(Charge, "spell-1500", 0, 0, 15f, EffectDamage: 5, EffectTarget: SpellEffectTargets.SingleEnemy)]);

    private static readonly SpellDefinition LeapAfterDamage = new(Charge, "spell-1500", 0, 1000, 25f, PowerCost: 0,
        EffectDamage: 5, EffectTarget: SpellEffectTargets.SingleEnemy,
        ExtraEffects: [new(Charge, "spell-1500", 0, 0, 25f, EffectTarget: SpellEffectTargets.SingleEnemy, EffectLeapToTarget: true)]);

    private static async Task<PlaySession> SessionAsync(float creatureX, SpellDefinition? spell = null)
    {
        spell ??= LeapFirst;
        var content = GameContent.Development with
        {
            Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells) { [Charge] = spell },
            StartingSpells = [.. GameContent.Development.StartingSpells, Charge],
        };
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Warrior]), [new CreatureSpawnDefinition(1001, 13, 9001, creatureX, 2, 3, 0)],
            null, content, spellRollUnit: () => 1.0, combatRollUnit: () => 1.0);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static Task<IReadOnlyList<string>> CastAsync(PlaySession session) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 5, new { spellId = Charge, targetId = "Creature:1" })));

    [Fact]
    public async Task ACastThatLeapsPutsTheCasterJustShortOfItsTarget()
    {
        var session = await SessionAsync(creatureX: 13);

        var cast = await CastAsync(session);

        var moved = Assert.Single(cast, m => m.Contains("\"type\":\"move.accepted\""));
        Assert.Contains("\"x\":11.5", moved);
        Assert.Equal(11.5f, session.PlayerPosition!.Value.X, 3);
        Assert.Equal(3f, session.PlayerPosition!.Value.Z, 3);
        Assert.Contains(cast, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"damage\":5"));
    }

    [Fact]
    public async Task ACasterAlreadyAtTheTargetStaysWhereTheyAre()
    {
        var session = await SessionAsync(creatureX: 2);

        var cast = await CastAsync(session);

        Assert.DoesNotContain(cast, m => m.Contains("\"type\":\"move.accepted\""));
        Assert.Equal(1f, session.PlayerPosition!.Value.X, 3);
    }

    [Fact]
    public async Task ALeapCarriedAsAFurtherEffectMovesTheCasterOnceTheSpellLands()
    {
        var session = await SessionAsync(creatureX: 13, LeapAfterDamage);

        var cast = await CastAsync(session);

        Assert.Contains(cast, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains("\"damage\":5"));
        Assert.Single(cast, m => m.Contains("\"type\":\"move.accepted\""));
        Assert.Equal(11.5f, session.PlayerPosition!.Value.X, 3);
    }
}
