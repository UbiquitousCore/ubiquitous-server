using Ubiquitous.Data;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    // A pet does nothing but follow, so the server holds only which one is out; each client walks the body.
    private (uint SpellId, uint Entry)? _pet;

    public uint? PetEntry => _pet?.Entry;

    private IReadOnlyList<string> ResolvePet(
        WebNetworkId casterId, uint spellId, SpellDefinition spell, uint entry, DateTimeOffset now)
    {
        _pet = (spellId, entry);
        MarkUnitValues(PlayerUnitId, new Dictionary<string, int> { [WireCodes.UnitValue.Pet] = checked((int)entry) }, now);
        return ResolveReleaseOnly(casterId, casterId.Text!, spellId, spell);
    }

    // Own choice: casting the pet that is out sends it away, as casting a ridden mount gets down.
    private IReadOnlyList<string> DismissPet(DateTimeOffset now)
    {
        _pet = null;
        MarkUnitValues(PlayerUnitId, new Dictionary<string, int> { [WireCodes.UnitValue.Pet] = 0 }, now);
        var responses = new List<string>();
        FlushUnitValues(now, responses);
        return responses;
    }
}
