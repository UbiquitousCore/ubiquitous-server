using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private readonly List<uint> _learnedSpells = [];

    private (uint SpellId, SpellDefinition Spell)? _riding;

    public uint? RiddenMountId => _riding?.Spell.AuraMountId;

    private float AllowedSpeedMultiplier(DateTimeOffset now) =>
        OwnSpeedMultiplier(now) * (_riding is null ? 1f : MountRules.SpeedRatio);

    private IReadOnlyList<string> InventoryUseLesson(InventoryItemState item, ItemDefinition proto, SpellDefinition lesson)
    {
        if (!SpellLessons.KnowsRequiredSpell(proto, _spells.Known, _content.Items.Values, _content.Spells, MeetsItemRequirements))
        {
            var needed = _content.Spells.TryGetValue(proto.RequiredSpellId!.Value, out var spell) ? spell.NameKey : "another skill";
            return [Error(WireCodes.ProtocolError.CannotUse, $"You need {needed} to use this.")];
        }

        var fresh = SpellLessons.Taught(lesson)
            .Where(spellId => _content.Spells.ContainsKey(spellId) && !_spells.Known.Contains(spellId))
            .ToArray();
        if (fresh.Length == 0)
            return [Error(WireCodes.ProtocolError.CannotUse, "You already know this.")];

        _learnedSpells.AddRange(fresh);
        _baseSpells = [.. _baseSpells, .. fresh];
        var book = SpellBookWithTalents();
        _spells.ReplaceKnown(book);
        RaiseBarToHighestRanks(book);
        return
        [
            Reply(WebMessageTypes.SpellLearned, new SpellLearnedPayload(fresh)),
            Reply(WebMessageTypes.SpellBook, new SpellBookPayload([.. book], [.. _actionBar.Select(ActionSlotPayloadFor)])),
            .. ConsumeOne(item),
        ];
    }

    private IReadOnlyList<string> ResolveMount(WebNetworkId casterId, uint spellId, SpellDefinition spell, DateTimeOffset now)
    {
        _riding = (spellId, spell);
        MarkUnitValues(PlayerUnitId, new Dictionary<string, int>
        {
            [WireCodes.UnitValue.Mount] = checked((int)spell.AuraMountId!.Value),
        }, now);
        // Shown among the rider's buffs until they get down: it lasts until taken off.
        return
        [
            .. ResolveReleaseOnly(casterId, casterId.Text!, spellId, spell),
            Reply(WebMessageTypes.AuraApplied, new AuraAppliedPayload(
                spellId, casterId, casterId, Harmful: 0, MaxDuration: 0, Remaining: 0)),
        ];
    }

    private void Dismount(DateTimeOffset now, List<string> responses)
    {
        if (_riding is not { } riding || _character is null) return;
        _riding = null;
        MarkUnitValues(PlayerUnitId, new Dictionary<string, int> { [WireCodes.UnitValue.Mount] = 0 }, now);
        var unit = WebNetworkId.FromString(PlayerUnitId);
        responses.Add(Reply(WebMessageTypes.AuraEnded, new AuraEndedPayload(riding.SpellId, unit, unit)));
    }

    private void ShakeRider(DateTimeOffset now, List<string> responses)
    {
        if (_riding is { } riding && !_spells.RollTriggerChance(riding.Spell.AuraMountKeepSeatPercent ?? 0))
            Dismount(now, responses);
    }

    private IReadOnlyList<string> MountDismount(DateTimeOffset now)
    {
        if (_character is null)
            return [Error(WireCodes.ProtocolError.InvalidState, "Character selection is required.")];
        var responses = new List<string>();
        Dismount(now, responses);
        FlushUnitValues(now, responses);
        return responses;
    }
}
