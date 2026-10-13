using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class ShadowStrikeAndSneakTests
{
    private const uint ShadowStrike = 5815;
    private const uint Sneak = 5813;
    private const float PlayerStartX = 1;

    private static readonly IReadOnlyList<int> DualWielding =
        [.. EquipmentSlots.Build((EquipmentSlots.MainHand, 3907), (EquipmentSlots.OffHand, 3907))];

    private static readonly IReadOnlyList<int> BareHanded =
        [.. EquipmentSlots.Build()];

    [Fact]
    public async Task Cast_ShadowStrikeStartsWithNoCastBarAndSpendsItsPower()
    {
        var session = await SessionAsync(At(2), DualWielding);

        var responses = await CastAsync(session, ShadowStrike);

        Assert.Contains(responses, message => message.Contains("\"type\":\"spell.released\"")
            && message.Contains($"\"spellId\":{ShadowStrike}"));
        Assert.Contains(responses, message => message.Contains("\"type\":\"spell.cooldownStarted\"")
            && message.Contains("\"duration\":1500"));
    }

    [Fact]
    public async Task Cast_ShadowStrikeLandsAWeaponBlowOnTheTarget()
    {
        var session = await SessionAsync(At(2), DualWielding);

        var responses = await CastAsync(session, ShadowStrike);

        var log = Assert.Single(responses, message => message.Contains("\"type\":\"spell.outcome\""));
        Assert.Contains($"\"spellId\":{ShadowStrike}", log);
        Assert.Contains("\"targetId\":\"Creature:1\"", log);
        Assert.DoesNotContain(responses, message => message.Contains("attack.shotFired"));
        var flushed = session.TickCombat(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));
        Assert.Contains(flushed, message => message.Contains("\"type\":\"entity.updated\""));
    }

    [Fact]
    public async Task Cast_ShadowStrikeCarriesTheEffectsOwnPercentAndBonus()
    {
        var plain = GameContent.Development with
        {
            Spells = GameContent.Development.Spells.ToDictionary(
                entry => entry.Key,
                entry => entry.Key == ShadowStrike
                    ? entry.Value with { EffectMeleeWeaponDamagePercent = 100, EffectMeleeWeaponDamageBonus = 0 }
                    : entry.Value),
        };
        var baseline = DamageOf(await CastAsync(await SessionAsync(At(2), DualWielding, plain), ShadowStrike));

        var damage = DamageOf(await CastAsync(await SessionAsync(At(2), DualWielding), ShadowStrike));

        Assert.Equal(CombatService.ApplyWeaponDamageModifiers(baseline, 110, 10), damage);
    }

    [Fact]
    public async Task Cast_AnOffhandVariantWithNothingWornThereStillGoesOffButDealsNothing()
    {
        var offhandContent = GameContent.Development with
        {
            Spells = GameContent.Development.Spells.ToDictionary(
                entry => entry.Key,
                entry => entry.Key == ShadowStrike
                    ? entry.Value with { EffectMeleeWeaponSlot = EquipmentSlots.OffHand }
                    : entry.Value),
        };
        var session = await SessionAsync(At(2), BareHanded, offhandContent);

        var responses = await CastAsync(session, ShadowStrike);

        Assert.Contains(responses, message => message.Contains("\"type\":\"spell.released\""));
        Assert.Contains(responses, message => message.Contains("\"type\":\"spell.cooldownStarted\""));
        Assert.DoesNotContain(responses, message => message.Contains("\"type\":\"spell.outcome\""));
    }

    [Fact]
    public async Task Cast_SneakAppliesTheStealthAuraForFourteenSeconds()
    {
        var session = await SessionAsync(At(2), DualWielding);

        var responses = await CastAsync(session, Sneak);

        var aura = Assert.Single(responses, message => message.Contains("\"type\":\"aura.applied\""));
        Assert.Contains($"\"spellId\":{Sneak}", aura);
        Assert.Contains("\"unitId\":\"player:char-a\"", aura);
        Assert.Contains("\"maxDuration\":14000", aura);
    }

    [Fact]
    public async Task TickCombat_TheStealthAuraExpiresOnItsOwnDuration()
    {
        var session = await SessionAsync(At(2), DualWielding);
        var start = DateTimeOffset.UtcNow;
        await CastAsync(session, Sneak);

        Assert.DoesNotContain(
            session.TickCombat(start + TimeSpan.FromSeconds(13)),
            message => message.Contains("\"type\":\"aura.ended\""));

        var expired = session.TickCombat(start + TimeSpan.FromSeconds(15));

        Assert.Contains(expired, message => message.Contains("\"type\":\"aura.ended\"")
            && message.Contains($"\"spellId\":{Sneak}"));
    }

    [Fact]
    public async Task TickCombat_AStealthedPlayerIsNotPickedUpByTheCreatureAggroScan()
    {
        var session = await SessionAsync(AggroingCreatureAt(2), DualWielding, AggroContent());
        var start = DateTimeOffset.UtcNow;
        await CastAsync(session, Sneak);

        var stealthed = session.TickCombat(start + TimeSpan.FromSeconds(1));

        Assert.DoesNotContain(stealthed, message => message.Contains("\"type\":\"attack.meleeStart\""));

        var afterwards = session.TickCombat(start + TimeSpan.FromSeconds(15));

        Assert.Contains(afterwards, message => message.Contains("\"type\":\"attack.meleeStart\""));
    }

    private static int DamageOf(IReadOnlyList<string> responses)
    {
        var log = Assert.Single(responses, message => message.Contains("\"type\":\"spell.outcome\""));
        var marker = "\"damage\":";
        var at = log.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = at;
        while (end < log.Length && (char.IsDigit(log[end]) || log[end] == '-')) end++;
        return int.Parse(log[at..end]);
    }

    private static GameContent AggroContent()
    {
        var creatures = GameContent.Development.Creatures.ToDictionary(entry => entry.Key, entry => entry.Value);
        creatures[9002] = new CreatureDefinition(
            9002, "Aggressive Wolf", Level: 1, MaxHealth: 100, NoticeDistance: 20, FactionType: 0);
        return GameContent.Development with { Creatures = creatures };
    }

    private static CreatureSpawnDefinition At(float distance) =>
        new(1001, 13, 9001, PlayerStartX + distance, 2, 3, 0);

    private static CreatureSpawnDefinition AggroingCreatureAt(float distance) =>
        new(1001, 13, 9002, PlayerStartX + distance, 2, 3, 0);

    private static async Task<IReadOnlyList<string>> CastAsync(PlaySession session, uint spellId) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.SpellRequest, 5, new { spellId, targetId = "Creature:1" })));

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
