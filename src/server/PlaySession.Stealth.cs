using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    public string? PlayerNetworkIdText => _character is null ? null : PlayerUnitId;

    public bool IsStealthed => PlayerNetworkIdText is { } self && _stealth.IsHeld(self);

    public bool IsTeammateOf(PlaySession other) =>
        other.PlayerNetworkIdText is { } otherId && PartyMemberIdsExcludingSelf().Contains(otherId, StringComparer.Ordinal);

    public bool HidesFrom(PlaySession observer) =>
        !ReferenceEquals(this, observer) && IsStealthed && !IsTeammateOf(observer);

    private bool HiddenFromMe(string? targetId) =>
        targetId is not null && targetId.StartsWith("player:", StringComparison.Ordinal)
        && _hub?.FindByCharacterNetworkId(WebNetworkId.FromString(targetId)) is { } other
        && other.HidesFrom(this);

    private void BreakStealth(DateTimeOffset now, List<string> responses)
    {
        if (PlayerNetworkIdText is not { } self) return;
        var ended = _stealth.Clear(self);
        if (ended.Count == 0) return;
        var unit = WebNetworkId.FromString(self);
        foreach (var spellId in ended)
            responses.Add(Reply(WebMessageTypes.AuraEnded, new AuraEndedPayload(spellId, unit, unit)));
        MarkAuraStateUpdates(now);
    }

    private void DropHiddenTarget(List<string> responses)
    {
        if (PlayerNetworkIdText is not { } self) return;
        if (_combat.CurrentTargetOf(self) is not { } target || !HiddenFromMe(target)) return;
        UnmarkEngaged();
        _combat.Disengage(self);
        responses.Add(Reply(WebMessageTypes.AttackHalted, new AttackHaltedPayload(WebNetworkId.FromString(self))));
        responses.Add(Reply(WebMessageTypes.AttackExitCombat, new AttackExitCombatPayload(WebNetworkId.FromString(self))));
    }
}
