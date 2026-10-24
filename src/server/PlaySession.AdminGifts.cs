using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    // Own choice: enough for any stack, small enough that one request cannot fill the bags and the log.
    private const int MaxGivenItems = 1000;

    // Own choice: a single request stays well inside the purse's range.
    private const int MaxGivenGold = 100_000;

    private IReadOnlyList<string> AdminGiveItem(JsonElement payload)
    {
        if (RefuseNonAdmin("give items") is { } refused) return refused;
        var request = WebMessageJson.DeserializePayload<AdminGiveItemPayload>(payload);
        if (request.Entry <= 0 || !_content.Items.ContainsKey((uint)request.Entry))
            return [Error(WireCodes.ProtocolError.InvalidPayload, $"There is no item {request.Entry}.")];
        if (request.Count < 1 || request.Count > MaxGivenItems)
            return [Error(WireCodes.ProtocolError.InvalidPayload, $"Give between 1 and {MaxGivenItems}.")];

        var added = _belongings.Receive(request.Entry, request.Count);
        if (added is null) return [ItemRefused(ItemErrors.InventoryFull)];
        _logger.LogInformation("GM {User} gave themselves {Count} of item {Entry}", _account!.UserId, request.Count, request.Entry);
        return [.. added.Select(item => Reply(WebMessageTypes.InventoryItemAdded, _belongings.PayloadFor(item)))];
    }

    private IReadOnlyList<string> AdminGiveGold(JsonElement payload)
    {
        if (RefuseNonAdmin("give gold") is { } refused) return refused;
        var request = WebMessageJson.DeserializePayload<AdminGiveGoldPayload>(payload);
        if (request.Gold < 1 || request.Gold > MaxGivenGold)
            return [Error(WireCodes.ProtocolError.InvalidPayload, $"Give between 1 and {MaxGivenGold} gold.")];

        var copper = (long)request.Gold * CoinWords.Gold;
        if (_belongings.Currency + copper > int.MaxValue)
            return [Error(WireCodes.ProtocolError.InvalidPayload, "That would overfill the purse.")];

        _belongings.Currency += (int)copper;
        _logger.LogInformation("GM {User} gave themselves {Gold} gold", _account!.UserId, request.Gold);
        return
        [
            Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
                new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = _belongings.Currency })),
        ];
    }

    private IReadOnlyList<string> AdminSetLevel(JsonElement payload)
    {
        if (RefuseNonAdmin("set levels") is { } refused) return refused;
        var request = WebMessageJson.DeserializePayload<AdminSetLevelPayload>(payload);
        if (request.Level < 1 || request.Level > LevelingService.MaxLevel)
            return [Error(WireCodes.ProtocolError.InvalidPayload, $"Choose a level from 1 to {LevelingService.MaxLevel}.")];

        var wasLevel = _level;
        _xp = 0;
        var values = new Dictionary<string, int>
        {
            [WireCodes.UnitValue.Xp] = 0,
            [WireCodes.UnitValue.XpToNextLevel] = LevelingService.XpToNextLevel(request.Level),
        };
        // Full health: a GM changing level is not in a fight.
        ApplyLevel(request.Level, null, values, Now);
        _logger.LogInformation("GM {User} set their level to {Level}", _account!.UserId, request.Level);
        return
        [
            Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(values)),
            // Going up plays the same banner, sound and effect as earning the level.
            .. request.Level > wasLevel
                ? [Reply(WebMessageTypes.PlayerExpGained, new PlayerExpGainedPayload(0, true))]
                : Array.Empty<string>(),
            TalentStateReply(),
            SpellsForTheLevel(),
        ];
    }

    // Nothing hurts the character while it is on: creatures, spells, auras and other players alike. It lasts until
    // the character leaves the world, so a relog starts without it.
    private IReadOnlyList<string> AdminSetInvincible(JsonElement payload)
    {
        if (RefuseNonAdmin("be invincible") is { } refused) return refused;
        var request = WebMessageJson.DeserializePayload<AdminInvinciblePayload>(payload);
        _combat.GodMode = request.On;
        _logger.LogInformation("GM {User} turned invincibility {State}", _account!.UserId, request.On ? "on" : "off");
        return [Reply(WebMessageTypes.AdminInvincible, new AdminInvinciblePayload(request.On))];
    }

    // The levels the class's spells are learned at; see SpellLearning.
    private IReadOnlyDictionary<uint, int> SpellLevels() =>
        SpellLearning.LevelsFor(_content.Items.Values, _content.Spells, _character!.Class);

    // The class's own spells, and the higher ranks of the talent abilities it holds: what a level can add or take.
    // A spell no lesson teaches counts as level 1 (it comes with the class); a talent rank none teaches, as the cap.
    private IEnumerable<(uint Id, int Level)> LevelledSpells(IReadOnlyDictionary<uint, int> levels) =>
        PlaytestSpellbook.AtMaxLevel(_character!.Class)
            .Select(id => (id, SpellLearning.LevelOf(levels, id, 1)))
            .Concat(TalentEffects.Castable(_talents.CurrentSpells, _content.Spells)
                .SelectMany(PlaytestSpellbook.TalentAbilityRanksAtMaxLevel)
                .Select(id => (id, SpellLearning.LevelOf(levels, id, LevelingService.MaxLevel))));

    // Every rank of the class's spells that the level has reached.
    private IReadOnlyList<string> AdminLearnSpells()
    {
        if (RefuseNonAdmin("learn spells") is { } refused) return refused;
        var fresh = LevelledSpells(SpellLevels())
            .Where(spell => spell.Level <= _level && _content.Spells.ContainsKey(spell.Id) && !_spells.Known.Contains(spell.Id))
            .Select(spell => spell.Id)
            .Distinct()
            .ToArray();
        if (fresh.Length == 0)
            return [Error(WireCodes.ProtocolError.InvalidState, "There is nothing new to learn at this level.")];

        _learnedSpells.AddRange(fresh);
        _baseSpells = [.. _baseSpells, .. fresh];
        var book = SpellBookWithTalents();
        _spells.ReplaceKnown(book);
        RaiseBarToHighestRanks(book);
        _logger.LogInformation("GM {User} learned {Count} spells", _account!.UserId, fresh.Length);
        return
        [
            Reply(WebMessageTypes.SpellLearned, new SpellLearnedPayload(fresh)),
            Reply(WebMessageTypes.SpellBook, new SpellBookPayload([.. book], [.. _actionBar.Select(ActionSlotPayloadFor)])),
        ];
    }

    // After a level change: ranks the level has not reached are forgotten, and a button that held one takes the
    // highest rank still known, or empties.
    private string SpellsForTheLevel()
    {
        var beyond = LevelledSpells(SpellLevels()).Where(spell => spell.Level > _level).Select(spell => spell.Id).ToHashSet();
        _learnedSpells.RemoveAll(beyond.Contains);
        _baseSpells = [.. _baseSpells.Where(id => !beyond.Contains(id))];
        var book = SpellBookWithTalents();
        _spells.ReplaceKnown(book);
        foreach (var (slot, (id, type)) in _actionBar.ToArray())
        {
            if (type != SpellButtonType || book.Contains(id)) continue;
            if (SpellRanks.HighestKnownOfChain(id, book, _content.Spells) is uint stand) _actionBar[slot] = (stand, type);
            else _actionBar.Remove(slot);
        }

        return Reply(WebMessageTypes.SpellBook, new SpellBookPayload([.. book], [.. _actionBar.Select(ActionSlotPayloadFor)]));
    }

    // The account's own flag, read at sign-in, is the only thing that opens these.
    private IReadOnlyList<string>? RefuseNonAdmin(string what)
    {
        if (_world is null || _character is null)
            return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required first.")];
        if (_account?.IsAdmin != true)
            return [Error(WireCodes.ProtocolError.Forbidden, $"Only an administrator may {what}.")];
        return null;
    }
}
