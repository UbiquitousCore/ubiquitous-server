namespace Ubiquitous.Game;

public sealed record AmbientState(string Weather, string? Festival = null);

public sealed class AmbientService
{
    public static readonly AmbientState Default = new("clear");

    private readonly Dictionary<ushort, AmbientState> _byMap = new();

    public AmbientState StateFor(ushort mapId) =>
        _byMap.TryGetValue(mapId, out var state) ? state : Default;

    public void SetState(ushort mapId, AmbientState state) => _byMap[mapId] = state;
}
