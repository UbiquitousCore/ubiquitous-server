using System.Collections.Frozen;
using Ubiquitous.Game;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

internal static class EventAudience
{
    private const string PlayerIdPrefix = "player:";

    private static readonly FrozenSet<string> MapWideTypes = new[]
    {
        WebMessageTypes.EntityMoved,
        WebMessageTypes.EntitySpawned,
        WebMessageTypes.EntityRemoved,
        WebMessageTypes.EntityValuesChanged,
        WebMessageTypes.EntityAppearanceChanged,
        WebMessageTypes.AttackMeleeStart,
        WebMessageTypes.AttackSwingResult,
        WebMessageTypes.AttackShotResult,
        WebMessageTypes.SpellOutcome,
        WebMessageTypes.SpellCasting,
        WebMessageTypes.SpellReleased,
        WebMessageTypes.SpellInterrupted,
        WebMessageTypes.SpellFailed,
        WebMessageTypes.AuraApplied,
        WebMessageTypes.AuraEnded,
        WebMessageTypes.AttackHalted,
        WebMessageTypes.AttackEnterCombat,
        WebMessageTypes.AttackExitCombat,
        WebMessageTypes.UnitDied,
        WebMessageTypes.PlayerRevived,
        WebMessageTypes.LootBusy,
        WebMessageTypes.LootSlotEmptied,
        WebMessageTypes.DungeonCompleted,
        WebMessageTypes.EmotePerformed,
    }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsMapWide(OutboundEvent outbound)
    {
        // A creature's yell (it names its shouter) and a dungeon's notice are heard by everyone
        // there; players' own chat, a player's yell included, is routed apart by range.
        if (outbound.Type == WebMessageTypes.ChatReceived)
            return outbound.ChatChannel is ChatService.Notice
                || (outbound.ChatChannel is ChatService.Yell && NamesItsShouter(outbound.Payload));
        var isPresenceChange = outbound.Type is WebMessageTypes.EntitySpawned or WebMessageTypes.EntityRemoved;
        if (isPresenceChange && outbound.NetworkId is { Length: > 0 } id
            && !id.StartsWith(PlayerIdPrefix, StringComparison.Ordinal))
            return false;
        return MapWideTypes.Contains(outbound.Type);
    }

    private static bool NamesItsShouter(System.Text.Json.JsonElement payload) =>
        payload.ValueKind == System.Text.Json.JsonValueKind.Object
        && payload.TryGetProperty("senderId", out var sender)
        && sender.ValueKind == System.Text.Json.JsonValueKind.String;
}
