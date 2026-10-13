using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private readonly AmbientService _ambient;

    private string AmbientReply(ushort mapId)
    {
        var state = _ambient.StateFor(mapId);
        return Reply(WebMessageTypes.WorldAmbient, new WorldAmbientPayload(state.Weather, state.Festival));
    }
}
