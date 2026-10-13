namespace Ubiquitous.Protocol.Web;

public static partial class WebMessageTypes
{
    public const string ChatJoinChannel = "chat.joinChannel";
    public const string ChatLeaveChannel = "chat.leaveChannel";
    public const string ChatChannelUpdated = "chat.channelUpdated";

    public const string ChatWhisper = "chat.whisper";
    public const string ChatWhisperReceived = "chat.whisperReceived";
    public const string ChatWhisperSent = "chat.whisperSent";
}
