using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private TalentBook _talents = new([]);

    private IReadOnlyList<uint> _baseSpells = [];

    private async Task<IReadOnlyList<string>> TalentLearnAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (_character is null)
            return [Error(WireCodes.ProtocolError.InvalidState, "Character selection is required before learning a talent.")];

        var request = WebMessageJson.DeserializePayload<TalentLearnRequest>(payload);
        var refusal = _talents.Learn(request.TalentId, _character.Class, _level);
        if (refusal is TalentRefusal reason)
        {
            return [Reply(WebMessageTypes.TalentRefused, new TalentRefusedPayload(request.TalentId, WireTalentRefusal(reason)))];
        }

        var responses = TalentsChanged(Now);
        await SaveCharacterAsync(cancellationToken);
        return responses;
    }

    private async Task<IReadOnlyList<string>> TalentResetAsync(CancellationToken cancellationToken)
    {
        if (_character is null)
            return [Error(WireCodes.ProtocolError.InvalidState, "Character selection is required before resetting talents.")];
        if (_talents.Ranks.Count == 0) return [TalentStateReply()];

        _talents.Reset();
        var responses = TalentsChanged(Now);
        await SaveCharacterAsync(cancellationToken);
        return responses;
    }

    private void RestoreTalents(CharacterRecord character, IReadOnlyList<uint> baseSpells)
    {
        _baseSpells = baseSpells;
        _talents = new TalentBook(_content.TalentTrees);
        _talents.Restore(character.TalentRanks ?? new Dictionary<uint, int>(), character.Class, character.Level);
        ApplyPassiveTalents();
    }

    private IReadOnlyList<uint> SpellBookWithTalents()
    {
        var talentAbilities = TalentEffects.Castable(_talents.CurrentSpells, _content.Spells);
        var higherRanks = _level >= LevelingService.MaxLevel
            ? talentAbilities.SelectMany(PlaytestSpellbook.TalentAbilityRanksAtMaxLevel)
            : [];
        return [.. _baseSpells.Concat(talentAbilities).Concat(higherRanks).Distinct()];
    }

    private void ApplyPassiveTalents()
    {
        if (_character is null) return;
        _attributeModifiers.SetPassive(PlayerUnitId,
            TalentEffects.PassiveAttributes(_talents.CurrentSpells, _content.Spells));
    }

    private List<string> TalentsChanged(DateTimeOffset now)
    {
        var playerId = PlayerUnitId;
        ApplyPassiveTalents();
        var book = SpellBookWithTalents();
        _spells.ReplaceKnown(book);

        var maxHealth = _combat.EffectiveMaxHealth(_attributeModifiers.GetFlatAmount(playerId, AttributeIds.Stamina, now));
        var maxPower = _spells.EffectiveMaxPower(_attributeModifiers.GetFlatAmount(playerId, AttributeIds.Wisdom, now));
        return
        [
            TalentStateReply(),
            Reply(WebMessageTypes.SpellBook, new SpellBookPayload([.. book], [.. _actionBar.Select(ActionSlotPayloadFor)])),
            Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(new Dictionary<string, int>
            {
                [WireCodes.UnitValue.MaxHealth] = maxHealth,
                [WireCodes.UnitValue.MaxPower] = maxPower,
            })),
        ];
    }

    private string TalentStateReply() =>
        Reply(WebMessageTypes.TalentState, new TalentStatePayload(
            _talents.Unspent(_level),
            [.. _talents.Ranks.Select(pair => new TalentRankPayload(pair.Key, pair.Value))]));

    private static string WireTalentRefusal(TalentRefusal reason) => reason switch
    {
        TalentRefusal.UnknownTalent => WireCodes.TalentRefusal.UnknownTalent,
        TalentRefusal.WrongClass => WireCodes.TalentRefusal.WrongClass,
        TalentRefusal.MaxRank => WireCodes.TalentRefusal.MaxRank,
        TalentRefusal.NoPoints => WireCodes.TalentRefusal.NoPoints,
        TalentRefusal.TierLocked => WireCodes.TalentRefusal.TierLocked,
        TalentRefusal.Prerequisite => WireCodes.TalentRefusal.Prerequisite,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
