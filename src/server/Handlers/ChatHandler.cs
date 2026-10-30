using System.Text.Json;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

internal sealed class ChatHandler(ISessionPlayer player, SessionOutbox outbox, GuildService guilds)
{
    private readonly ChatService _chat = new();
    private readonly ChatWhisperService _whispers = new();

    public bool IsJoined(string channel) => _chat.IsJoined(channel);

    public async Task<string> Say(JsonElement payload, bool inParty)
    {
        if (player.Character is null) return NotInWorld();
        var request = WebMessageJson.DeserializePayload<ChatPayload>(payload);
        var channel = request.Channel ?? string.Empty;
        if (ChatService.IsPartyChannel(channel) && !inParty)
            return outbox.Error(WireCodes.ProtocolError.InvalidChat, "Party chat requires being in a party.");
        if (ChatService.IsGuildChannel(channel) && await guilds.RosterOf(player.UnitId) is null)
            return outbox.Error(WireCodes.ProtocolError.InvalidChat, "Guild chat requires being in a guild.");
        var sent = _chat.Send(new ChatCommand(channel, request.Text), player.Character.Name, player.Now,
            exemptFromLimits: player.IsAdmin);
        return sent switch
        {
            { Event: { } chat } => outbox.Reply(WebMessageTypes.ChatReceived, new ChatReceivedPayload(chat.Channel, chat.Sender, chat.Text)),
            // The game's own words for the flood limit.
            { Refusal: ChatRefusal.TooFrequent } => outbox.Error(WireCodes.ProtocolError.ChatTooFrequent, "You speak too frequently, please hold on."),
            _ => outbox.Error(WireCodes.ProtocolError.InvalidChat, "Chat text is required, and the channel must be a known channel this session has joined."),
        };
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
            ? outbox.Reply(WebMessageTypes.ChatChannelUpdated, new ChatChannelUpdatedPayload(ChatService.Normalize(request.Channel), true))
            : outbox.Error(WireCodes.ProtocolError.InvalidChannel, "Unknown chat channel, or a channel that is always on and cannot be joined.");
    }

    public string Leave(JsonElement payload)
    {
        if (player.Character is null) return NotInWorld();
        var request = WebMessageJson.DeserializePayload<ChatChannelPayload>(payload);
        return _chat.Leave(request.Channel)
            ? outbox.Reply(WebMessageTypes.ChatChannelUpdated, new ChatChannelUpdatedPayload(ChatService.Normalize(request.Channel), false))
            : outbox.Error(WireCodes.ProtocolError.InvalidChannel, "Unknown chat channel, or a channel that is always on and cannot be left.");
    }

    private string NotInWorld() => outbox.Error(WireCodes.ProtocolError.NotInWorld, "Character selection is required before chat.");
}
