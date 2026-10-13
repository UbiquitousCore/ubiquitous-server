using System.Text.Json;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.WebProtocol.Tests;

public class WebMessageJsonTests
{
    [Fact]
    public void Envelope_SerializesWithTypeSequenceAndPayload()
    {
        var message = WebMessageEnvelope.Create("player.move", 123, new { x = 1.5, y = 2.0, z = 10.25 });

        string json = WebMessageJson.Serialize(message);

        Assert.Contains("\"type\":\"player.move\"", json);
        Assert.Contains("\"sequence\":123", json);
    }

    [Fact]
    public void Envelope_Roundtrips_PreservingPayload()
    {
        var original = WebMessageEnvelope.Create("world.entered", 42, new { mapId = 13 });

        var parsed = WebMessageJson.Deserialize(WebMessageJson.Serialize(original));

        Assert.Equal(original.Type, parsed.Type);
        Assert.Equal(original.Sequence, parsed.Sequence);
        Assert.Equal(13, parsed.Payload.GetProperty("mapId").GetInt32());
    }

    [Fact]
    public void Deserialize_Garbage_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => WebMessageJson.Deserialize("{not json"));
    }

    [Fact]
    public void Deserialize_MissingPayload_NormalizesToObject()
    {
        var parsed = WebMessageJson.Deserialize("{\"type\":\"characters.list\",\"sequence\":0}");

        Assert.Equal(JsonValueKind.Object, parsed.Payload.ValueKind);
    }

    [Theory]
    [InlineData("{\"type\":\"\",\"sequence\":0,\"payload\":{}}")]
    [InlineData("{\"type\":\"player.move\",\"sequence\":-1,\"payload\":{}}")]
    [InlineData("{\"type\":\"player.move\",\"sequence\":0,\"payload\":[]}")]
    [InlineData("{\"type\":\"player.move\",\"sequence\":0,\"payload\":\"bad\"}")]
    public void Deserialize_InvalidEnvelope_Throws(string json)
    {
        Assert.ThrowsAny<JsonException>(() => WebMessageJson.Deserialize(json));
    }

    [Fact]
    public void FirstMilestoneMessageNames_MatchWebClientVocabulary()
    {
        Assert.Equal("account.login", WebMessageTypes.AccountLogin);
        Assert.Equal("characters.list", WebMessageTypes.CharactersList);
        Assert.Equal("characters.choose", WebMessageTypes.CharactersChoose);
        Assert.Equal("world.entered", WebMessageTypes.WorldEntered);
        Assert.Equal("entity.spawned", WebMessageTypes.EntitySpawned);
        Assert.Equal("entity.moved", WebMessageTypes.EntityMoved);
        Assert.Equal("entity.removed", WebMessageTypes.EntityRemoved);
        Assert.Equal("player.move", WebMessageTypes.PlayerMove);
        Assert.Equal("move.accepted", WebMessageTypes.MoveAccepted);
    }

    [Fact]
    public void PlayerMove_UsesFlatPositionFields()
    {
        var payload = new PlayerMovePayload(
            1.5, 2.0, 10.25, 0.75, true, MovementState.Walking,
            new Position(0, 0, 6), 6);

        var message = WebMessageEnvelope.Create(WebMessageTypes.PlayerMove, 3, payload);
        var json = WebMessageJson.Serialize(message);

        Assert.Contains("\"x\":1.5", json);
        Assert.Contains("\"velocity\":{\"x\":0,\"y\":0,\"z\":6}", json);
        Assert.DoesNotContain("\"position\"", json);
    }

    [Fact]
    public void EntityMove_PreservesOptionalFieldsAndStringNetworkId()
    {
        var payload = new EntityMovedPayload(
            WebNetworkId.FromString("2:000000c8"),
            Position: new Position(1, 2, 3),
            Moving: false,
            State: MovementState.Standing);

        var envelope = WebMessageEnvelope.Create(WebMessageTypes.EntityMoved, 4, payload);
        var parsed = WebMessageJson.Deserialize(WebMessageJson.Serialize(envelope));
        var json = parsed.Payload;

        Assert.Equal("2:000000c8", json.GetProperty("networkId").GetString());
        Assert.Equal(3, json.GetProperty("position").GetProperty("z").GetInt32());
        Assert.False(json.GetProperty("moving").GetBoolean());
        Assert.False(json.TryGetProperty("yaw", out _));
    }

    [Fact]
    public void WorldEntryAndRemoval_RoundtripNumericNetworkId()
    {
        var entered = WebMessageEnvelope.Create(
            WebMessageTypes.WorldEntered,
            5,
            new WorldEnteredPayload(13, new Position(1, 2, 3), WebNetworkId.FromNumber(42)));
        var removed = WebMessageEnvelope.Create(
            WebMessageTypes.EntityRemoved,
            6,
            new EntityRemovedPayload(WebNetworkId.FromNumber(42)));

        var enteredJson = WebMessageJson.Deserialize(WebMessageJson.Serialize(entered)).Payload;
        var removedJson = WebMessageJson.Deserialize(WebMessageJson.Serialize(removed)).Payload;

        Assert.Equal(42, enteredJson.GetProperty("self").GetInt32());
        Assert.Equal(42, removedJson.GetProperty("networkId").GetInt32());
    }

    [Fact]
    public void Create_WithABlankTypeOrNegativeSequence_ThrowsAnArgumentExceptionNotAJsonException()
    {
        Assert.ThrowsAny<ArgumentException>(() => WebMessageEnvelope.Create(" ", 0));
        Assert.ThrowsAny<ArgumentException>(() => WebMessageEnvelope.Create("session.pong", -1));
    }
}
