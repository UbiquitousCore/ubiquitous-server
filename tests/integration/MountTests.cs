using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class MountTests
{
    private const uint HumanPermit = 6995;
    private const uint OrcPermit = 6997;
    private const uint HumanRiding = 6952;
    private const uint LearnHumanRiding = 6950;
    private const uint RidingSkillBook = 7461;
    private const uint Horse = 6978;
    private const uint LearnHorse = 6979;
    private const uint HorseBond = 7485;
    private const uint HorseModel = 3060;

    private static readonly GameContent Content = GameContent.Development with
    {
        Spells = new Dictionary<uint, SpellDefinition>(GameContent.Development.Spells)
        {
            [HumanPermit] = new(HumanPermit, "Riding Permit for Humans", 0, 0, 0f),
            [OrcPermit] = new(OrcPermit, "Riding Permit for Orcs", 0, 0, 0f),
            [HumanRiding] = new(HumanRiding, "Riding Skill for Humans", 0, 0, 0f),
            [LearnHumanRiding] = new(LearnHumanRiding, "Learn Riding Skill for Humans I", 0, 0, 0f,
                EffectTarget: SpellEffectTargets.Self, EffectLearnSpellId: HumanRiding),
            [Horse] = new(Horse, "Brown Horse", 3000, 0, 0f, EffectTarget: SpellEffectTargets.Self,
                AuraMountId: HorseModel, AuraMountKeepSeatPercent: 30, AuraMountSpeedupPercent: 20),
            [LearnHorse] = new(LearnHorse, "Learn Brown Horse", 0, 0, 0f,
                EffectTarget: SpellEffectTargets.Self, EffectLearnSpellId: Horse),
        },
        Items = new Dictionary<uint, ItemDefinition>(GameContent.Development.Items)
        {
            [RidingSkillBook] = new(RidingSkillBook, "Human's Riding Skill I", ItemClasses.Riding, 1, 0,
                OnUseSpellId: LearnHumanRiding, RequiredLevel: 20, RequiredSpellId: HumanPermit),
            [HorseBond] = new(HorseBond, "Bond: Brown Horse", ItemClasses.Riding, 0, 0,
                OnUseSpellId: LearnHorse, BuyPrice: 19_990_000, RequiredSpellId: HumanRiding),
        },
        StartingItems = [new StartingItemStack(HorseBond, 1)],
    };

    [Fact]
    public async Task UsingABond_TeachesItsMount_AndSpendsTheBond()
    {
        var ayla = await EnterAsync(Rider());

        var used = await UseBondAsync(ayla);

        Assert.Contains(used, message => Is(message, WebMessageTypes.SpellLearned) && message.Contains($"[{Horse}]"));
        Assert.Contains(used, message => Is(message, WebMessageTypes.SpellBook) && message.Contains($"{Horse}"));
        Assert.Contains(used, message => Is(message, WebMessageTypes.InventoryItemRemoved));
    }

    [Fact]
    public async Task UsingTheSecondOfTwoBonds_SpendsThatOne()
    {
        var content = Content with { StartingItems = [new StartingItemStack(HorseBond, 1), new StartingItemStack(HorseBond, 1)] };
        var session = TestSessions.Create(Accounts(), new InMemoryCharacterStore([Rider()]), content: content);
        await EnterAsync(session);

        var used = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 2, new { from = new { area = "bag", index = 1 }, itemId = "starter-item-1" })));

        var removed = Assert.Single(used, message => Is(message, WebMessageTypes.InventoryItemRemoved));
        Assert.Contains("\"starter-item-1\"", removed);
    }

    [Fact]
    public async Task AnOrcCannotLearnAHorse()
    {
        var ayla = await EnterAsync(Rider() with { Race = PlayerRaceIds.Orc });

        var used = await UseBondAsync(ayla);

        Assert.Contains(used, message => message.Contains("cannot_use") && message.Contains("Riding Skill for Humans"));
        Assert.DoesNotContain(used, message => Is(message, WebMessageTypes.InventoryItemRemoved));
    }

    [Fact]
    public async Task AHumanBelowTheRidingSkillsLevelCannotLearnAHorseYet()
    {
        var ayla = await EnterAsync(Rider() with { Level = 19 });

        var used = await UseBondAsync(ayla);

        Assert.Contains(used, message => message.Contains("cannot_use"));
    }

    [Fact]
    public async Task ASecondBondForAMountAlreadyKnown_IsKept()
    {
        var ayla = await EnterAsync(Rider() with { LearnedSpells = [Horse] });

        var used = await UseBondAsync(ayla);

        Assert.Contains(used, message => message.Contains("cannot_use") && message.Contains("already know"));
        Assert.DoesNotContain(used, message => Is(message, WebMessageTypes.InventoryItemRemoved));
    }

    [Fact]
    public async Task ALearnedMount_IsSavedAndKnownAgainOnTheNextVisit()
    {
        var characters = new InMemoryCharacterStore([Rider()]);
        var ayla = await EnterAsync(Rider(), characters);
        await UseBondAsync(ayla);

        await ayla.SaveCharacterAsync();
        var saved = await characters.GetByGuidAsync("char-rider");
        var again = TestSessions.Create(Accounts(), new InMemoryCharacterStore([saved!]), content: Content);
        var entered = await EnterAsync(again);

        Assert.Equal([Horse], saved!.LearnedSpells);
        Assert.Contains(entered, message => Is(message, WebMessageTypes.SpellBook) && message.Contains($"{Horse}"));
    }

    [Fact]
    public async Task CastingAKnownMount_SeatsTheRiderForEveryoneNearby()
    {
        var (hub, ayla, borin) = await PairAsync("m1");

        var started = await SendAsync(hub, ayla, WebMessageTypes.SpellRequest, new { spellId = Horse, targetId = ayla.Id });
        var now = DateTimeOffset.UtcNow;
        await TickAsync(hub, now.AddMilliseconds(3100));
        await TickAsync(hub, now.AddMilliseconds(3700));

        Assert.Contains(started, message => Is(message, WebMessageTypes.SpellCasting));
        Assert.Equal((int)HorseModel, MountShown(borin.Inbox, ayla.Id));
        Assert.Equal(HorseModel, ayla.Session.RiddenMountId);
    }

    [Fact]
    public async Task CastingTheMountAgain_GetsDownAtOnce()
    {
        var (hub, ayla, borin) = await PairAsync("m2");
        await MountAsync(hub, ayla);
        borin.Inbox.Clear();

        await SendAsync(hub, ayla, WebMessageTypes.SpellRequest, new { spellId = Horse, targetId = ayla.Id });
        await TickAsync(hub, DateTimeOffset.UtcNow.AddSeconds(10));

        Assert.Null(ayla.Session.RiddenMountId);
        Assert.Equal(0, MountShown(borin.Inbox, ayla.Id));
    }

    [Fact]
    public async Task AskingToDismount_GetsDown()
    {
        var (hub, ayla, _) = await PairAsync("m3");
        await MountAsync(hub, ayla);

        await SendAsync(hub, ayla, WebMessageTypes.MountDismount, new { });

        Assert.Null(ayla.Session.RiddenMountId);
    }

    [Fact]
    public async Task SomeoneArrivingWhileAPlayerRides_SeesThemMounted()
    {
        var (hub, ayla, _) = await PairAsync("m4");
        await MountAsync(hub, ayla);

        var late = await JoinAsync(hub, "m4", "Late");

        Assert.Equal((int)HorseModel, MountShown(late.Inbox, ayla.Id));
    }

    [Fact]
    public async Task ARideShowsAsALastingBuff_UntilTheRiderGetsDown()
    {
        var (hub, ayla, borin) = await PairAsync("m6");
        await MountAsync(hub, ayla);

        var buff = Assert.Single(PayloadsOf(borin.Inbox, WebMessageTypes.AuraApplied));
        Assert.Equal(Horse, buff.GetProperty("spellId").GetUInt32());
        Assert.Equal(0, buff.GetProperty("harmful").GetInt32());
        Assert.Equal(0, buff.GetProperty("maxDuration").GetInt32());

        await SendAsync(hub, ayla, WebMessageTypes.MountDismount, new { });

        var ended = Assert.Single(PayloadsOf(borin.Inbox, WebMessageTypes.AuraEnded));
        Assert.Equal(Horse, ended.GetProperty("spellId").GetUInt32());
    }

    [Fact]
    public async Task SomeoneArrivingWhileAPlayerRides_SeesTheRideAmongTheirBuffs()
    {
        var (hub, ayla, _) = await PairAsync("m7");
        await MountAsync(hub, ayla);

        var late = await JoinAsync(hub, "m7", "Late");

        var spawned = Assert.Single(PayloadsOf(late.Inbox, WebMessageTypes.EntitySpawned),
            payload => payload.GetProperty("networkId").GetString() == ayla.Id);
        Assert.Contains(spawned.GetProperty("auras").EnumerateArray(),
            aura => aura.GetProperty("spellId").GetUInt32() == Horse);
    }

    [Fact]
    public async Task ARiderMovesFasterThanTheyCouldOnFoot()
    {
        var (hub, ayla, _) = await PairAsync("m5");
        var stride = new PlayerMovePayload(1 + LongStride, 2, 3, 0, true, MovementState.Walking);

        var onFoot = await SendAsync(hub, ayla, WebMessageTypes.PlayerMove, stride);
        await MountAsync(hub, ayla);
        var riding = await SendAsync(hub, ayla, WebMessageTypes.PlayerMove, stride);

        Assert.Contains(onFoot, message => Is(message, WebMessageTypes.MoveAccepted));
        Assert.DoesNotContain(riding, message => Is(message, WebMessageTypes.MoveAccepted));
    }

    [Fact]
    public async Task ADevelopmentLogin_EntersWithEnoughGoldForEveryMount()
    {
        var session = TestSessions.Create(Accounts(), new InMemoryCharacterStore([Rider()]), content: Content,
            developmentUserIds: new HashSet<string> { "developer" });

        var entered = await EnterAsync(session);

        Assert.Contains(entered, message => Is(message, WebMessageTypes.PlayerValuesChanged)
            && message.Contains($"\"gold\":{DevelopmentFixtures.Purse}"));
    }

    // Longer than a step on foot may cover, within one on a mount.
    private const float LongStride = Ubiquitous.World.PlayerMovementLimits.MaxStepDistance * 1.3f;

    private static CharacterRecord Rider() => new(
        "char-rider", 1, "Ayla", 1, PlayerRaceIds.Human, PlayerClassIds.Warrior, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 20, [100, 200]);

    private static DevelopmentAccountStore Accounts() => new("developer", "dev-secret");

    private static bool Is(string message, string type) => message.Contains($"\"type\":\"{type}\"");

    private static async Task<PlaySession> EnterAsync(CharacterRecord character, ICharacterStore? characters = null)
    {
        var session = TestSessions.Create(Accounts(), characters ?? new InMemoryCharacterStore([character]), content: Content);
        await EnterAsync(session);
        return session;
    }

    private static async Task<IReadOnlyList<string>> EnterAsync(PlaySession session)
    {
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        return await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
    }

    private static Task<IReadOnlyList<string>> UseBondAsync(PlaySession session) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.InventoryUse, 2, new { from = new { area = "bag", index = 0 }, itemId = "starter-item" })));

    private sealed record Side(PlaySession Session, List<string> Inbox, string Id);

    private static async Task<(PlaySessionHub Hub, Side Ayla, Side Borin)> PairAsync(string tag)
    {
        var hub = new PlaySessionHub();
        var ayla = await JoinAsync(hub, tag, "Ayla", [Horse]);
        var borin = await JoinAsync(hub, tag, "Borin");
        ayla.Inbox.Clear();
        borin.Inbox.Clear();
        return (hub, ayla, borin);
    }

    private static async Task<Side> JoinAsync(PlaySessionHub hub, string tag, string name, IReadOnlyList<uint>? learned = null)
    {
        var guid = $"char-{tag}{name}";
        var character = Rider() with { Guid = guid, Name = name, LearnedSpells = learned };
        var session = TestSessions.Create(Accounts(), new InMemoryCharacterStore([character]),
            content: Content, combatRollUnit: () => 1.0, spellRollUnit: () => 0.5);
        var inbox = new List<string>();
        hub.Register(session, message => { inbox.Add(message); return Task.CompletedTask; });
        var side = new Side(session, inbox, $"player:{guid}");
        await SendAsync(hub, side, WebMessageTypes.AccountLogin, new LoginRequest("developer", "dev-secret"));
        await SendAsync(hub, side, WebMessageTypes.CharactersChoose, new ChooseCharacterRequest(name));
        return side;
    }

    private static async Task MountAsync(PlaySessionHub hub, Side rider)
    {
        await SendAsync(hub, rider, WebMessageTypes.SpellRequest, new { spellId = Horse, targetId = rider.Id });
        var now = DateTimeOffset.UtcNow;
        await TickAsync(hub, now.AddMilliseconds(3100));
        await TickAsync(hub, now.AddMilliseconds(3700));
    }

    private static Task TickAsync(PlaySessionHub hub, DateTimeOffset now) =>
        hub.TickAsync(session => session.TickCombat(now));

    private static async Task<IReadOnlyList<string>> SendAsync(PlaySessionHub hub, Side side, string type, object payload)
    {
        IReadOnlyList<string> replies = [];
        await hub.SendInOrderAsync(side.Session, async () => replies = await side.Session.HandleTextAsync(
            WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 1, payload))));
        return replies;
    }

    private static IEnumerable<JsonElement> PayloadsOf(IEnumerable<string> inbox, string type) =>
        inbox.Select(message => JsonDocument.Parse(message).RootElement)
            .Where(root => root.GetProperty("type").GetString() == type)
            .Select(root => root.GetProperty("payload").Clone());

    private static int? MountShown(IEnumerable<string> inbox, string unitId) =>
        inbox.Select(message => JsonDocument.Parse(message).RootElement)
            .Where(root => root.GetProperty("type").GetString() == WebMessageTypes.EntityValuesChanged)
            .Select(root => root.GetProperty("payload"))
            .Where(payload => payload.GetProperty("networkId").GetString() == unitId
                && payload.GetProperty("changed").TryGetProperty(WireCodes.UnitValue.Mount, out _))
            .Select(payload => (int?)payload.GetProperty("changed").GetProperty(WireCodes.UnitValue.Mount).GetInt32())
            .LastOrDefault();
}
