using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class AdminGiftTests
{
    private static readonly GameContent Content = GameContent.Development with
    {
        StartingCurrency = 0,
        Spells = GameContent.Development.Spells
            .Concat(PlaytestSpellbook.AtMaxLevel(PlayerClassIds.Ranger)
                .Where(id => !GameContent.Development.Spells.ContainsKey(id))
                .Select(id => KeyValuePair.Create(id, new SpellDefinition(id, $"spell-{id}", 0, 0, 0f))))
            .ToDictionary(pair => pair.Key, pair => pair.Value),
    };

    private static readonly uint AnItem = Content.Items.Keys.First();

    [Fact]
    public async Task AnAdministrator_IsGivenTheItemAsked()
    {
        var session = await EnterAsync(admin: true);

        var given = await SendAsync(session, WebMessageTypes.AdminGiveItem, new { entry = AnItem, count = 1 });

        Assert.Contains(given, message => message.Contains($"\"type\":\"{WebMessageTypes.InventoryItemAdded}\"")
            && message.Contains($"\"entry\":{AnItem}"));
    }

    [Fact]
    public async Task AnyoneElse_IsRefusedAnItem_AndGetsNothing()
    {
        var session = await EnterAsync(admin: false);

        var refused = await SendAsync(session, WebMessageTypes.AdminGiveItem, new { entry = AnItem, count = 1 });

        Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.Forbidden));
        Assert.DoesNotContain(refused, message => message.Contains(WebMessageTypes.InventoryItemAdded));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(4_000_000, 1)]
    public async Task AnItemTheServerDoesNotKnow_IsRefused(int entry, int count)
    {
        var session = await EnterAsync(admin: true);

        var refused = await SendAsync(session, WebMessageTypes.AdminGiveItem, new { entry, count });

        Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.InvalidPayload));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task ACountOutsideTheLimit_IsRefused(int count)
    {
        var session = await EnterAsync(admin: true);

        var refused = await SendAsync(session, WebMessageTypes.AdminGiveItem, new { entry = AnItem, count });

        Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.InvalidPayload));
    }

    [Fact]
    public async Task AnAdministrator_IsGivenGold_AndToldTheNewPurse()
    {
        var session = await EnterAsync(admin: true);

        var given = await SendAsync(session, WebMessageTypes.AdminGiveGold, new { gold = 5 });
        var more = await SendAsync(session, WebMessageTypes.AdminGiveGold, new { gold = 2 });

        Assert.Contains(given, message => message.Contains($"\"{WireCodes.UnitValue.Gold}\":{5 * CoinWords.Gold}"));
        Assert.Contains(more, message => message.Contains($"\"{WireCodes.UnitValue.Gold}\":{7 * CoinWords.Gold}"));
    }

    [Fact]
    public async Task AnyoneElse_IsRefusedGold()
    {
        var session = await EnterAsync(admin: false);

        var refused = await SendAsync(session, WebMessageTypes.AdminGiveGold, new { gold = 5 });

        Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.Forbidden));
        Assert.DoesNotContain(refused, message => message.Contains(WebMessageTypes.PlayerValuesChanged));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(100_001)]
    public async Task AnAmountOutsideTheLimit_IsRefused(int gold)
    {
        var session = await EnterAsync(admin: true);

        var refused = await SendAsync(session, WebMessageTypes.AdminGiveGold, new { gold });

        Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.InvalidPayload));
    }

    [Fact]
    public async Task AnAdministrator_CanSetTheirLevel_AndIsToldTheNewValues()
    {
        var session = await EnterAsync(admin: true);

        var set = await SendAsync(session, WebMessageTypes.AdminSetLevel, new { level = 30 });
        var back = await SendAsync(session, WebMessageTypes.AdminSetLevel, new { level = 1 });

        Assert.Contains(set, message => message.Contains(WebMessageTypes.PlayerValuesChanged)
            && message.Contains($"\"{WireCodes.UnitValue.Level}\":30"));
        Assert.Contains(set, message => message.Contains(WebMessageTypes.TalentState));
        Assert.Contains(back, message => message.Contains($"\"{WireCodes.UnitValue.Level}\":1"));
        Assert.Contains(set, message => message.Contains(WebMessageTypes.PlayerExpGained) && message.Contains("\"levelUp\":true"));
        Assert.DoesNotContain(back, message => message.Contains(WebMessageTypes.PlayerExpGained));
    }

    [Fact]
    public async Task AnyoneElse_IsRefusedALevelChange()
    {
        var session = await EnterAsync(admin: false);

        var refused = await SendAsync(session, WebMessageTypes.AdminSetLevel, new { level = 30 });

        Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.Forbidden));
        Assert.DoesNotContain(refused, message => message.Contains(WebMessageTypes.PlayerValuesChanged));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    [InlineData(61)]
    public async Task ALevelOutsideTheGame_IsRefused(int level)
    {
        var session = await EnterAsync(admin: true);

        var refused = await SendAsync(session, WebMessageTypes.AdminSetLevel, new { level });

        Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.InvalidPayload));
    }

    [Fact]
    public async Task AnAdministrator_LearnsEveryRankOfTheirClass_AndNothingTheSecondTime()
    {
        var session = await EnterAsync(admin: true);

        var learned = await SendAsync(session, WebMessageTypes.AdminLearnSpells, new { });
        var again = await SendAsync(session, WebMessageTypes.AdminLearnSpells, new { });

        var book = learned.Single(message => message.Contains($"\"type\":\"{WebMessageTypes.SpellBook}\""));
        foreach (var id in PlaytestSpellbook.AtMaxLevel(PlayerClassIds.Ranger))
            Assert.Contains($"{id}", book);
        Assert.Contains(learned, message => message.Contains(WebMessageTypes.SpellLearned));
        Assert.Contains(again, message => message.Contains(WireCodes.ProtocolError.InvalidState));
    }

    [Fact]
    public async Task AnyoneElse_IsRefusedSpells()
    {
        var session = await EnterAsync(admin: false);

        var refused = await SendAsync(session, WebMessageTypes.AdminLearnSpells, new { });

        Assert.Contains(refused, message => message.Contains(WireCodes.ProtocolError.Forbidden));
        Assert.DoesNotContain(refused, message => message.Contains(WebMessageTypes.SpellLearned));
    }

    // One spell with three ranks: the first comes with the class, the others from lessons needing levels 10 and 20.
    private static readonly uint[] Chain = [.. PlaytestSpellbook.AtMaxLevel(PlayerClassIds.Ranger).Take(3)];

    private static GameContent LessonContent()
    {
        const uint firstLesson = 900_001;
        var spells = Content.Spells.ToDictionary(pair => pair.Key, pair => pair.Value);
        for (var rank = 0; rank < Chain.Length; rank++)
            spells[Chain[rank]] = new SpellDefinition(Chain[rank], "Chain", 0, 0, 0f, Rank: rank + 1);
        var items = Content.Items.ToDictionary(pair => pair.Key, pair => pair.Value);
        var template = Content.Items.Values.First();
        for (var lesson = 0; lesson < 2; lesson++)
        {
            var spellId = firstLesson + (uint)lesson;
            spells[spellId] = new SpellDefinition(spellId, "lesson", 0, 0, 0f, EffectLearnSpellId: Chain[lesson + 1]);
            items[7_000_001 + (uint)lesson] = template with
            {
                Id = 7_000_001 + (uint)lesson,
                OnUseSpellId = spellId,
                RequiredLevel = (uint)(10 * (lesson + 1)),
                AllowableClassMask = 0,
            };
        }

        return Content with { Spells = spells, Items = items };
    }

    private static HashSet<uint> BookOf(IEnumerable<string> messages)
    {
        var book = messages.Last(message => message.Contains($"\"type\":\"{WebMessageTypes.SpellBook}\""));
        using var document = JsonDocument.Parse(book);
        return [.. document.RootElement.GetProperty("payload").GetProperty("spells").EnumerateArray().Select(id => id.GetUInt32())];
    }

    [Fact]
    public async Task LearnAllSpells_TakesOnlyTheRanksTheLevelHasReached()
    {
        var session = await EnterAsync(admin: true, content: LessonContent());

        var learned = BookOf(await SendAsync(session, WebMessageTypes.AdminLearnSpells, new { }));

        Assert.Contains(Chain[0], learned);
        Assert.Contains(Chain[1], learned);
        Assert.DoesNotContain(Chain[2], learned);
    }

    [Fact]
    public async Task LoweringTheLevel_ForgetsTheRanksAboveIt_AndKeepsTheRest()
    {
        var session = await EnterAsync(admin: true, content: LessonContent());
        await SendAsync(session, WebMessageTypes.AdminSetLevel, new { level = 60 });
        Assert.Contains(Chain[2], BookOf(await SendAsync(session, WebMessageTypes.AdminLearnSpells, new { })));

        var at15 = BookOf(await SendAsync(session, WebMessageTypes.AdminSetLevel, new { level = 15 }));
        var at5 = BookOf(await SendAsync(session, WebMessageTypes.AdminSetLevel, new { level = 5 }));

        Assert.Contains(Chain[1], at15);
        Assert.DoesNotContain(Chain[2], at15);
        Assert.Contains(Chain[0], at5);
        Assert.DoesNotContain(Chain[1], at5);
    }

    [Fact]
    public async Task ARankLostToALowerLevel_ReplacesItsButtonWithTheHighestRankLeft()
    {
        var session = await EnterAsync(admin: true, content: LessonContent());
        await SendAsync(session, WebMessageTypes.AdminSetLevel, new { level = 60 });
        await SendAsync(session, WebMessageTypes.AdminLearnSpells, new { });
        await SendAsync(session, WebMessageTypes.ActionBarAssign, new { slot = 2, id = Chain[2], type = 0 });

        var book = (await SendAsync(session, WebMessageTypes.AdminSetLevel, new { level = 15 })).Last();

        using var document = JsonDocument.Parse(book);
        var bar = document.RootElement.GetProperty("payload").GetProperty("actionBar").EnumerateArray()
            .Single(slot => slot.GetProperty("bar").GetString() == "primary" && slot.GetProperty("slot").GetInt32() == 2);
        Assert.Equal(Chain[1], bar.GetProperty("id").GetUInt32());
    }

    private static async Task<PlaySession> EnterAsync(bool admin, GameContent? content = null)
    {
        var characters = new InMemoryCharacterStore([new CharacterRecord(
            "char-a", 1, "Ayla", 1, 2, 3, 4, 5, 6, 7, 8, 13, 1, 2, 3, 12, [100, 200])]);
        var session = TestSessions.Create(
            new DevelopmentAccountStore("developer", "dev-secret", isAdmin: admin), characters, content: content ?? Content);
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
        return session;
    }

    private static Task<IReadOnlyList<string>> SendAsync(PlaySession session, string type, object payload) =>
        session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 2, payload)));
}
