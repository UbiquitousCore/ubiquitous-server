using System.Text.Json;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

internal sealed class ChatHandler(ISessionPlayer player, SessionOutbox outbox)
{
    private readonly ChatService _chat = new();
    private readonly ChatWhisperService _whispers = new();

    public bool IsJoined(string channel) => _chat.IsJoined(channel);

    public string Say(JsonElement payload, bool inParty)
    {
        if (player.Character is null) return NotInWorld();
        var request = WebMessageJson.DeserializePayload<ChatPayload>(payload);
        if (ChatService.IsPartyChannel(request.Channel ?? string.Empty) && !inParty)
            return outbox.Error(WireCodes.ProtocolError.InvalidChat, "Party chat requires being in a party.");
        var chat = _chat.Create(new ChatCommand(request.Channel ?? string.Empty, request.Text), player.Character.Name);
        return chat is null
            ? outbox.Error(WireCodes.ProtocolError.InvalidChat, "Chat text is required, and the channel must be a known channel this session has joined.")
            : outbox.Reply(WebMessageTypes.ChatReceived, new ChatReceivedPayload(chat.Channel, chat.Sender, chat.Text));
    }

    public async Task<IReadOnlyList<string>> Whisper(JsonElement payload)
    {
        if (player.Character is null) return [NotInWorld()];
        var request = WebMessageJson.DeserializePayload<ChatWhisperPayload>(payload);

        var result = _whispers.Validate(player.UnitId, request.TargetId, request.Text);
        switch (result.Outcome)
        {
            case ChatWhisperService.ValidateOutcome.MissingTarget:
                return [outbox.Error(WireCodes.ProtocolError.InvalidTarget, "A whisper target is required.")];
            case ChatWhisperService.ValidateOutcome.SelfWhisper:
                return [outbox.Error(WireCodes.ProtocolError.InvalidTarget, "You cannot whisper yourself.")];
            case ChatWhisperService.ValidateOutcome.EmptyText:
                return [outbox.Error(WireCodes.ProtocolError.InvalidChat, "Whisper text is required.")];
        }

        var whisper = result.Event!;
        var targetSession = player.Hub?.FindByCharacterNetworkId(WebNetworkId.FromString(whisper.TargetId));
        if (targetSession?.Presence is not { } targetPresence)
            return [outbox.Error(WireCodes.ProtocolError.InvalidTarget, "That player is not available.")];
        if (await targetSession.IgnoresAsync(player.Character.Guid))
            return [outbox.Reply(WebMessageTypes.ChatWhisperSent,
                new ChatWhisperSentPayload(targetPresence.NetworkId, targetPresence.Name, whisper.Text))];

        if (player.Hub is not null)
        {
            await player.Hub.DeliverAsync(targetSession, SessionOutbox.Unsequenced(WebMessageTypes.ChatWhisperReceived,
                new ChatWhisperReceivedPayload(WebNetworkId.FromString(player.UnitId), player.Character.Name, whisper.Text)));
        }

        return [outbox.Reply(WebMessageTypes.ChatWhisperSent,
            new ChatWhisperSentPayload(targetPresence.NetworkId, targetPresence.Name, whisper.Text))];
    }

    public string Join(JsonElement payload)
    {
        if (player.Character is null) return NotInWorld();
        var request = WebMessageJson.DeserializePayload<ChatChannelPayload>(payload);
        return _chat.Join(request.Channel)
            ? outbox.Reply(WebMessageTypes.ChatChannelUpdated, new ChatChannelUpdatedPayload(request.Channel.Trim().ToLowerInvariant(), true))
            : outbox.Error(WireCodes.ProtocolError.InvalidChannel, "Unknown chat channel, or a channel that is always on and cannot be joined.");
    }

    public string Leave(JsonElement payload)
    {
        if (player.Character is null) return NotInWorld();
        var request = WebMessageJson.DeserializePayload<ChatChannelPayload>(payload);
        return _chat.Leave(request.Channel)
            ? outbox.Reply(WebMessageTypes.ChatChannelUpdated, new ChatChannelUpdatedPayload(request.Channel.Trim().ToLowerInvariant(), false))
            : outbox.Error(WireCodes.ProtocolError.InvalidChannel, "Unknown chat channel, or a channel that is always on and cannot be left.");
    }

    private string NotInWorld() => outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before chat.");
}
