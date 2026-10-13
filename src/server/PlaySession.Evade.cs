using Ubiquitous.Data;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private readonly Dictionary<string, (int MaxHealth, HashSet<uint> EndedAuras)> _evaded =
        new(StringComparer.Ordinal);

    private void ResetForHome(string wireId)
    {
        if (_combat.ResetHealth(wireId) is not int maxHealth) return;
        _combat.ClearHostility(wireId);
        _shared?.ClearFighters(wireId);
        _outOfMeleeRangeSince.Remove(wireId);
        _announcedMobVictims.Remove(wireId);

        if (!_evaded.TryGetValue(wireId, out var entry))
        {
            entry = (maxHealth, new HashSet<uint>());
            _evaded[wireId] = entry;
        }
        entry.EndedAuras.UnionWith(ClearEffectsOn(wireId));
    }

    private List<string> EvadedCast(WebNetworkId casterId, string targetId, uint spellId, SpellDefinition spell)
    {
        var target = WebNetworkId.FromString(targetId);
        var responses = new List<string>
        {
            Reply(WebMessageTypes.SpellReleased, new SpellReleasedPayload(spellId, casterId, [target], [], spell.Speed, Aimed: false)),
            Reply(WebMessageTypes.SpellOutcome, new SpellOutcomePayload(casterId, target, spellId, WireCodes.Outcome.Evade, 0)),
        };
        if (spell.CooldownMs > 0)
            responses.Add(Reply(WebMessageTypes.SpellCooldownStarted, new SpellCooldownStartedPayload(spellId, spell.CooldownMs)));
        return responses;
    }

    private void AnnounceEvades(DateTimeOffset now, List<string> responses)
    {
        if (_evaded.Count == 0 || _character is null) return;
        var casterId = WebNetworkId.FromString(PlayerUnitId);
        foreach (var (wireId, (maxHealth, endedAuras)) in _evaded)
        {
            if (!_sight.Contains(wireId) || _combat.IsDead(wireId)) continue;
            var unitId = WebNetworkId.FromString(wireId);
            foreach (var spellId in endedAuras.Order())
                responses.Add(Reply(WebMessageTypes.AuraEnded,
                    new AuraEndedPayload(spellId, unitId, casterId)));
            var values = new Dictionary<string, int>
            {
                [WireCodes.UnitValue.Health] = maxHealth,
                [WireCodes.UnitValue.MaxHealth] = maxHealth,
            };
            responses.Add(Reply(WebMessageTypes.EntityValuesChanged, new EntityValuesChangedPayload(unitId, values)));
            _dirty.MarkSent(wireId, values);
        }
        _evaded.Clear();
    }
}
