using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class PartyRewardTests
{
    private const uint Wolf = 9001;
    private const int Purse = 90;

    private static readonly GameContent Content = GameContent.Development with
    {
        Creatures = new Dictionary<uint, CreatureDefinition>
        {
            [Wolf] = new(Wolf, "creature-9001", Level: 10, MaxHealth: 1, MinDamage: 0, MaxDamage: 0,
                MoneyDropMin: Purse, MoneyDropMax: Purse),
        },
        Quests =
        [
            new QuestDefinition(5001, Wolf, 12001, 12002, 1,
                [new QuestObjectiveDefinition(0, QuestObjectiveKind.Killed, GoalCount: 1, ObjectiveCreature: Wolf)]),
        ],
    };

    [Fact]
    public async Task AKill_IsSharedWithATeammateStandingNearby()
    {
        var hub = new PlaySessionHub();
        var ayla = await JoinAsync(hub, "a1", "Ayla");
        var borin = await JoinAsync(hub, "b1", "Borin");
        await TeamUpAsync(ayla, borin, "b1");

        var now = await KillAsync(ayla);
        var shared = borin.Session.TickCombat(now);

        var each = PartyRewards.KillXpShares([new("a", 10), new("b", 10)], 10, elite: false)["a"];
        Assert.Contains(ayla.Killed, m => IsXpGain(m, each));
        Assert.Contains(shared, m => IsXpGain(m, each));
    }

    [Fact]
    public async Task ATeammateFurtherThanFiftyUnitsAway_SharesNothing()
    {
        var hub = new PlaySessionHub();
        var ayla = await JoinAsync(hub, "a2", "Ayla");
        var borin = await JoinAsync(hub, "b2", "Borin", x: 1 + PartyRewards.ShareRange + 5);
        await TeamUpAsync(ayla, borin, "b2");

        var now = await KillAsync(ayla);
        var shared = borin.Session.TickCombat(now);

        Assert.Contains(ayla.Killed, m => IsXpGain(m, LevelingService.KillXp(10, 10)));
        Assert.DoesNotContain(shared, m => m.Contains("\"type\":\"player.expGained\""));
    }

    [Fact]
    public async Task ATeammateNearby_GetsCreditForAKillQuest()
    {
        var hub = new PlaySessionHub();
        var ayla = await JoinAsync(hub, "a4", "Ayla");
        var borin = await JoinAsync(hub, "b4", "Borin");
        await TeamUpAsync(ayla, borin, "b4");
        await SendAsync(borin.Session, WebMessageTypes.QuestAccept, new { networkId = "Creature:1", questId = 5001u });

        var now = await KillAsync(ayla);
        var shared = borin.Session.TickCombat(now);

        var update = Assert.Single(shared, m => m.Contains("\"type\":\"quest.updated\""));
        Assert.Contains("\"readyToHandIn\":true", update);
    }

    [Fact]
    public async Task MoneyOnABody_IsSplitBetweenTheTeammatesNearby()
    {
        var hub = new PlaySessionHub();
        var ayla = await JoinAsync(hub, "a3", "Ayla");
        var borin = await JoinAsync(hub, "b3", "Borin");
        await TeamUpAsync(ayla, borin, "b3");
        var now = await KillAsync(ayla);
        borin.Session.TickCombat(now);

        var picked = await SendAsync(ayla.Session, WebMessageTypes.LootTakeMoney, new { networkId = "Creature:1" });
        var shared = borin.Session.TickCombat(now + TimeSpan.FromMilliseconds(100));

        Assert.Contains(picked, m => IsGold(m, Purse / 2));
        Assert.Contains(shared, m => IsGold(m, Purse / 2));
    }

    private sealed class Side(PlaySession session)
    {
        public PlaySession Session { get; } = session;
        public List<string> Killed { get; } = [];
    }

    private static bool IsXpGain(string message, int amount) =>
        message.Contains("\"type\":\"player.expGained\"") && message.Contains($"\"amount\":{amount},");

    private static bool IsGold(string message, int gold) =>
        message.Contains("\"type\":\"player.updated\"") && message.Contains($"\"gold\":{gold}}}");

    private static async Task<DateTimeOffset> KillAsync(Side killer)
    {
        await SendAsync(killer.Session, WebMessageTypes.AttackEngage, new { targetId = "Creature:1" });
        var now = DateTimeOffset.UtcNow;
        for (var swing = 0; swing < 20; swing++)
        {
            now += TimeSpan.FromSeconds(2);
            killer.Killed.AddRange(killer.Session.TickCombat(now));
            if (killer.Killed.Any(m => m.Contains("\"type\":\"unit.died\""))) break;
        }
        Assert.Contains(killer.Killed, m => m.Contains("\"type\":\"unit.died\""));
        return now;
    }

    private static async Task TeamUpAsync(Side leader, Side member, string memberGuid)
    {
        await SendAsync(leader.Session, WebMessageTypes.PartyInvite, new { networkId = $"player:char-{memberGuid}" });
        await SendAsync(member.Session, WebMessageTypes.PartyAccept, new { });
    }

    private static async Task<Side> JoinAsync(PlaySessionHub hub, string guid, string name, float x = 1)
    {
        var session = TestSessions.Create(new DevelopmentAccountStore(guid, "p"),
            new InMemoryCharacterStore([new CharacterRecord(
                $"char-{guid}", 1, name, 1, 2, 3, 4, 5, 6, 7, 8, 13, x, 2, 3, 10, [100, 200]) with { Currency = 0 }]),
            [new CreatureSpawnDefinition(1, 13, Wolf, 2f, 2f, 3f, 0f)], null, Content,
            combatRollUnit: () => 1.0, parties: TestSessions.PartiesOf(hub));
        hub.Register(session, _ => Task.CompletedTask);
        await SendAsync(session, WebMessageTypes.AccountLogin, new LoginRequest(guid, "p"));
        await SendAsync(session, WebMessageTypes.CharactersChoose, new ChooseCharacterRequest(name));
        return new Side(session);
    }

    private static Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, object payload) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 1, payload)));
}
