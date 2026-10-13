using Ubiquitous.Data;

namespace Ubiquitous.Server;

public sealed record PatrolRouteSave(int RouteCount, string? Refusal = null);

public sealed class PatrolRouteLibrary
{
    private readonly string? _directory;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, IReadOnlyList<WaypointRouteDefinition>> _byFile = new(StringComparer.Ordinal);
    private Dictionary<uint, WaypointRouteDefinition> _byId = [];

    private PatrolRouteLibrary(
        string? directory, IReadOnlyDictionary<string, IReadOnlyList<WaypointRouteDefinition>> byFile)
    {
        _directory = directory;
        foreach (var (file, routes) in byFile) _byFile[file] = routes;
        _byId = Merged();
    }

    public static PatrolRouteLibrary Load(string directory) =>
        new(directory, PatrolRouteFiles.LoadDirectoryByFile(directory));

    public static PatrolRouteLibrary Fixed(IReadOnlyDictionary<uint, WaypointRouteDefinition> routes) =>
        new(null, new Dictionary<string, IReadOnlyList<WaypointRouteDefinition>> { [string.Empty] = [.. routes.Values] });

    public IReadOnlyDictionary<uint, WaypointRouteDefinition> All
    {
        get { lock (_gate) return _byId; }
    }

    public bool TryGet(uint routeId, out WaypointRouteDefinition route)
    {
        lock (_gate) return _byId.TryGetValue(routeId, out route!);
    }

    public (string FileName, string Text)? FileFor(ushort mapId)
    {
        lock (_gate)
        {
            foreach (var (file, routes) in _byFile)
            {
                if (file.Length > 0 && routes.Count > 0 && routes[0].MapId == mapId)
                    return (file, PatrolRouteFiles.Format(mapId, routes));
            }

            return null;
        }
    }

    public PatrolRouteSave Save(string fileName, string json)
    {
        if (_directory is null) return new(0, "This server has no content directory to save patrol routes in.");
        if (!PatrolRouteFiles.IsSafeFileName(fileName))
            return new(0, "A route file is named in lower case, like map-13.json.");

        IReadOnlyList<WaypointRouteDefinition> routes;
        try
        {
            routes = PatrolRouteFiles.Parse(json, fileName);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            return new(0, ex.Message);
        }

        if (routes.Count == 0) return new(0, "The file has no routes to save.");

        lock (_gate)
        {
            foreach (var (file, others) in _byFile)
            {
                if (string.Equals(file, fileName, StringComparison.Ordinal)) continue;
                var taken = others.FirstOrDefault(other => routes.Any(route => route.Id == other.Id));
                if (taken is not null) return new(0, $"Route {taken.Id} is already placed in {file}.");
            }

            var mapId = routes.Count > 0 ? routes[0].MapId : (ushort)0;
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, fileName);
            var staging = path + ".saving";
            File.WriteAllText(staging, PatrolRouteFiles.Format(mapId, routes));
            File.Move(staging, path, overwrite: true);

            _byFile[fileName] = routes;
            _byId = Merged();
            return new(routes.Count);
        }
    }

    private Dictionary<uint, WaypointRouteDefinition> Merged()
    {
        var merged = new Dictionary<uint, WaypointRouteDefinition>();
        foreach (var routes in _byFile.Values)
        {
            foreach (var route in routes) merged[route.Id] = route;
        }

        return merged;
    }
}
