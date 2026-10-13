using System.Globalization;
using System.Text.RegularExpressions;
using Ubiquitous.Data;

namespace Ubiquitous.AssetExtractor;

public static partial class GossipMenuTableMapper
{
    private const int StringColumn = 1;
    private const int FirstScriptColumn = 2;
    private const int FirstSubMenuColumn = 8;
    private const int Slots = 6;

    public sealed record Result(IReadOnlyList<GossipMenuDefinition> Menus, int DroppedTeleports);

    public static Result MapMenus(
        FTableDocument menuRows,
        IReadOnlyDictionary<uint, string> strings,
        IReadOnlyDictionary<uint, ushort> mapByZone)
    {
        var menus = new List<GossipMenuDefinition>();
        var seen = new HashSet<uint>();
        int dropped = 0;
        for (int i = 0; i < menuRows.Rows.Count; i++)
        {
            var row = menuRows.Rows[i];
            uint id = RequireUInt(row, 0, i, "id");
            if (!seen.Add(id)) throw new FTableFormatException($"Duplicate chat_menu id {id}.");

            var text = strings.TryGetValue(RequireUInt(row, StringColumn, i, "string"), out var found)
                ? found
                : string.Empty;

            var subMenus = new List<uint>();
            foreach (Match link in LinkTag().Matches(text))
            {
                if (!TryLeadingInt(link.Groups[1].Value, out var column) || column < 1 || column > Slots)
                    continue;
                uint subMenu = RequireUInt(row, FirstSubMenuColumn + column - 1, i, "sub-menu");
                if (subMenu != 0 && !subMenus.Contains(subMenu)) subMenus.Add(subMenu);
            }

            var teleports = new List<GossipTeleportDefinition>();
            foreach (Match tag in TeleportTag().Matches(text))
            {
                var offer = ReadTeleport(tag.Groups[1].Value, mapByZone);
                if (offer is null || teleports.Any(existing => existing.Choice == offer.Choice))
                {
                    dropped++;
                    continue;
                }

                teleports.Add(offer);
            }

            var scripts = Enumerable.Range(FirstScriptColumn, Slots)
                .Select(column => column < row.Count ? row[column] switch { uint script => script, int script when script > 0 => (uint)script, _ => 0u } : 0u)
                .ToList();
            menus.Add(new GossipMenuDefinition(id, subMenus, teleports, scripts.Any(script => script != 0) ? scripts : null));
        }

        return new Result(menus, dropped);
    }

    private static GossipTeleportDefinition? ReadTeleport(string attributes, IReadOnlyDictionary<uint, ushort> mapByZone)
    {
        var parts = attributes.Split(',');
        if (!TryLeadingInt(parts[0], out var choice) || choice < 1 || choice > Slots) return null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in parts.Skip(1))
        {
            var at = part.IndexOf('=');
            if (at > 0) values.TryAdd(part[..at].Trim(), part[(at + 1)..].Trim());
        }

        if (!TryUInt(values, "topzone", out var zone) || !mapByZone.TryGetValue(zone, out var mapId)) return null;
        if (!TryFloat(values, "x", out var x) || !TryFloat(values, "y", out var y) || !TryFloat(values, "z", out var z))
            return null;
        var yaw = TryFloat(values, "o", out var o) ? o : 0f;
        var currency = TryUInt(values, "moneytype", out var type) ? type : 0u;
        var cost = TryUInt(values, "moneycount", out var count) ? count : 0u;
        if (cost > int.MaxValue) return null;

        return new GossipTeleportDefinition(choice, zone, mapId, x, y, z, yaw, currency, (int)cost);
    }

    private static bool TryLeadingInt(string value, out int result) =>
        int.TryParse(value.Split(',')[0].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);

    private static bool TryUInt(IReadOnlyDictionary<string, string> values, string key, out uint result)
    {
        result = 0;
        return values.TryGetValue(key, out var text)
            && uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryFloat(IReadOnlyDictionary<string, string> values, string key, out float result)
    {
        result = 0;
        return values.TryGetValue(key, out var text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            && float.IsFinite(result);
    }

    private static uint RequireUInt(IReadOnlyList<object> row, int column, int index, string field)
    {
        if (column >= 0 && column < row.Count)
        {
            switch (row[column])
            {
                case uint value: return value;
                case int value when value >= 0: return (uint)value;
            }
        }

        throw new FTableFormatException($"chat_menu row {index} has no usable {field}.");
    }

    [GeneratedRegex("\u0012link_menu=([^\u0013]*)\u0013")]
    private static partial Regex LinkTag();

    [GeneratedRegex("\u0012teleport=([^\u0013]*)\u0013")]
    private static partial Regex TeleportTag();
}
