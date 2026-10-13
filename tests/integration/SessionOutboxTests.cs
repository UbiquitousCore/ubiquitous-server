using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class SessionOutboxTests
{
    [Fact]
    public void AReplyIsDescribedAsItsParsedStringWouldBe()
    {
        var outbox = new SessionOutbox();
        var raw = outbox.Reply(WebMessageTypes.EntityMoved, new
        {
            networkId = "player:7",
            nested = new[] { new { who = "player:9" } },
        });

        var described = outbox.Describe(raw)!;
        var parsed = OutboundEvent.From(raw)!;

        Assert.Equal(parsed.Type, described.Type);
        Assert.Equal(parsed.NetworkId, described.NetworkId);
        Assert.Equal(parsed.MentionedPlayerIds, described.MentionedPlayerIds);
        Assert.Equal(parsed.Payload.GetRawText(), described.Payload.GetRawText());
    }

    [Fact]
    public void AStringTheOutboxDidNotBuildIsParsed()
    {
        var described = new SessionOutbox().Describe("{\"type\":\"entity.moved\",\"sequence\":3,\"payload\":{\"networkId\":\"player:1\"}}");

        Assert.Equal(WebMessageTypes.EntityMoved, described!.Type);
        Assert.Equal("player:1", described.NetworkId);
    }
}
