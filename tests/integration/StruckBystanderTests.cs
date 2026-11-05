using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class StruckBystanderTests
{
    private const uint NukeSpellId = 108;
    private const uint DotSpellId = 107;

    [Theory]
    [InlineData(NukeSpellId)]
    [InlineData(DotSpellId)]
    public async Task ASpellOnASecondCreature_WhileFightingTheFirst_TurnsItOnThePlayer(uint spellId)
    {
        var session = await SessionAsync();
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 4; i++) session.TickCombat(now += TimeSpan.FromMilliseconds(500));

        var cast = await SendAsync(session, WebMessageTypes.SpellRequest, 3, new { spellId, targetId = "Creature:2" });
        var heard = cast.ToList();
        var secondStruck = false;
        for (var i = 0; i < 40 && !secondStruck; i++)
        {
            var tick = session.TickCombat(now += TimeSpan.FromMilliseconds(250));
            heard.AddRange(tick);
            secondStruck = tick.Any(m => m.Contains("\"type\":\"attack.swingResult\"")
                && m.Contains("\"attackerId\":\"Creature:2\""));
        }

        Assert.Contains(heard, m => m.Contains("\"type\":\"attack.meleeStart\"")
            && m.Contains("\"attackerId\":\"Creature:2\""));
        Assert.True(secondStruck, "expected the second creature to strike the player who hit it");
    }

    private static readonly GameContent Content = GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9500] = new(9500, "wolf", 3, 1000, MinDamage: 1, MaxDamage: 2, FactionType: UnitFactionTypes.Hostile),
        },
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [DotSpellId] = new(DotSpellId, "spell-107", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                AuraPeriodicDamage: 6),
            [NukeSpellId] = new(NukeSpellId, "spell-108", CastTimeMs: 0, CooldownMs: 0, MaxRange: 30f, PowerCost: 5,
                EffectDamage: 50),
        },
        StartingSpells = [.. GameContent.Development.StartingSpells, DotSpellId, NukeSpellId],
    };

    private static async Task<PlaySession> SessionAsync()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, [100, 200])]),
            [new(1001, 13, 9500, 2, 2, 4, 0), new(1002, 13, 9500, 1, 2, 15, 0)],
            content: Content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9, spellRollUnit: () => 1.0);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        return session;
    }

    private static Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, int sequence, object payload) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, sequence, payload)));
}
