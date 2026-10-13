using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class OffHandSwingTests
{
    private const uint Blade = 91001;
    private const uint Knife = 91002;
    private const uint Buckler = 91003;

    private static readonly GameContent Content = GameContent.Development with
    {
        Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
        {
            [Blade] = new(Blade, "item-91001", ItemClasses.Weapons, 0, 1,
                InventoryType: InventoryTypes.OneHand, MinDamage: 5, MaxDamage: 7, SpeedMs: 2000),
            [Knife] = new(Knife, "item-91002", ItemClasses.Weapons, 15, 1,
                InventoryType: InventoryTypes.OffHand, MinDamage: 3, MaxDamage: 4, SpeedMs: 2000),
            [Buckler] = new(Buckler, "item-91003", 4, 6, 1, InventoryType: InventoryTypes.OffHand, Armor: 10),
        },
        Creatures = new Dictionary<uint, CreatureDefinition>(GameContent.Development.Creatures)
        {
            [9502] = new(9502, "boar", 3, 5000, MinDamage: 1, MaxDamage: 1,
                FactionType: UnitFactionTypes.Hostile),
        },
    };

    [Fact]
    public async Task A_WeaponInEachHand_SendsSwingsFromBothHands()
    {
        var log = await FightAsync(offHand: Knife);

        Assert.Contains(log, m => m.Contains("\"type\":\"attack.swingResult\"")
            && m.Contains("\"attackerId\":\"player:char-a\"") && m.Contains("\"hand\":\"main\""));
        Assert.Contains(log, m => m.Contains("\"type\":\"attack.swingResult\"")
            && m.Contains("\"attackerId\":\"player:char-a\"") && m.Contains("\"hand\":\"off\""));
    }

    [Fact]
    public async Task A_ShieldInTheOffHand_NeverSwings()
    {
        var log = await FightAsync(offHand: Buckler);

        Assert.Contains(log, m => m.Contains("\"type\":\"attack.swingResult\"") && m.Contains("\"hand\":\"main\""));
        Assert.DoesNotContain(log, m => m.Contains("\"hand\":\"off\""));
    }

    private static async Task<List<string>> FightAsync(uint offHand)
    {
        var equipment = new int[EquipmentSlots.Count];
        equipment[EquipmentSlots.MainHand] = (int)Blade;
        equipment[EquipmentSlots.OffHand] = (int)offHand;
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([new CharacterRecord(
                "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 4, equipment)]),
            [new CreatureSpawnDefinition(1001, 13, 9502, 2, 2, 4, 0)],
            content: Content, combatRollUnit: () => 1.0, wanderRollUnit: () => 0.9);
        await SendAsync(session, WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"));
        await SendAsync(session, WebMessageTypes.AttackEngage, 2, new { targetId = "Creature:1" });

        var log = new List<string>();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 8; i++)
        {
            log.AddRange(session.TickCombat(now));
            now += CombatService.SwingInterval / 2;
        }
        return log;
    }

    private static async Task<IReadOnlyList<string>> SendAsync(
        PlaySession session, string type, int sequence, object payload) =>
        await session.HandleTextAsync(WebMessageJson.Serialize(
            WebMessageEnvelope.Create(type, sequence, payload)));
}
