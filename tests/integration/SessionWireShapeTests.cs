namespace Ubiquitous.Integration.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

public sealed class SessionWireShapeTests
{
    private const string FixtureName = "session-messages.json";

    private static readonly WebNetworkId Hero = WebNetworkId.FromString("player:char-a");
    private static readonly WebNetworkId Friend = WebNetworkId.FromString("player:char-b");
    private static readonly WebNetworkId Giver = WebNetworkId.FromString("Creature:12");
    private static readonly WebNetworkId Wolf = WebNetworkId.FromString("Creature:40");

    private static readonly QuestObjectivePayload KillFour = new(0, 1, 0, 0, 0, 0, 4, 90201);
    private static readonly QuestZonePayload WolfDen = new(90301, 1, 10.5f, 2f, -20.25f, 8f);
    private static readonly QuestZonePayload GiverHut = new(90302, 1, 24f, 3f, 4f, 5f);
    private static readonly QuestRewardItemPayload Reward = new(90001, 90401, 2, 1);

    public static IReadOnlyList<(string Type, object Payload)> Messages { get; } =
    [
        (WebMessageTypes.CharactersListed, new CharactersListedPayload(
            [new CharacterSummary("char-a", "Ayla", 3, 1, 0, 3, 90501, new Appearance(1, 2, 3, 4),
                1, new Position(2, 7, 55), [0, 0, 0, 0, 90001], Xp: 120, XpToLevel: 400)],
            [0, 1, 2])),
        (WebMessageTypes.WorldEntered, new WorldEnteredPayload(1, new Position(2, 7, 55), Hero,
            [new HeldAuraPayload(90103, Hero, 0, 30000, 4200)])),
        (WebMessageTypes.EntitySpawned, new EntitySpawnedPayload(Wolf, 90801, new Position(10.5, 2, -20.25), 0,
            Level: 4, Health: 58, MaxHealth: 80, Faction: 2,
            Auras: [new HeldAuraPayload(90102, Friend, 1, 10000, 6500), new HeldAuraPayload(90104, Wolf, 0, 0, 0)])),
        (WebMessageTypes.SpellBook, new SpellBookPayload(
            [90101, 90102],
            [new ActionSlotPayload("primary", 0, 90101, 0), new ActionSlotPayload("shortcut", 1, 90102, 0)])),
        (WebMessageTypes.ActionBarAssigned, new ActionSlotPayload("primary", 2, 90102, 0)),
        (WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(new Dictionary<string, int>
        {
            [WireCodes.UnitValue.Level] = 3,
            [WireCodes.UnitValue.Health] = 181,
            [WireCodes.UnitValue.MaxHealth] = 181,
            [WireCodes.UnitValue.Power] = 100,
            [WireCodes.UnitValue.MaxPower] = 100,
            [WireCodes.UnitValue.Xp] = 120,
            [WireCodes.UnitValue.XpToNextLevel] = 400,
            [WireCodes.UnitValue.Gold] = 5000,
        })),
        (WebMessageTypes.PlayerExpGained, new PlayerExpGainedPayload(35, false)),
        (WebMessageTypes.TalentState, new TalentStatePayload(3,
            [new TalentRankPayload(90701, 2), new TalentRankPayload(90702, 1)])),
        (WebMessageTypes.TalentRefused, new TalentRefusedPayload(90703, WireCodes.TalentRefusal.TierLocked)),
        (WebMessageTypes.AttackMeleeStart, new AttackMeleeStartPayload(Hero, Wolf)),
        (WebMessageTypes.AttackSwingResult, new AttackSwingResultPayload(
            Hero, Wolf, WireCodes.Outcome.Critical, 42, 58, WireCodes.Hand.Off)),
        (WebMessageTypes.SpellOutcome, new SpellOutcomePayload(
            Friend, Hero, 90102, WireCodes.Outcome.Hit, 0, 181, Absorbed: 20)),
        (WebMessageTypes.UnitDied, new UnitDiedPayload(Wolf, new Position(10.5, 2, -20.25))),
        (WebMessageTypes.PartyInviteSent, new PartyInviteSentPayload(Friend, "Kael")),
        (WebMessageTypes.PartyInvited, new PartyInvitedPayload(Hero, "Ayla")),
        (WebMessageTypes.PartyVoteKickAsked, new PartyVoteKickAskedPayload(Friend, "Kael", Hero, "Ayla")),
        (WebMessageTypes.PartyVoteKickRefused, new PartyVoteKickRefusedPayload(Friend, "Kael", Hero, "Ayla")),
        (WebMessageTypes.PartyVoteKickEnded, new PartyVoteKickEndedPayload(Friend, "Kael", true)),
        (WebMessageTypes.PartyRoster, new PartyRosterPayload("7",
            [new PartyRosterMemberPayload(Hero, "Ayla", true, true), new PartyRosterMemberPayload(Friend, "Kael", false, false)])),
        (WebMessageTypes.DuelChallenged, new DuelChallengedPayload(Hero)),
        (WebMessageTypes.QuestStatuses, new QuestStatusesPayload(
            [new QuestStatusRow(Giver, QuestService.WireStatus(QuestMark.Offered))])),
        (WebMessageTypes.QuestDetails, new QuestDetailsPayload(Giver, 90601, 90602, 90603,
            QuestService.WireStatus(QuestMark.Offered), 1, [Reward], [], 250, [KillFour])),
        (WebMessageTypes.QuestUpdated, new QuestUpdatePayload(90601, 90602, 90603,
            QuestService.WireStatus(QuestMark.Underway), 0, false, [KillFour with { Killed = 2 }],
            [new QuestCounterPayload(0, QuestObjectiveKind.Killed, 2)], [WolfDen])),
        (WebMessageTypes.QuestLog, new QuestLogPayload(
        [
            new QuestLogEntryPayload(90601, 90602, 90603, QuestService.WireStatus(QuestMark.Underway),
                0, false, [KillFour], 1, [Reward], [], 250, [WolfDen]),
            new QuestLogEntryPayload(90611, 90612, 90613, QuestService.WireStatus(QuestMark.ReadyToHandIn),
                1, true, [], 1, HandInZone: GiverHut),
        ])),
        (WebMessageTypes.Gossip, new GossipPayload(Giver, 90701)),
        (WebMessageTypes.NpcChoiceResult, new NpcChoiceResultPayload(Giver, 90702, 2, WireCodes.ChoiceResult.NotEnoughMoney)),
        (WebMessageTypes.EntityLootable, new EntityLootablePayload(Wolf, WireCodes.Loot.Lootable)),
        (WebMessageTypes.LootContents, new LootContentsPayload(Wolf, 1234,
            [new LootRowPayload(0, "item-90001-1", 90001, 3), new LootRowPayload(1, "item-90002-1", 90002, 1)])),
        (WebMessageTypes.QuestShareOffered, new QuestShareOfferedPayload(Friend, "Kael", 90601, 90602, 90603, 1)),
        (WebMessageTypes.QuestShareResult, new QuestShareResultPayload(Friend, "Kael", 90601, WireCodes.ShareResult.Accepted)),
    ];

    private static string Canonical()
    {
        var root = new JsonObject();
        foreach (var (type, payload) in Messages)
        {
            root[type] = JsonNode.Parse(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 0, payload)))!["payload"]!.DeepClone();
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    [Fact]
    public void TheCommittedFixtureIsWhatThisServerActuallySends()
    {
        var canonical = Canonical();
        if (Environment.GetEnvironmentVariable("UBIQUITOUS_WRITE_WIRE_FIXTURES") == "1")
        {
            var source = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "fixtures", FixtureName));
            File.WriteAllText(source, canonical + "\n");
        }

        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", FixtureName);
        Assert.True(File.Exists(path), $"Missing {path}. Regenerate with UBIQUITOUS_WRITE_WIRE_FIXTURES=1.");
        Assert.Equal(
            canonical.ReplaceLineEndings("\n"),
            File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd('\n'));
    }

    [Fact]
    public void EveryPinnedMessageIsOneThisServerNames()
    {
        var types = WireVocabularyTests.Vocabulary()["messageTypes"]!.AsArray().Select(node => (string)node!).ToHashSet();
        foreach (var (type, _) in Messages) Assert.Contains(type, types);
    }
}
