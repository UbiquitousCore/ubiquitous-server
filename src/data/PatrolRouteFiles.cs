using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ubiquitous.Data;

public static class PatrolRouteFiles
{
    public const string DirectoryName = "patrol-routes";

    private const int MinimumStops = 2;
    private const int MillisecondsPerSecond = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Regex SafeFileName = new("^[a-z0-9][a-z0-9_-]{0,63}\\.json$", RegexOptions.CultureInvariant);

    public static bool IsSafeFileName(string? name) => name is not null && SafeFileName.IsMatch(name);

    public static IReadOnlyDictionary<uint, WaypointRouteDefinition> LoadDirectory(string directory)
    {
        var routes = new Dictionary<uint, WaypointRouteDefinition>();
        foreach (var (file, inFile) in LoadDirectoryByFile(directory))
        {
            foreach (var route in inFile)
            {
                if (!routes.TryAdd(route.Id, route))
                    throw new InvalidDataException($"{file}: patrol route {route.Id} is defined twice.");
            }
        }

        return routes;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<WaypointRouteDefinition>> LoadDirectoryByFile(string directory)
    {
        var byFile = new SortedDictionary<string, IReadOnlyList<WaypointRouteDefinition>>(StringComparer.Ordinal);
        if (!Directory.Exists(directory)) return byFile;

        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var name = Path.GetFileName(path);
            byFile[name] = Parse(File.ReadAllText(path), name);
        }

        return byFile;
    }

    public static string Format(ushort mapId, IReadOnlyList<WaypointRouteDefinition> routes)
    {
        static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        var text = new StringBuilder();
        text.Append("{\n  \"mapId\": ").Append(mapId).Append(",\n  \"routes\": [\n");
        for (var r = 0; r < routes.Count; r++)
        {
            var route = routes[r];
            text.Append("    {\n      \"id\": ").Append(route.Id).Append(",\n      \"stops\": [\n");
            for (var s = 0; s < route.Stops.Count; s++)
            {
                var stop = route.Stops[s];
                text.Append("        {\"x\": ").Append(Number(stop.X))
                    .Append(", \"y\": ").Append(Number(stop.Y))
                    .Append(", \"z\": ").Append(Number(stop.Z))
                    .Append(", \"pauseSeconds\": ").Append((stop.PauseMilliseconds / (double)MillisecondsPerSecond).ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(s + 1 < route.Stops.Count ? "},\n" : "}\n");
            }
            text.Append("      ]\n    }").Append(r + 1 < routes.Count ? ",\n" : "\n");
        }

        return text.Append("  ]\n}\n").ToString();
    }

    public static IReadOnlyList<WaypointRouteDefinition> Parse(string json, string source)
    {
        var file = JsonSerializer.Deserialize<RouteFile>(json, JsonOptions)
            ?? throw new InvalidDataException($"{source}: empty patrol route file.");
        if (file.Routes is null) throw new InvalidDataException($"{source}: missing \"routes\".");

        return [.. file.Routes.Select(route => ToDefinition(route, file.MapId, source))];
    }

    private static WaypointRouteDefinition ToDefinition(RouteEntry route, ushort mapId, string source)
    {
        var stops = route.Stops ?? [];
        if (stops.Count < MinimumStops)
            throw new InvalidDataException($"{source}: patrol route {route.Id} needs at least {MinimumStops} stops.");

        foreach (var stop in stops)
        {
            if (!float.IsFinite(stop.X) || !float.IsFinite(stop.Y) || !float.IsFinite(stop.Z))
                throw new InvalidDataException($"{source}: patrol route {route.Id} has a non-finite stop.");
            if (!double.IsFinite(stop.PauseSeconds) || stop.PauseSeconds < 0)
                throw new InvalidDataException($"{source}: patrol route {route.Id} has a negative pause.");
        }

        return new WaypointRouteDefinition(
            route.Id,
            mapId,
            [.. stops.Select(stop => new WaypointRouteStop(stop.X, stop.Y, stop.Z, (int)Math.Round(stop.PauseSeconds * MillisecondsPerSecond)))],
            LoopToStopIndex: 0);
    }

    private sealed record RouteFile(ushort MapId, IReadOnlyList<RouteEntry>? Routes);

    private sealed record RouteEntry(uint Id, IReadOnlyList<StopEntry>? Stops);

    private sealed record StopEntry(float X, float Y, float Z, double PauseSeconds);
}
