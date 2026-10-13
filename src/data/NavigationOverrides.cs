using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ubiquitous.Data;

public enum NavigationEditKind
{
    Walkable,

    Unwalkable,
}

public sealed record NavigationEdit(
    NavigationEditKind Kind,
    float MinX = 0f,
    float MaxX = 0f,
    float MinZ = 0f,
    float MaxZ = 0f,
    float? MinY = null,
    float? MaxY = null,
    float? FloorY = null,
    string? Note = null);

public sealed record NavigationOverrides(ushort MapId, IReadOnlyList<NavigationEdit> Edits);

public static class NavigationOverrideFile
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static NavigationOverrides Parse(string json)
    {
        NavigationOverrides? overrides;
        try
        {
            overrides = JsonSerializer.Deserialize<NavigationOverrides>(json, Options);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"Walkable-ground corrections are not readable: {error.Message}", error);
        }

        if (overrides is null) throw new InvalidDataException("Walkable-ground corrections are empty.");

        foreach (var edit in overrides.Edits)
        {
            Validate(edit);
        }

        return overrides;
    }

    public static NavigationOverrides Load(string path) => Parse(File.ReadAllText(path));

    private static void Validate(NavigationEdit edit)
    {
        switch (edit.Kind)
        {
            case NavigationEditKind.Walkable when edit.FloorY is null:
                throw new InvalidDataException("A walkable correction needs floorY: the height the floor it adds sits at.");
            case NavigationEditKind.Walkable:
            case NavigationEditKind.Unwalkable:
                if (edit.MaxX <= edit.MinX || edit.MaxZ <= edit.MinZ)
                {
                    throw new InvalidDataException(
                        $"A {edit.Kind} correction covers no ground: minX {edit.MinX} to maxX {edit.MaxX}, minZ {edit.MinZ} to maxZ {edit.MaxZ}.");
                }

                break;
            default:
                throw new InvalidDataException($"Unknown walkable-ground correction {edit.Kind}.");
        }
    }
}
