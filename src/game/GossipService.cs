using Ubiquitous.Data;

namespace Ubiquitous.Game;

public sealed record GossipRequest(string NetworkId, uint MenuId, int Choice);

public sealed record GossipEvent(string NetworkId, uint Entry);

public enum GossipPickKind
{
    NothingHappens,

    Teleport,

    RunsScript,
}

public sealed record GossipPick(GossipPickKind Kind, GossipTeleportDefinition? Teleport = null, uint Script = 0);

// What a chat-menu script does on this server, by the script id a choice slot names.
public static class GossipScripts
{
    // The Orc Teleporter's and Akhan's offer to send the player to the Town of Silence.
    public const uint SendToTownOfSilence = 562;

    // Tharrath's offer, in the swamp's dungeon, to lead the way to his ritual.
    public const uint StartsRitual = 591;

    // The teleport spot the client's tables name "HF-Silent town".
    public const uint TownOfSilenceSpot = 1;

    public static readonly IReadOnlySet<uint> ServerEvents = new HashSet<uint> { StartsRitual };

    public static readonly IReadOnlyDictionary<uint, uint> TeleportSpots = new Dictionary<uint, uint>
    {
        [SendToTownOfSilence] = TownOfSilenceSpot,
    };
}

public sealed class GossipService
{
    public const int ChoiceSlots = 6;

    public GossipEvent? Open(string networkId, uint entryId, IReadOnlyDictionary<uint, NpcDefinition> definitions)
    {
        if (!definitions.TryGetValue(entryId, out var npc) || (npc.Flags == NpcFlags.None && !npc.OffersQuests)) return null;
        return new GossipEvent(networkId, entryId);
    }

    public GossipPick? Pick(
        GossipRequest request,
        uint entryId,
        IReadOnlyDictionary<uint, NpcDefinition> npcs,
        IReadOnlyDictionary<uint, GossipMenuDefinition> menus,
        IReadOnlyDictionary<uint, TeleportStopDefinition> spots)
    {
        if (request.Choice is < 1 or > ChoiceSlots) return null;
        if (!npcs.TryGetValue(entryId, out var npc) || (npc.Flags == NpcFlags.None && !npc.OffersQuests)) return null;
        if (!Reachable(npc.GreetingMenuId, request.MenuId, menus, out var menu)) return null;

        var teleport = menu.Teleports.FirstOrDefault(offer => offer.Choice == request.Choice)
            ?? ScriptedTeleport(menu, request.Choice, spots);
        var script = menu.Scripts?.ElementAtOrDefault(request.Choice - 1) ?? 0;
        if (teleport is null && GossipScripts.ServerEvents.Contains(script))
            return new GossipPick(GossipPickKind.RunsScript, Script: script);
        return teleport is null || teleport.Currency != 0
            ? new GossipPick(GossipPickKind.NothingHappens)
            : new GossipPick(GossipPickKind.Teleport, teleport);
    }

    private static GossipTeleportDefinition? ScriptedTeleport(
        GossipMenuDefinition menu, int choice, IReadOnlyDictionary<uint, TeleportStopDefinition> spots)
    {
        var script = menu.Scripts?.ElementAtOrDefault(choice - 1) ?? 0;
        if (!GossipScripts.TeleportSpots.TryGetValue(script, out var spotId) || !spots.TryGetValue(spotId, out var spot))
            return null;
        return new GossipTeleportDefinition(choice, 0, spot.MapId, spot.X, spot.Y, spot.Z, spot.Yaw, 0, 0);
    }

    private static bool Reachable(
        uint greeting,
        uint target,
        IReadOnlyDictionary<uint, GossipMenuDefinition> menus,
        out GossipMenuDefinition found)
    {
        found = null!;
        if (greeting == 0) return false;
        var seen = new HashSet<uint> { greeting };
        var queue = new Queue<uint>([greeting]);
        while (queue.TryDequeue(out var id))
        {
            if (!menus.TryGetValue(id, out var menu)) continue;
            if (id == target)
            {
                found = menu;
                return true;
            }

            foreach (var next in menu.SubMenus)
            {
                if (seen.Add(next)) queue.Enqueue(next);
            }
        }

        return false;
    }
}
