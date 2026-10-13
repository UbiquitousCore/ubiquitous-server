using System.Runtime.CompilerServices;
using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed class SessionOutbox(Action<object>? observe = null)
{
    private int _sequence;

    // The hub routes a reply by its type and payload; keeping the envelope this
    // outbox already built spares parsing the string it was just turned into.
    private readonly ConditionalWeakTable<string, WebMessageEnvelope> _built = new();

    public int NextSequence() => _sequence++;

    public void ContinueFrom(SessionOutbox earlier) => _sequence = Math.Max(_sequence, earlier._sequence);

    public string Reply(string type, object payload)
    {
        observe?.Invoke(payload);
        var envelope = WebMessageEnvelope.Create(type, _sequence++, payload);
        var raw = WebMessageJson.Serialize(envelope);
        _built.AddOrUpdate(raw, envelope);
        return raw;
    }

    public OutboundEvent? Describe(string raw) =>
        _built.TryGetValue(raw, out var envelope)
            ? OutboundEvent.Of(envelope)
            : OutboundEvent.From(raw);

    public string Error(string code, string reason) =>
        Reply(WebMessageTypes.ProtocolError, new { code, reason });

    public static string Unsequenced(string type, object payload) =>
        WebMessageJson.Serialize(WebMessageEnvelope.Create(type, 0, payload));
}
