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
    }.ToFrozenSet(StringComparer.Ordinal);

    // The moment-to-moment events a bot only acts on when they happen near it or name it;
    // arrivals, departures and notices still reach it.
    public static bool IsLocalToBots(OutboundEvent outbound) =>
        outbound.Type is WebMessageTypes.EntityMoved or WebMessageTypes.EntityValuesChanged
            or WebMessageTypes.EntityAppearanceChanged or WebMessageTypes.EmotePerformed
            or WebMessageTypes.AttackMeleeStart or WebMessageTypes.AttackSwingResult or WebMessageTypes.AttackShotResult
            or WebMessageTypes.SpellOutcome or WebMessageTypes.SpellCasting or WebMessageTypes.SpellReleased
            or WebMessageTypes.SpellInterrupted or WebMessageTypes.SpellFailed
            or WebMessageTypes.AuraApplied or WebMessageTypes.AuraEnded
            or WebMessageTypes.AttackEnterCombat or WebMessageTypes.AttackExitCombat or WebMessageTypes.AttackHalted
            or WebMessageTypes.LootBusy or WebMessageTypes.LootSlotEmptied;

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
