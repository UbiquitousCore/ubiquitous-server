using System.Collections.Concurrent;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private sealed record PartyShare(uint? KilledEntry, int Xp, int Money);

    // Filled from a teammate's session and emptied on this one's own tick, so
    // nothing outside this session touches its character.
    private readonly ConcurrentQueue<PartyShare> _partyShares = new();

    private IReadOnlyList<string> RewardKill(uint victimEntry, CreatureDefinition? creature, string corpseId, DateTimeOffset now)
    {
        var responses = new List<string>();
        foreach (var update in _quests.RecordKill(victimEntry, _content.Quests))
            responses.Add(Reply(WebMessageTypes.QuestUpdated, QuestUpdatePayloadFor(update)));
        if (creature is null || _character is null) return responses;

        var teammates = TeammatesNear(corpseId);
        var earners = new List<PartyEarner> { new(PlayerUnitId, _level) };
        earners.AddRange(teammates.Select(mate => new PartyEarner(mate.PlayerNetworkIdText!, mate.Presence?.Level ?? 1)));
        var shares = PartyRewards.KillXpShares(earners, creature.Level, creature.Rank == CreatureRank.Elite);
        responses.AddRange(GrantXp(shares[PlayerUnitId], now));
        foreach (var mate in teammates)
            mate._partyShares.Enqueue(new PartyShare(victimEntry, shares[mate.PlayerNetworkIdText!], 0));
        return responses;
    }

    private int SplitPickedMoney(string corpseId, int amount)
    {
        var teammates = TeammatesNear(corpseId);
        var split = PartyRewards.SplitMoney(amount, teammates.Count + 1);
        if (split.EachShare > 0)
            foreach (var mate in teammates) mate._partyShares.Enqueue(new PartyShare(null, 0, split.EachShare));
        return split.EachShare + split.PickerExtra;
    }

    private List<PlaySession> TeammatesNear(string corpseId)
    {
        var teammates = new List<PlaySession>();
        if (_hub is null || _world is null) return teammates;
        var at = _shared is not null && _shared.TryGetPosition(corpseId, out var corpse) ? corpse : _world.PlayerPosition;
        foreach (var memberId in PartyMemberIdsExcludingSelf())
        {
            var mate = _hub.FindByCharacterNetworkId(WebNetworkId.FromString(memberId));
            if (mate is null || ReferenceEquals(mate, this) || mate.PlayerNetworkIdText is null) continue;
            if (mate.CurrentMapId != CurrentMapId || mate.CurrentInstanceKey != CurrentInstanceKey) continue;
            if (mate.PlayerPosition is not { } standing
                || standing.DistanceSquaredTo(at) > PartyRewards.ShareRange * PartyRewards.ShareRange) continue;
            teammates.Add(mate);
        }
        return teammates;
    }

    private void TakePartyShares(DateTimeOffset now, List<string> responses)
    {
        while (_partyShares.TryDequeue(out var share))
        {
            if (share.KilledEntry is { } entry)
                foreach (var update in _quests.RecordKill(entry, _content.Quests))
                    responses.Add(Reply(WebMessageTypes.QuestUpdated, QuestUpdatePayloadFor(update)));
            responses.AddRange(GrantXp(share.Xp, now));
            if (share.Money > 0)
            {
                _belongings.Currency += share.Money;
                responses.Add(Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(
                    new Dictionary<string, int> { [WireCodes.UnitValue.Gold] = _belongings.Currency })));
            }
        }
    }
}
