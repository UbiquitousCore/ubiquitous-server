using System.Reflection;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Integration.Tests;

public sealed class ProtocolErrorCodeTests
{
    private static readonly string[] Expected =
    [
        "barrier_closed",
        "cannot_use",
        "chat_too_frequent",
        "duel_refused",
        "dungeon_mode_refused",
        "dungeon_reset_refused",
        "forbidden",
        "insufficient_funds",
        "internal",
        "invalid_auctioneer",
        "invalid_channel",
        "invalid_chat",
        "invalid_emote",
        "invalid_gossip",
        "invalid_guild",
        "invalid_instance",
        "invalid_inventory",
        "invalid_mail",
        "invalid_party",
        "invalid_payload",
        "invalid_quest",
        "invalid_report",
        "invalid_state",
        "invalid_staticobject",
        "invalid_summon",
        "invalid_target",
        "invalid_teleport",
        "invalid_trade",
        "invalid_vendor",
        "malformed",
        "not_authenticated",
        "not_in_world",
        "patrol_refused",
        "rate_limited",
        "trade_refused",
        "unstuck_refused",
        "unsupported",
        "upgrade_refused",
    ];

    [Fact]
    public void TheProtocolErrorCodesAreExactlyTheOnesTheClientIsToldAbout()
    {
        var actual = typeof(WireCodes.ProtocolError)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(Expected.Order(StringComparer.Ordinal), actual);
    }
}
