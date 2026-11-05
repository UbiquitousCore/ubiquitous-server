using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class SneakOpenerTests
{
    private const uint Hide = 980;
    private const uint Opener = 981;
    private const uint Stun = 982;

    [Fact]
    public async Task AnOpenerFromSneak_StunsItsTarget()
    {
        var session = await SessionAsync();
        await SendAsync(session, WebMessageTypes.SpellRequest, 2, new { spellId = Hide, targetId = "player:char-a" });

        var strike = await SendAsync(session, WebMessageTypes.SpellRequest, 3, new { spellId = Opener, targetId = "Creature:1" });

        Assert.Contains(strike, m => m.Contains("\"type\":\"aura.ended\"") && m.Contains($"\"spellId\":{Hide}"));
        Assert.Contains(strike, m => m.Contains("\"type\":\"aura.applied\"")
            && m.Contains($"\"spellId\":{Stun}") && m.Contains("\"unitId\":\"Creature:1\""));
    }

    [Fact]
    public async Task TheSameOpenerInPlainSight_DoesNotStun()
    {
        var session = await SessionAsync();

        var strike = await SendAsync(session, WebMessageTypes.SpellRequest, 2, new { spellId = Opener, targetId = "Creature:1" });

        Assert.Contains(strike, m => m.Contains("\"type\":\"spell.outcome\"") && m.Contains($"\"spellId\":{Opener}"));
        Assert.DoesNotContain(strike, m => m.Contains($"\"spellId\":{Stun}"));
    }

    private static readonly GameContent Content = GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9500] = new(9500, "wolf", 3, 1000, MinDamage: 1, MaxDamage: 2),
        },
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [Hide] = new(Hide, "spell-980", 0, 0, 0f, PowerCost: 0, AuraStealth: true, DurationMs: 60_000),
            [Opener] = new(Opener, "spell-981", 0, 0, 30f, PowerCost: 0, EffectDamage: 5,
                EffectTarget: SpellEffectTargets.SingleEnemy,
                EffectFromHidingTriggerSpellId: Stun, EffectFromHidingTriggerChancePercent: 100),
            [Stun] = new(Stun, "spell-982", 0, 0, 30f, AuraStun: true, DurationMs: 4000,
                EffectTarget: SpellEffectTargets.SingleEnemy),
        },
        StartingSpells = [.. GameContent.Development.StartingSpells, Hide, Opener],
    };

    private static async Task<PlaySession> SessionAsync()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new(1001, 13, 9500, 1, 2, 12, 0)],
            content: Content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9, spellRollUnit: () => 0.5);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        return session;
    }

    private static Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, int sequence, object payload) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload)));
}
