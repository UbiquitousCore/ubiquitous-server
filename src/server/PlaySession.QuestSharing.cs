using System.Text.Json;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private readonly Dictionary<uint, (string SharerId, string SharerName)> _sharedQuestOffers = new();

    private async Task<IReadOnlyList<string>> ShareQuest(JsonElement payload)
    {
        if (_character is null || _world is null)
            return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before sharing a quest.")];

        var request = WebMessageJson.DeserializePayload<QuestSharePayload>(payload);
        var quest = _content.Quests.FirstOrDefault(candidate => candidate.Id == request.QuestId);
        if (quest is null || !_quests.Holds(request.QuestId))
            return [Error(WireCodes.ProtocolError.InvalidQuest, "You can only share a quest you hold.")];

        var myId = PlayerUnitId;
        var roster = _partyHandler.MyRoster;
        if (roster is null)
            return [Error(WireCodes.ProtocolError.InvalidParty, "You are not in a party.")];

        var responses = new List<string>();
        foreach (var member in roster.Members)
        {
            if (string.Equals(member.Id, myId, StringComparison.Ordinal)) continue;
            var memberId = WebNetworkId.FromString(member.Id);
            var session = _hub?.FindByCharacterNetworkId(memberId);
            var result = session?.Presence is { } presence
                && session.CurrentMapId == _world.MapId
                && InteractionRange.Within(_world.PlayerPosition,
                    new DomainPosition((float)presence.Position.X, (float)presence.Position.Y, (float)presence.Position.Z),
                    QuestService.ShareRange)
                ? session.ReceiveSharedQuest(quest.Id, myId, _character.Name)
                : WireCodes.ShareResult.OutOfReach;

            if (result is null)
            {
                var offer = WebMessageJson.Serialize(WebMessageEnvelope.Create(WebMessageTypes.QuestShareOffered, 0,
                    new QuestShareOfferedPayload(WebNetworkId.FromString(myId), _character.Name,
                        quest.Id, quest.TitleStringId, quest.DetailsStringId, quest.MinLevel)));
                await _hub!.DeliverAsync(session!, offer);
                continue;
            }

            responses.Add(Reply(WebMessageTypes.QuestShareResult,
                new QuestShareResultPayload(memberId, member.Name, quest.Id, result)));
        }

        return responses;
    }

    private string? ReceiveSharedQuest(uint questId, string sharerId, string sharerName)
    {
        if (_character is null) return WireCodes.ShareResult.OutOfReach;
        switch (_quests.CanTake(questId, _content.Quests, _level, _character.Class, _spells.Known))
        {
            case QuestTakeFit.AlreadyHas: return WireCodes.ShareResult.AlreadyHas;
            case QuestTakeFit.NotEligible: return WireCodes.ShareResult.NotEligible;
        }
        _sharedQuestOffers[questId] = (sharerId, sharerName);
        return null;
    }

    private async Task<IReadOnlyList<string>> QuestShareAnswer(JsonElement payload)
    {
        if (_character is null)
            return [Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before answering a shared quest.")];

        var answer = WebMessageJson.DeserializePayload<QuestShareAnswerPayload>(payload);
        if (!_sharedQuestOffers.Remove(answer.QuestId, out var offer))
            return [Error(WireCodes.ProtocolError.InvalidQuest, "Nobody offered you that quest.")];

        var responses = new List<string>();
        var result = WireCodes.ShareResult.Declined;
        if (answer.Accept)
        {
            var accepted = _quests.Accept(answer.QuestId, _content.Quests, HeldCountFor, _level, _character.Class, _spells.Known);
            if (accepted is null)
            {
                result = WireCodes.ShareResult.NotEligible;
                responses.Add(Error(WireCodes.ProtocolError.InvalidQuest, "You can no longer take that quest."));
            }
            else
            {
                result = WireCodes.ShareResult.Accepted;
                responses.Add(Reply(WebMessageTypes.QuestUpdated, QuestUpdatePayloadFor(accepted)));
            }
        }

        var sharer = _hub?.FindByCharacterNetworkId(WebNetworkId.FromString(offer.SharerId));
        if (sharer is not null)
        {
            var told = WebMessageJson.Serialize(WebMessageEnvelope.Create(WebMessageTypes.QuestShareResult, 0,
                new QuestShareResultPayload(WebNetworkId.FromString(PlayerUnitId), _character.Name,
                    answer.QuestId, result)));
            await _hub!.DeliverAsync(sharer, told);
        }

        return responses;
    }
}
