using Ubiquitous.Data;

namespace Ubiquitous.Game;

public enum TeleportRefusal
{
    None,
    NotVisible,
    UnknownNpc,
    NotTeleporter,
}

public sealed record TeleportResult(
    string NetworkId,
    bool Ok,
    TeleportRefusal Refusal);

public sealed class TeleportService
{
    public TeleportResult Request(
        string networkId,
        IReadOnlyDictionary<string, uint> visibleEntries,
        IReadOnlyDictionary<uint, NpcDefinition> definitions)
    {
        if (!visibleEntries.TryGetValue(networkId, out var entryId))
            return Refused(networkId, TeleportRefusal.NotVisible);
        if (!definitions.TryGetValue(entryId, out var npc))
            return Refused(networkId, TeleportRefusal.UnknownNpc);
        if (!npc.Flags.HasFlag(NpcFlags.Teleporter))
            return Refused(networkId, TeleportRefusal.NotTeleporter);

        return new TeleportResult(networkId, true, TeleportRefusal.None);
    }

    private static TeleportResult Refused(string networkId, TeleportRefusal refusal) =>
        new(networkId, false, refusal);
}
